param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Repository,
    [string]$ReleaseTag = "runtime-seed-v1"
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
    param([Parameter(Mandatory)]$Release)
    $found = @($Release.assets | Where-Object { [string]$_.name -ceq $name })
    if ($found.Count -gt 1) { throw "Duplicate versioned asset '$name' in release." }
    if ($found.Count -eq 0) { return $null }
    return $found[0]
}

function Confirm-Asset {
    param([Parameter(Mandatory)]$Asset)
    if ([string]$Asset.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        [long]$Asset.size -ne [long]$size -or
        [string]$Asset.digest -cne "sha256:$sha") {
        throw "Versioned asset '$name' has conflicting or unverifiable SHA-256/size."
    }
}

$release = Get-Release
$existing = Get-ExactAsset -Release $release
if ($null -ne $existing) {
    Confirm-Asset -Asset $existing
    Write-Host "SKIP verified unchanged versioned asset: $name ($sha)"
    return
}

# A raced publication with this name must fail, not delete another asset.
gh release upload $ReleaseTag $source --repo $Repository
if ($LASTEXITCODE -ne 0) { throw "Append-only versioned asset upload failed: $name" }

$published = Get-ExactAsset -Release (Get-Release)
if ($null -eq $published) {
    throw "Uploaded versioned runtime seed '$name' is absent from release metadata."
}
Confirm-Asset -Asset $published
Write-Host "Verified append-only versioned runtime seed: $name ($sha)"
