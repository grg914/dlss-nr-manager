param(
    [string]$SourcePath,
    [string]$BuildPath,
    [string]$VulkanSdkVersion = "1.4.363.0",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $Root "third_party/Real-ESRGAN-ncnn-vulkan/src"
}
elseif (-not [IO.Path]::IsPathRooted($SourcePath)) {
    $SourcePath = Join-Path $Root $SourcePath
}

if ([string]::IsNullOrWhiteSpace($BuildPath)) {
    $BuildPath = Join-Path $Root "build-realesrgan"
}
elseif (-not [IO.Path]::IsPathRooted($BuildPath)) {
    $BuildPath = Join-Path $Root $BuildPath
}

$SourcePath = [IO.Path]::GetFullPath($SourcePath)
$BuildPath = [IO.Path]::GetFullPath($BuildPath)

if ($Clean -and (Test-Path -LiteralPath $BuildPath)) {
    Remove-Item -LiteralPath $BuildPath -Recurse -Force
}

$sdkRoot = "C:\VulkanSDK\$VulkanSdkVersion"
$vulkanHeader = Join-Path $sdkRoot "Include\vulkan\vulkan.h"

if (!(Test-Path -LiteralPath $vulkanHeader)) {
    if (!(Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget is required to install the pinned Vulkan SDK $VulkanSdkVersion."
    }

    Write-Host "Installing Vulkan SDK $VulkanSdkVersion..."
    winget install --id KhronosGroup.VulkanSDK --exact --version $VulkanSdkVersion --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Vulkan SDK $VulkanSdkVersion through winget."
    }
}

if (!(Test-Path -LiteralPath $vulkanHeader)) {
    throw "Vulkan SDK $VulkanSdkVersion is missing expected headers at $sdkRoot."
}

$env:VULKAN_SDK = $sdkRoot
$env:Path = "$sdkRoot\Bin;$env:Path"

$ncnnCmake = Join-Path $SourcePath "ncnn\CMakeLists.txt"
$webpCmake = Join-Path $SourcePath "libwebp\CMakeLists.txt"

if (!(Test-Path -LiteralPath $ncnnCmake)) {
    throw "Vendored ncnn source is missing: $ncnnCmake"
}

if (!(Test-Path -LiteralPath $webpCmake)) {
    throw "Vendored libwebp source is missing: $webpCmake"
}

Write-Host "Configuring vendored Real-ESRGAN..."
# V4 policy: only Real-ESRGAN/ncnn is built without OpenMP.
# Keep Vulkan enabled. Never use a System32 or redistributable vcomp140.dll.
$cmakeArgs = @(
    "-S", $SourcePath, "-B", $BuildPath, "-A", "x64",
    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
    "-DNCNN_OPENMP=OFF",
    "-DCMAKE_DISABLE_FIND_PACKAGE_OpenMP=TRUE"
)
Write-Host "Real-ESRGAN/ncnn: OpenMP disabled; Vulkan GPU support preserved."
& cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) {
    throw "Real-ESRGAN CMake configure failed."
}

Write-Host "Building vendored Real-ESRGAN..."
& cmake --build $BuildPath --config Release --parallel 2
if ($LASTEXITCODE -ne 0) {
    throw "Real-ESRGAN build failed."
}

$exe = Join-Path $BuildPath "Release\realesrgan-ncnn-vulkan.exe"
if (!(Test-Path -LiteralPath $exe)) {
    throw "Real-ESRGAN executable was not generated."
}

$size = (Get-Item -LiteralPath $exe).Length
if ($size -lt 256KB) {
    throw "Real-ESRGAN executable is unexpectedly small: $size bytes."
}

Write-Host "Vendored Real-ESRGAN build verified: $exe ($size bytes)"
Write-Output $exe
