param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$ReleaseTag,
    [long]$MinimumSizeBytes = 1MB
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $Path).Path
$name = [IO.Path]::GetFileName($source)
if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$' -or
    $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $ReleaseTag -notmatch '^[A-Za-z0-9._-]+$') {
    throw "Unsafe release asset name, repository or tag."
}

$length = (Get-Item -LiteralPath $source).Length
if ($length -lt $MinimumSizeBytes) { throw "Release ZIP is unexpectedly small." }
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($source)
    try {
        if ($archive.Entries.Count -eq 0) { throw "Empty release ZIP." }
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($entryName) -or
                $entryName.StartsWith('/') -or
                $entryName -match '(^|/)\.\.(/|$)' -or
                $entryName -match '^[A-Za-z]:') {
                throw "Unsafe release ZIP entry '$entryName'."
            }
        }
    }
    finally { $archive.Dispose() }
}
catch { throw "Release ZIP integrity validation failed: $($_.Exception.Message)" }

$sha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
function Read-Release {
    $json = @(gh api "repos/$Repository/releases/tags/$ReleaseTag")
    if ($LASTEXITCODE -ne 0) { throw "Cannot read release asset metadata." }
    $release = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    if ($release.draft) { throw "Cannot publish into a draft release." }
    return $release
}
function Find-Asset {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$AssetName)
    $found = @($Release.assets | Where-Object { [string]$_.name -ceq $AssetName })
    if ($found.Count -gt 1) { throw "Duplicate release asset: $AssetName" }
    if ($found.Count -eq 0) { return $null }
    return $found[0]
}
function Check-Asset {
    param([Parameter(Mandatory)]$Asset)
    $digest = [string]$Asset.digest
    if ($digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or [long]$Asset.size -le 0) {
        throw "Release asset has invalid digest/size."
    }
    return ($digest -ceq "sha256:$sha" -and [long]$Asset.size -eq [long]$length)
}

$release = Read-Release
$canonical = Find-Asset -Release $release -AssetName $name
$target = $name
if ($null -ne $canonical) {
    $same = Check-Asset -Asset $canonical
    if ($same) {
        Write-Host "SKIP identical canonical release asset $name ($sha)."
        return
    }
    $target = $name.Substring(0, $name.Length - 4) + ".sha256-$sha.zip"
}

$existing = Find-Asset -Release $release -AssetName $target
if ($null -ne $existing) {
    if (-not (Check-Asset -Asset $existing)) {
        throw "Immutable release asset '$target' has conflicting content."
    }
    Write-Host "SKIP identical immutable release asset $target."
    return
}

$temp = $null
$upload = $source
try {
    if ($target -ne $name) {
        $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $temp = Join-Path $tempRoot ("dlssnr-asset-" + [Guid]::NewGuid().ToString("N"))
        New-Item -Path $temp -ItemType Directory -Force | Out-Null
        $upload = Join-Path $temp $target
        Copy-Item -LiteralPath $source -Destination $upload
    }
    # Never delete/reupload an existing asset; a racing writer must fail.
    gh release upload $ReleaseTag $upload --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Append-only release asset upload failed." }

    $published = Find-Asset -Release (Read-Release) -AssetName $target
    if ($null -eq $published -or -not (Check-Asset -Asset $published)) {
        throw "Published release asset missing or SHA-256/size mismatch."
    }
    Write-Host "Verified append-only release asset: $target ($sha)."
    if ($target -ne $name) {
        Write-Warning "Canonical release asset retained. Promotion requires reviewed consumer pin changes."
    }
}
finally {
    if ($null -ne $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
}
