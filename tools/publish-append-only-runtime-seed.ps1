param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1",
    [ValidateSet("Reject", "StageImmutable")][string]$OnChanged = "Reject"
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $Path).Path
$name = [IO.Path]::GetFileName($source)

# Only reviewed manager-owned seed inputs can be published by this helper.
$allowed = (
    $name -cmatch '^vlc-[0-9]+\.[0-9]+\.[0-9]+-win64\.zip$' -or
    $name -cmatch '^vlc-[0-9]+\.[0-9]+\.[0-9]+\.tar\.xz$' -or
    $name -cmatch '^vlc-[0-9]+\.[0-9]+\.[0-9]+-provenance\.json$' -or
    $name -ceq 'temurin-25-jre-win-x64.zip' -or
    $name -ceq 'temurin-25-jre.json' -or
    $name -ceq 'realesrgan-ncnn-vulkan-windows-x64.zip'
)
if (-not $allowed) { throw "Unsupported manager-owned runtime seed asset: $name" }
if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $ReleaseTag -cnotmatch '^[A-Za-z0-9._-]+$') {
    throw "Unsafe repository or release tag."
}

$size = [long](Get-Item -LiteralPath $source).Length
if ($size -le 0) { throw "Cannot publish an empty runtime seed asset." }

# Validate input structure and path safety before querying or modifying a release.
if ($name.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
    try {
        # Windows PowerShell 5.1 does not preload ZipFile; PowerShell 7 usually does.
        if ($PSVersionTable.PSEdition -eq "Desktop") {
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
        }
        $archive = [IO.Compression.ZipFile]::OpenRead($source)
        try {
            if ($archive.Entries.Count -eq 0) { throw "ZIP has no entries." }
            foreach ($entry in $archive.Entries) {
                $item = $entry.FullName.Replace('\', '/')
                if ([string]::IsNullOrWhiteSpace($item) -or
                    $item.StartsWith("/") -or
                    $item -match '(^|/)\.\.(/|$)' -or
                    $item -match '^[A-Za-z]:') {
                    throw "Unsafe archive entry: $item"
                }
            }
        }
        finally { $archive.Dispose() }
    }
    catch { throw "Runtime seed ZIP validation failed: $($_.Exception.Message)" }
}
elseif ($name.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase)) {
    try {
        $data = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
        if ($null -eq $data -or $data -isnot [pscustomobject]) {
            throw "JSON document must be an object."
        }
    }
    catch { throw "Runtime seed JSON validation failed: $($_.Exception.Message)" }
}
elseif ($name.EndsWith(".tar.xz", [StringComparison]::OrdinalIgnoreCase)) {
    if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
        throw "tar is required to verify a manager-owned VLC source archive."
    }
    $entries = @(& tar -tf $source 2>$null)
    if ($LASTEXITCODE -ne 0 -or $entries.Count -eq 0) {
        throw "Runtime seed tar.xz validation failed."
    }
    foreach ($item in $entries) {
        $pathName = [string]$item
        if ([string]::IsNullOrWhiteSpace($pathName) -or
            $pathName.StartsWith("/") -or
            $pathName -match '(^|/)\.\.(/|$)' -or
            $pathName -match '^[A-Za-z]:') {
            throw "Unsafe tar archive path: $pathName"
        }
    }
}

$sha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()

function Read-Release {
    $raw = @(gh api "repos/$Repository/releases/tags/$ReleaseTag")
    if ($LASTEXITCODE -ne 0) { throw "Could not read runtime seed release metadata." }
    $release = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($release.draft) { throw "Refusing publication into draft runtime seed release." }
    return $release
}

function Find-ExactAsset {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$AssetName)
    $matched = @($Release.assets | Where-Object { [string]$_.name -ceq $AssetName })
    if ($matched.Count -gt 1) { throw "Duplicate release asset name: $AssetName" }
    if ($matched.Count -eq 0) { return $null }
    return $matched[0]
}

function Test-AssetIntegrity {
    param([Parameter(Mandatory)]$Asset)
    $digest = [string]$Asset.digest
    if ($digest -notmatch '^(?i:sha256):[a-fA-F0-9]{64}$' -or
        [long]$Asset.size -le 0) {
        throw "Unverifiable GitHub release asset SHA-256 or size."
    }
    return ($digest.ToLowerInvariant() -ceq "sha256:$sha" -and
        [long]$Asset.size -eq $size)
}

$release = Read-Release
$canonical = Find-ExactAsset -Release $release -AssetName $name
if ($null -ne $canonical) {
    if (Test-AssetIntegrity -Asset $canonical) {
        Write-Host "SKIP verified unchanged runtime seed: $name ($sha)"
        return
    }
    if ($OnChanged -eq "Reject") {
        throw "Versioned runtime seed '$name' already exists with different content. Refusing destructive replacement."
    }
}

$target = $name
if ($null -ne $canonical) {
    if ($name.EndsWith(".tar.xz", [StringComparison]::OrdinalIgnoreCase)) {
        $target = $name.Substring(0, $name.Length - 7) + ".sha256-$sha.tar.xz"
    }
    else {
        $extension = [IO.Path]::GetExtension($name)
        $target = $name.Substring(0, $name.Length - $extension.Length) +
            ".sha256-$sha$extension"
    }
}

$existing = Find-ExactAsset -Release $release -AssetName $target
if ($null -ne $existing) {
    if (-not (Test-AssetIntegrity -Asset $existing)) {
        throw "Immutable release asset '$target' has conflicting SHA-256/size."
    }
    Write-Host "SKIP verified immutable runtime seed: $target"
    return
}

$tempDirectory = $null
$upload = $source
try {
    if ($target -cne $name) {
        $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $tempDirectory = Join-Path $tempRoot ("dlssnr-append-seed-" + [Guid]::NewGuid().ToString("N"))
        New-Item -Path $tempDirectory -ItemType Directory -Force | Out-Null
        $upload = Join-Path $tempDirectory $target
        Copy-Item -LiteralPath $source -Destination $upload
    }

    # No --clobber. A racing publisher must fail, never replace an in-use asset.
    gh release upload $ReleaseTag $upload --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Non-destructive runtime seed upload failed." }

    $published = Find-ExactAsset -Release (Read-Release) -AssetName $target
    if ($null -eq $published -or -not (Test-AssetIntegrity -Asset $published)) {
        throw "Published runtime seed SHA-256/size mismatch or missing asset: $target"
    }
    Write-Host "Verified appended runtime seed: $target ($sha)"
    if ($target -ne $name) {
        Write-Warning "Canonical seed remains pinned; activate this revision through reviewed consumer metadata."
    }
}
finally {
    if ($null -ne $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
