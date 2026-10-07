param(
    [switch]$Apply,
    [switch]$BumpPatchVersion,
    [string[]]$Only
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LockPath = Join-Path $Root "third_party\DEPENDENCIES.lock.json"
$PolicyPath = Join-Path $Root "third_party\UPSTREAMS.json"
$VendorScript = Join-Path $PSScriptRoot "vendor-third-party.ps1"
$ProjectPath = Join-Path $Root "DlssNrManager.csproj"

if (!(Test-Path -LiteralPath $LockPath)) { throw "Missing dependency lock: $LockPath" }
if (!(Test-Path -LiteralPath $PolicyPath)) { throw "Missing upstream policy registry: $PolicyPath" }

$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$Policies = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json

$headers = @{
    Accept = "application/vnd.github+json"
    "User-Agent" = "DlssNrManager-UpstreamSync/1.0"
}
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    $headers.Authorization = "Bearer $env:GITHUB_TOKEN"
}

function Invoke-GitHubJson {
    param([Parameter(Mandatory=$true)][string]$Uri)

    $last = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $Uri -Headers $headers -Method Get
        }
        catch {
            $last = $_
            if ($attempt -lt 3) { Start-Sleep -Seconds ([Math]::Pow(2, $attempt - 1)) }
        }
    }

    throw "GitHub API request failed after retries: $Uri :: $last"
}

function Resolve-CommitInfo {
    param(
        [Parameter(Mandatory=$true)][string]$Repository,
        [Parameter(Mandatory=$true)][string]$Ref
    )

    $encoded = [Uri]::EscapeDataString($Ref)
    $commit = Invoke-GitHubJson "https://api.github.com/repos/$Repository/commits/$encoded"
    $sha = ([string]$commit.sha).ToLowerInvariant()
    $tree = ([string]$commit.commit.tree.sha).ToLowerInvariant()

    if ($sha -notmatch "^[0-9a-f]{40}$") {
        throw "GitHub did not return an immutable commit SHA for $Repository@$Ref"
    }

    if ($tree -notmatch "^[0-9a-f]{40}$") {
        throw "GitHub did not return an immutable tree SHA for $Repository@$Ref"
    }

    return [pscustomobject]@{
        Sha = $sha
        Tree = $tree
    }
}

function Get-TagVersion {
    param(
        [Parameter(Mandatory=$true)][string]$Tag,
        [Parameter(Mandatory=$true)][string]$Pattern
    )

    $m = [regex]::Match($Tag, $Pattern)
    if (-not $m.Success) { return $null }

    $value = if ($m.Groups["version"].Success) { $m.Groups["version"].Value } else { $Tag.TrimStart([char[]]"vVnN") }
    $parsed = $null
    if ([Version]::TryParse($value, [ref]$parsed)) { return $parsed }
    return $null
}

