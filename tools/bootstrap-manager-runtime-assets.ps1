param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$NvidiaSdkPath,
    [string]$NeuralRuntimePath,
    [string]$SuperResolutionRuntimePath,
    [switch]$SkipVideoTests
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required."
    }

    $ReleaseTag = (& gh release view --repo $Repository --json tagName --jq ".tagName").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        throw "Unable to resolve the latest release tag for $Repository."
    }
}

Write-Host "Bootstrapping manager-owned runtime assets into $Repository $ReleaseTag..."

$streamlineScript = Join-Path $PSScriptRoot "publish-streamline-runtime.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $streamlineScript -Repository $Repository -ReleaseTag $ReleaseTag
if ($LASTEXITCODE -ne 0) {
    throw "Streamline runtime bootstrap failed."
}

$videoScript = Join-Path $PSScriptRoot "publish-video2dlssnr.ps1"
$videoArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $videoScript,
    "-Repository", $Repository,
    "-ReleaseTag", $ReleaseTag
)

if (-not [string]::IsNullOrWhiteSpace($NvidiaSdkPath)) {
    $videoArgs += @("-NvidiaSdkPath", $NvidiaSdkPath)
}
if (-not [string]::IsNullOrWhiteSpace($NeuralRuntimePath)) {
    $videoArgs += @("-NeuralRuntimePath", $NeuralRuntimePath)
}
if (-not [string]::IsNullOrWhiteSpace($SuperResolutionRuntimePath)) {
    $videoArgs += @("-SuperResolutionRuntimePath", $SuperResolutionRuntimePath)
}
if ($SkipVideoTests) {
    $videoArgs += "-SkipTests"
}

& powershell.exe @videoArgs
if ($LASTEXITCODE -ne 0) {
    throw "video2dlssnr runtime bootstrap failed."
}

$minecraftScript = Join-Path $PSScriptRoot "bootstrap-minecraft-runtime.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $minecraftScript -Repository $Repository -ReleaseTag $ReleaseTag
if ($LASTEXITCODE -ne 0) {
    throw "Minecraft runtime bootstrap failed."
}

$releaseJson = & gh api "repos/$Repository/releases/tags/$ReleaseTag"
if ($LASTEXITCODE -ne 0) {
    throw "Unable to verify manager release after runtime bootstrap."
}

$release = $releaseJson | ConvertFrom-Json
$assetNames = @($release.assets | ForEach-Object { [string]$_.name })

$hasVideo = $assetNames -contains "video2dlssnr_release.zip"
$hasStreamline = @($assetNames | Where-Object { $_ -like "streamline-runtime-v*-win-x64.zip" }).Count -gt 0
$hasMinecraft = $assetNames -contains "minecraft-runtime-26.2.zip"

if (-not $hasVideo -or -not $hasStreamline -or -not $hasMinecraft) {
    throw "Runtime bootstrap verification failed. video2dlssnr=$hasVideo Streamline=$hasStreamline Minecraft=$hasMinecraft"
}

Write-Host ""
Write-Host "Manager-owned runtime bootstrap complete."
Write-Host "Release: $ReleaseTag"
Write-Host "video2dlssnr_release.zip: present"
Write-Host "Streamline runtime bundle: present"
Write-Host "minecraft-runtime-26.2.zip: present"
