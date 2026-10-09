param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Repository,
    [string]$ReleaseTag = "runtime-seed-v1",
    [ValidateSet("Reject", "StageImmutable")][string]$OnChanged = "Reject"
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $Path).Path
$name = [IO.Path]::GetFileName($source)
if ($name -cnotmatch '^(?:OptiScaler-NR-[A-Za-z0-9._+-]+-vendored-win-x64|ReShade-Setup-[A-Za-z0-9._+-]+-vendored)\.zip$') {
    throw "Unsupported versioned runtime seed asset name: $name"
}
if ($Repository -notmatch '^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$' -or
    $ReleaseTag -notmatch '^[a-zA-Z0-9._-]+$') {
    throw "Invalid runtime seed repository or release tag."
}

# Reject corrupted and empty archives before touching any release asset.
try {
    $zip = [IO.Compression.ZipFile]::OpenRead($source)
    try {
        if ($zip.Entries.Count -eq 0) { throw "Archive is empty." }
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($entryName) -or
                $entryName.StartsWith('/') -or
                $entryName -match '(^|/)\.\.(/|$)' -or
                $entryName -match '^[A-Za-z]:') {
                throw "Unsafe archive entry '$entryName'."
            }
        }
    }
    finally { $zip.Dispose() }
}
catch {
    throw "Versioned runtime seed ZIP validation failed: $($_.Exception.Message)"
}

$sha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $source).Length
if ($size -le 0) { throw "Empty runtime seed package." }

function Get-Release {
    $raw = @(gh api "repos/$Repository/releases/tags/$ReleaseTag")
    if ($LASTEXITCODE -ne 0) { throw "Unable to read manager-owned release." }
    $release = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($release.draft) { throw "Cannot publish into draft runtime seed release." }
    return $release
}

function Get-ExactAsset {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$AssetName)
    $found = @($Release.assets | Where-Object { [string]$_.name -ceq $AssetName })
    if ($found.Count -gt 1) { throw "Duplicate versioned asset '$AssetName' in release." }
    if ($found.Count -eq 0) { return $null }
    return $found[0]
}

function Assert-AssetMetadata {
    param([Parameter(Mandatory)]$Asset, [Parameter(Mandatory)][string]$AssetName)
    # A broken/missing remote digest must never be treated as a legitimate
    # binary revision. Fail closed even when immutable staging is requested.
    if ([string]$Asset.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        [long]$Asset.size -le 0) {
        throw "Versioned asset '$AssetName' has conflicting or unverifiable SHA-256/size."
    }
}

function Confirm-Asset {
    param([Parameter(Mandatory)]$Asset, [Parameter(Mandatory)][string]$AssetName)
    Assert-AssetMetadata -Asset $Asset -AssetName $AssetName
    if ([long]$Asset.size -ne [long]$size -or
        [string]$Asset.digest -cne "sha256:$sha") {
        throw "Versioned asset '$AssetName' has conflicting or unverifiable SHA-256/size."
    }
}

Write-Host "Local versioned runtime seed: $name (SHA-256 $sha; $size bytes)"
$release = Get-Release
$existing = Get-ExactAsset -Release $release -AssetName $name
$target = $name
if ($null -ne $existing) {
    Assert-AssetMetadata -Asset $existing -AssetName $name
    if ([long]$existing.size -eq [long]$size -and [string]$existing.digest -ceq "sha256:$sha") {
        Write-Host "SKIP verified unchanged versioned asset: $name ($sha)"
        return
    }
    if ($OnChanged -eq "Reject") {
        throw "Versioned asset '$name' has conflicting or unverifiable SHA-256/size."
    }

    # Preserve the canonical same-version binary: a native rebuild is not
    # proof of reproducibility or an authorization to promote a new DLL.
    # Immutable revisions have a collision-resistant, SHA-addressed name.
    $target = $name.Substring(0, $name.Length - 4) + ".sha256-$sha.zip"
    Write-Warning "Canonical '$name' differs from rebuilt output; staging '$target' for review only."
}

$staged = Get-ExactAsset -Release $release -AssetName $target
if ($null -ne $staged) {
    Confirm-Asset -Asset $staged -AssetName $target
    Write-Host "SKIP verified unchanged immutable versioned asset: $target"
    return
}

$tempDirectory = $null
$upload = $source
try {
    if ($target -cne $name) {
        $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $tempDirectory = Join-Path $tempRoot ("dlssnr-versioned-seed-" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
        $upload = Join-Path $tempDirectory $target
        Copy-Item -LiteralPath $source -Destination $upload
    }

    # Never use --clobber; raced names must fail rather than replace an asset.
    gh release upload $ReleaseTag $upload --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Append-only versioned asset upload failed: $target" }

    $published = Get-ExactAsset -Release (Get-Release) -AssetName $target
    if ($null -eq $published) {
        throw "Uploaded versioned runtime seed '$target' is absent from release metadata."
    }
    Confirm-Asset -Asset $published -AssetName $target
    Write-Host "Verified append-only versioned runtime seed: $target ($sha)"
    if ($target -cne $name) {
        Write-Warning "Canonical asset remains pinned; reviewed consumer promotion and rollback are required."
    }
}
finally {
    if ($null -ne $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