function Resolve-UpstreamState {
    param([Parameter(Mandatory=$true)]$Policy)

    $repository = [string]$Policy.repository
    $strategy = [string]$Policy.strategy

    if ($strategy -eq "latest-release") {
        if ($Policy.release_name_regex) {
            $releasePattern = [string]$Policy.release_name_regex
            $release = @(Invoke-GitHubJson "https://api.github.com/repos/$repository/releases?per_page=100") |
                Where-Object {
                    -not $_.draft -and
                    -not $_.prerelease -and
                    (
                        ([string]$_.name -match $releasePattern) -or
                        ([string]$_.tag_name -match $releasePattern)
                    )
                } |
                Sort-Object { [DateTimeOffset]$_.published_at } -Descending |
                Select-Object -First 1

            if (-not $release) {
                throw "No stable release matching '$releasePattern' was found for $repository."
            }
        }
        else {
            $release = Invoke-GitHubJson "https://api.github.com/repos/$repository/releases/latest"
            if ($release.draft -or $release.prerelease) {
                throw "Latest release for $repository is not a stable release."
            }
        }

        $tag = [string]$release.tag_name
        $commit = Resolve-CommitInfo -Repository $repository -Ref $tag
        return [pscustomobject]@{
            Ref = $commit.Sha
            Tree = $commit.Tree
            Version = $tag
            Release = $release
            Branch = $null
        }
    }

    if ($strategy -eq "latest-tag") {
        $tags = @(Invoke-GitHubJson "https://api.github.com/repos/$repository/tags?per_page=100")
        $pattern = if ($Policy.tag_regex) { [string]$Policy.tag_regex } else { "^[vV]?(?<version>\d+(?:\.\d+){1,3})$" }

        $candidates = foreach ($tag in $tags) {
            $version = Get-TagVersion -Tag ([string]$tag.name) -Pattern $pattern
            if ($version) {
                [pscustomobject]@{
                    Tag = [string]$tag.name
                    Version = $version
                }
            }
        }

        $selected = $candidates | Sort-Object Version -Descending | Select-Object -First 1
        if (-not $selected) {
            throw "No stable version tag matched '$pattern' for $repository."
        }

        $commit = Resolve-CommitInfo -Repository $repository -Ref $selected.Tag
        return [pscustomobject]@{
            Ref = $commit.Sha
            Tree = $commit.Tree
            Version = $selected.Tag
            Release = $null
            Branch = $null
        }
    }

    if ($strategy -eq "branch-head" -or $strategy -eq "default-branch-head") {
        $branch = if ($strategy -eq "branch-head") {
            [string]$Policy.branch
        }
        else {
            $repo = Invoke-GitHubJson "https://api.github.com/repos/$repository"
            [string]$repo.default_branch
        }

        if ([string]::IsNullOrWhiteSpace($branch)) {
            throw "No branch could be resolved for $repository."
        }

        $commit = Resolve-CommitInfo -Repository $repository -Ref $branch
        return [pscustomobject]@{
            Ref = $commit.Sha
            Tree = $commit.Tree
            Version = $branch
            Release = $null
            Branch = $branch
        }
    }

    throw "Unsupported upstream strategy '$strategy' for $repository."
}

function Set-JsonProperty {
    param(
        [Parameter(Mandatory=$true)]$Object,
        [Parameter(Mandatory=$true)][string]$Name,
        $Value
    )

    $existing = $Object.PSObject.Properties[$Name]
    if ($existing) {
        $existing.Value = $Value
    }
    else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}

function Update-ReleaseMetadata {
    param(
        [Parameter(Mandatory=$true)]$Entry,
        [Parameter(Mandatory=$true)]$Policy,
        [Parameter(Mandatory=$true)]$State
    )

    if ($Policy.lock_tag_field -and -not [string]::IsNullOrWhiteSpace([string]$State.Version)) {
        Set-JsonProperty -Object $Entry -Name ([string]$Policy.lock_tag_field) -Value ([string]$State.Version)
    }

    if ($Policy.release_asset_pattern -and $State.Release) {
        $pattern = [string]$Policy.release_asset_pattern
        $asset = @($State.Release.assets) |
            Where-Object { [string]$_.name -match $pattern } |
            Select-Object -First 1

        if (-not $asset) {
            throw "No release asset matching '$pattern' was found for $($Policy.repository) $($State.Version)."
        }

        if ($Policy.lock_asset_field) {
            Set-JsonProperty -Object $Entry -Name ([string]$Policy.lock_asset_field) -Value ([string]$asset.name)
        }

        if ($Policy.lock_asset_sha256_field) {
            $digest = [string]$asset.digest
            if ([string]::IsNullOrWhiteSpace($digest) -or -not $digest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release asset $($asset.name) has no GitHub SHA-256 digest."
            }

            Set-JsonProperty -Object $Entry -Name ([string]$Policy.lock_asset_sha256_field) -Value $digest.Substring(7).ToLowerInvariant()
        }
    }
}

