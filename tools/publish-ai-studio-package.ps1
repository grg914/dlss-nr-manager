param(
    [Parameter(Mandatory=$true)][string]$SourcePath,
    [Parameter(Mandatory=$true)][string]$PackageId,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$License,
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$InstallRelativePath,
    [string]$SourceUri = "",
    [int]$ReleaseAssetChunkMiB = 1900,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = "ai-studio-$PackageId-$Version"
}

if ([string]::IsNullOrWhiteSpace($InstallRelativePath)) {
    $InstallRelativePath = "models/$PackageId"
}

# There is deliberately no total package/model size limit here.
# GitHub Releases currently require each individual release asset to be
# smaller than 2 GiB, so only the transport chunks are capped.
if ($ReleaseAssetChunkMiB -lt 64 -or $ReleaseAssetChunkMiB -gt 1950) {
    throw "ReleaseAssetChunkMiB must be between 64 and 1950 MiB because GitHub caps each release asset below 2 GiB. The complete package has no DLSS NR Manager size limit."
}

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = Join-Path $root "build-local\ai-studio-packages\$PackageId\$Version"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$archiveName = "$PackageId-$Version.zip"
$archivePath = Join-Path $outputRoot $archiveName

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Write-Host "Creating ZIP64 package for $DisplayName..."
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ((Get-Item -LiteralPath $resolvedSource).PSIsContainer) {
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $resolvedSource,
        $archivePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}
else {
    $temp = Join-Path $env:TEMP ("dlssnr-ai-package-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    try {
        Copy-Item -LiteralPath $resolvedSource -Destination (Join-Path $temp (Split-Path -Leaf $resolvedSource)) -Force
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $temp,
            $archivePath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$archiveInfo = Get-Item -LiteralPath $archivePath
$archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

$chunkBytes = [int64]$ReleaseAssetChunkMiB * 1024 * 1024
# Intentionally no check against the total ZIP size. Very large model
# packages are streamed into as many GitHub-compatible parts as needed.
$chunks = [System.Collections.ArrayList]::new()
$buffer = New-Object byte[] (8 * 1024 * 1024)
$input = [System.IO.File]::OpenRead($archivePath)

try {
    $index = 1
    while ($input.Position -lt $input.Length) {
        $chunkName = "{0}-{1}.part{2:d3}" -f $PackageId, $Version, $index
        $chunkPath = Join-Path $outputRoot $chunkName
        $output = [System.IO.File]::Create($chunkPath)

        try {
            [int64]$written = 0
            while ($written -lt $chunkBytes) {
                $remaining = [Math]::Min([int64]$buffer.Length, $chunkBytes - $written)
                $read = $input.Read($buffer, 0, [int]$remaining)
                if ($read -le 0) { break }
                $output.Write($buffer, 0, $read)
                $written += $read
            }
        }
        finally {
            $output.Dispose()
        }

        $chunkInfo = Get-Item -LiteralPath $chunkPath
        $chunkSha256 = (Get-FileHash -LiteralPath $chunkPath -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$chunks.Add([ordered]@{
            index = $index
            name = $chunkName
            size = [int64]$chunkInfo.Length
            sha256 = $chunkSha256
        })
        $index++
    }
}
finally {
    $input.Dispose()
}

$manifestName = "$PackageId.manifest.json"
$manifestPath = Join-Path $outputRoot $manifestName

$manifest = [ordered]@{
    schema = 1
    package_id = $PackageId
    display_name = $DisplayName
    version = $Version
    release_tag = $ReleaseTag
    install_relative_path = $InstallRelativePath.Replace("\", "/")
    license = $License
    source = $SourceUri
    archive = [ordered]@{
        name = $archiveName
        size = [int64]$archiveInfo.Length
        sha256 = $archiveSha256
        format = "zip"
    }
    transport = [ordered]@{
        package_size_limit = $null
        release_asset_chunk_mib = $ReleaseAssetChunkMiB
        rationale = "GitHub Release per-asset transport limit only; reconstructed package is not capped by DLSS NR Manager."
    }
    chunks = @($chunks)
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "AI Studio package prepared:"
Write-Host "  Package: $PackageId"
Write-Host "  Release: $ReleaseTag"
Write-Host "  Archive: $archiveName"
Write-Host "  Archive SHA-256: $archiveSha256"
Write-Host "  Total package size: $($archiveInfo.Length) bytes (no DLSS NR Manager package-size cap)"
Write-Host "  GitHub transport chunk size: $ReleaseAssetChunkMiB MiB"
Write-Host "  Chunks: $($chunks.Count)"
Write-Host "  Manifest: $manifestPath"

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required for -Publish."
    }

    gh release view $ReleaseTag --repo $Repository *> $null
    if ($LASTEXITCODE -ne 0) {
        gh release create $ReleaseTag --repo $Repository --title "DLSS NR Manager AI Studio - $DisplayName $Version" --notes "Manager-owned AI Studio package. Manifest and chunks are SHA-256 verified by DLSS NR Manager." --prerelease
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to create GitHub release $ReleaseTag."
        }
    }

    $assets = @($manifestPath) + @(
        $chunks | ForEach-Object { Join-Path $outputRoot ([string]$_.name) }
    )

    foreach ($asset in $assets) {
        gh release upload $ReleaseTag $asset --repo $Repository --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload AI Studio release asset: $asset"
        }
    }

    Write-Host "Published $PackageId to $Repository release $ReleaseTag."
}

Write-Host ""
Write-Host "The unsplit archive remains only in build-local and is not uploaded."
