param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$NvidiaSdkPath,
    [string]$NeuralRuntimePath,
    [string]$SuperResolutionRuntimePath,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required to publish the video2dlssnr bootstrap asset."
}

& gh auth status --hostname github.com 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
}

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = (& gh release view --repo $Repository --json tagName --jq ".tagName").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        throw "Unable to resolve the latest release tag for $Repository."
    }
}

if ([string]::IsNullOrWhiteSpace($NeuralRuntimePath)) {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:DLSS_NR_RUNTIME)) {
        $candidates += $env:DLSS_NR_RUNTIME
    }

    $candidates += Join-Path $Root "third_party-local\NVIDIA-DLSS\nvngx_dlssnr.dll"

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $installedVideoRoot = Join-Path $localAppData "DlssNrManager\media-engine\video2dlssnr"

    if (Test-Path -LiteralPath $installedVideoRoot) {
        $candidates += @(Get-ChildItem -LiteralPath $installedVideoRoot -Filter "nvngx_dlssnr.dll" -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | ForEach-Object { $_.FullName })
    }

    foreach ($candidate in @($candidates)) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or !(Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $candidate
        $signer = if ($signature.SignerCertificate) { [string]$signature.SignerCertificate.Subject } else { "" }

        if ($signature.Status -eq "Valid" -and $signer -match "(?i)NVIDIA") {
            $NeuralRuntimePath = (Resolve-Path -LiteralPath $candidate).Path
            Write-Host "Auto-detected signed NVIDIA Neural Rendering runtime: $NeuralRuntimePath"
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($NeuralRuntimePath)) {
    throw "No valid NVIDIA nvngx_dlssnr.dll was found automatically. Supply -NeuralRuntimePath or DLSS_NR_RUNTIME."
}

$buildArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "build-video2dlssnr.ps1"),
    "-OutputPath", "build-local/video2dlssnr_release"
)

if (-not [string]::IsNullOrWhiteSpace($NvidiaSdkPath)) {
    $buildArgs += @("-NvidiaSdkPath", $NvidiaSdkPath)
}
if (-not [string]::IsNullOrWhiteSpace($NeuralRuntimePath)) {
    $buildArgs += @("-NeuralRuntimePath", $NeuralRuntimePath)
}
if (-not [string]::IsNullOrWhiteSpace($SuperResolutionRuntimePath)) {
    $buildArgs += @("-SuperResolutionRuntimePath", $SuperResolutionRuntimePath)
}
if ($SkipTests) {
    $buildArgs += "-SkipTests"
}

Write-Host "Building validated video2dlssnr package for $Repository release $ReleaseTag..."
& powershell.exe @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "video2dlssnr build/package step failed."
}

$zip = Join-Path $Root "build-local/video2dlssnr_release.zip"
if (!(Test-Path -LiteralPath $zip)) {
    throw "Expected package was not generated: $zip"
}

$size = (Get-Item -LiteralPath $zip).Length
if ($size -lt 1MB) {
    throw "video2dlssnr release package is unexpectedly small: $size bytes."
}

$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Local package: $zip"
Write-Host "SHA-256: $sha256"
Write-Host "Uploading to $Repository release $ReleaseTag..."

& gh release upload $ReleaseTag $zip --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) {
    throw "GitHub release upload failed."
}

$releaseJson = & gh api "repos/$Repository/releases/tags/$ReleaseTag"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($releaseJson)) {
    throw "Unable to verify release $ReleaseTag after upload."
}

$release = $releaseJson | ConvertFrom-Json
$asset = @($release.assets) |
    Where-Object { $_.name -eq "video2dlssnr_release.zip" } |
    Select-Object -First 1

if (-not $asset) {
    throw "Uploaded video2dlssnr_release.zip was not found in release $ReleaseTag."
}

if ([long]$asset.size -ne $size) {
    throw "Remote video2dlssnr asset size mismatch. Local=$size Remote=$($asset.size)"
}

if ($asset.digest -and ([string]$asset.digest).StartsWith("sha256:")) {
    $remoteDigest = ([string]$asset.digest).Substring(7).ToLowerInvariant()
    if ($remoteDigest -ne $sha256) {
        throw "Remote video2dlssnr SHA-256 mismatch. Local=$sha256 Remote=$remoteDigest"
    }
}

Write-Host "Published and verified video2dlssnr_release.zip on $Repository $ReleaseTag."