function Bump-ApplicationPatchVersion {
    $xmlText = Get-Content -LiteralPath $ProjectPath -Raw
    $match = [regex]::Match($xmlText, "<Version>(?<version>\d+\.\d+\.\d+)</Version>")
    if (-not $match.Success) { throw "DlssNrManager.csproj does not expose a three-part <Version>." }

    $current = [Version]::Parse($match.Groups["version"].Value)
    $next = "$($current.Major).$($current.Minor).$($current.Build + 1)"

    $replacements = @{
        "Version" = $next
        "AssemblyVersion" = "$next.0"
        "FileVersion" = "$next.0"
        "InformationalVersion" = $next
    }

    foreach ($name in $replacements.Keys) {
        $value = [string]$replacements[$name]
        $xmlText = [regex]::Replace(
            $xmlText,
            "<$name>[^<]+</$name>",
            "<$name>$value</$name>",
            1)
    }

    Set-Content -LiteralPath $ProjectPath -Value $xmlText -Encoding UTF8 -NoNewline
    return $next
}

$allEntries = @($Lock.sources) + @($Lock.local_only)
$changes = @()
$notifyOnly = @()

foreach ($policy in @($Policies.sources)) {
    $id = [string]$policy.id

    if ($Only -and $Only.Count -gt 0 -and $Only -notcontains $id) { continue }
    if (-not [bool]$policy.enabled) { continue }
    if ([string]$policy.provider -ne "github") { continue }

    $entry = $allEntries | Where-Object { [string]$_.id -eq $id } | Select-Object -First 1
    if (-not $entry) {
        Write-Warning "Policy '$id' has no matching dependency lock entry."
        continue
    }

    Write-Host "CHECK $id -> $($policy.repository) [$($policy.strategy)]"
    $state = Resolve-UpstreamState -Policy $policy
    $current = ([string]$entry.ref).ToLowerInvariant()

    if ($state.Ref -eq $current) {
        Write-Host "CURRENT $id @ $current"
        continue
    }

    $change = [pscustomobject]@{
        Id = $id
        Group = if ($entry.group) { [string]$entry.group } else { "local-only" }
        OldRef = $current
        NewRef = $state.Ref
        Version = [string]$state.Version
        Promotion = [string]$policy.promotion
    }

    if ([string]$policy.promotion -eq "notify-only") {
        $notifyOnly += $change
        Write-Warning "UPDATE AVAILABLE (notify-only) ${id}: $current -> $($state.Ref) [$($state.Version)]"
        continue
    }

    $changes += $change
    Write-Host "UPDATE ${id}: $current -> $($state.Ref) [$($state.Version)]"

    if ($Apply) {
        $entry.ref = $state.Ref
        if ($entry.PSObject.Properties["tree"]) {
            $entry.tree = $state.Tree
        }
        Update-ReleaseMetadata -Entry $entry -Policy $policy -State $state
    }
}

$newVersion = ""
if ($Apply -and $changes.Count -gt 0) {
    $json = $Lock | ConvertTo-Json -Depth 32
    Set-Content -LiteralPath $LockPath -Value $json -Encoding UTF8

    foreach ($change in $changes) {
        $args = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", $VendorScript,
            "-Replace",
            "-StageImported",
            "-Only", $change.Id
        )

        if ($change.Group -eq "minecraft") {
            $args += "-IncludeMinecraftSources"
        }

        & powershell.exe @args
        if ($LASTEXITCODE -ne 0) {
            throw "Vendoring failed for $($change.Id)."
        }
    }

    if ($BumpPatchVersion) {
        $newVersion = Bump-ApplicationPatchVersion
        git -C $Root add -- "DlssNrManager.csproj"
        if ($LASTEXITCODE -ne 0) { throw "Failed to stage application version bump." }
        Write-Host "APPLICATION VERSION -> $newVersion"
    }
}

Write-Host ""
Write-Host "Upstream check complete. promotable=$($changes.Count) notify_only=$($notifyOnly.Count) apply=$Apply"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "changed=$($changes.Count -gt 0)".ToLowerInvariant() | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "changed_ids=$($changes.Id -join ',')" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "notify_ids=$($notifyOnly.Id -join ',')" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "app_version=$newVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

if (-not $Apply -and $changes.Count -gt 0) {
    Write-Host "Run again with -Apply to update the lock and vendored snapshots."
}
