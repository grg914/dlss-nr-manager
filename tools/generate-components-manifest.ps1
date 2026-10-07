param(
    [string]$AssetsDirectory = "release-assets",
    [string]$OutputPath = "release-assets/components-manifest.json"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Lock = Get-Content (Join-Path $Root "third_party\DEPENDENCIES.lock.json") -Raw | ConvertFrom-Json
$MinecraftLock = Get-Content (Join-Path $Root "third_party\minecraft\RUNTIME.lock.json") -Raw | ConvertFrom-Json
[xml]$Project = Get-Content (Join-Path $Root "DlssNrManager.csproj")
$AppVersion = [string]$Project.Project.PropertyGroup.Version

$assetsRoot = if ([IO.Path]::IsPathRooted($AssetsDirectory)) {
    $AssetsDirectory
} else {
    Join-Path $Root $AssetsDirectory
}
$output = if ([IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $Root $OutputPath
}

function Get-Source {
    param([string]$Id)
    return @($Lock.sources) | Where-Object { [string]$_.id -eq $Id } | Select-Object -First 1
}

function Add-Asset {
    param(
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][System.Collections.ArrayList]$List,
        [Parameter(Mandatory=$true)][string]$Id,
        [Parameter(Mandatory=$true)][string]$DisplayName,
        [Parameter(Mandatory=$true)][string]$Pattern,
        [string]$SourceId,
        [string]$Version
    )

    $file = Get-ChildItem -LiteralPath $assetsRoot -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $file) { return }

    $source = if ($SourceId) { Get-Source $SourceId } else { $null }
    $resolvedVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $Version
    }
    elseif ($source -and $source.release_tag) {
        [string]$source.release_tag
    }
    elseif ($source -and $source.source_tag) {
        [string]$source.source_tag
    }
    elseif ($source -and $source.base_tag) {
        [string]$source.base_tag
    }
    elseif ($source) {
        ([string]$source.ref).Substring(0, 12)
    }
    else {
        $AppVersion
    }

    [void]$List.Add([ordered]@{
        id = $Id
        name = $DisplayName
        version = $resolvedVersion
        asset = $file.Name
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        source_id = $SourceId
        source_ref = if ($source) { [string]$source.ref } else { $null }
        channel = "stable"
    })
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$components = [System.Collections.ArrayList]::new()

Add-Asset $components "video2dlssnr" "video2dlssnr" "video2dlssnr_release.zip" "video2dlssnr"
Add-Asset $components "ffmpeg" "FFmpeg" "ffmpeg-dlssnr-win-x64.zip" "ffmpeg"
Add-Asset $components "streamline" "NVIDIA Streamline" "streamline-runtime-v*-win-x64.zip" "streamline"
Add-Asset $components "minecraft-runtime" "Minecraft Runtime" "minecraft-runtime-26.2.zip" "minecraft-fabric-api" ([string]$MinecraftLock.minecraft_version)
Add-Asset $components "realesrgan" "Real-ESRGAN" "realesrgan-ncnn-vulkan-windows-x64.zip" "realesrgan"
Add-Asset $components "optiscaler" "OptiScaler" "OptiScaler-NR-*-vendored-win-x64.zip" "optiscaler"
Add-Asset $components "reshade" "ReShade" "ReShade-Setup-*-vendored.zip" "reshade"
Add-Asset $components "caustica" "Caustica RTX" "Caustica-RTX-Minecraft-26.2-build-*.jar" "caustica"
Add-Asset $components "ai-origin-primary" "AI Origin Detector Primary" "ai-origin-primary-int8.onnx" "ai-primary"
Add-Asset $components "ai-origin-secondary" "AI Origin Detector Secondary" "ai-origin-secondary-int8.onnx" "ai-secondary"
$javaMetadataPath = Join-Path $assetsRoot "temurin-25-jre.json"
$javaVersion = $null
if (Test-Path -LiteralPath $javaMetadataPath) {
    try { $javaVersion = [string](Get-Content -LiteralPath $javaMetadataPath -Raw | ConvertFrom-Json).version } catch { $javaVersion = $null }
}
Add-Asset $components "java25" "Eclipse Temurin JRE 25" "temurin-25-jre-win-x64.zip" "" $javaVersion

$manifest = [ordered]@{
    schema = 1
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
    manager_repository = "grg914/dlss-nr-manager"
    application_version = $AppVersion
    components = @($components)
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding UTF8
Write-Host "Generated $output with $($components.Count) manager-owned components."
