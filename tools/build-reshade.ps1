param(
    [string]$SourcePath,
    [string]$OutputDirectory,
    [string]$VulkanSdkVersion = "1.4.363.0",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-RepoPath {
    param([string]$Path, [string]$Default)
    if ([string]::IsNullOrWhiteSpace($Path)) { $Path = $Default }
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $Root $Path }
    return [IO.Path]::GetFullPath($Path)
}

$SourcePath = Resolve-RepoPath $SourcePath "third_party/ReShade"
$OutputDirectory = Resolve-RepoPath $OutputDirectory "build-reshade"

$solution = Join-Path $SourcePath "ReShade.sln"
$license = Join-Path $SourcePath "LICENSE.md"
$sourceMetadata = Join-Path $SourcePath "SOURCE.json"
$versionHeader = Join-Path $SourcePath "res\version.h"
$lockPath = Join-Path $Root "third_party\DEPENDENCIES.lock.json"

foreach ($required in @($solution, $license, $sourceMetadata, $lockPath)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Vendored ReShade input is missing: $required"
    }
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$sourceLock = @($lock.sources) | Where-Object { $_.id -eq "reshade" } | Select-Object -First 1
if (-not $sourceLock) { throw "ReShade dependency lock entry was not found." }

$baseTag = [string]$sourceLock.base_tag
$sourceRef = [string]$sourceLock.ref
if ($baseTag -notmatch "^v(\d+)\.(\d+)\.(\d+)$") {
    throw "ReShade base_tag is missing or invalid in dependency lock: $baseTag"
}
$baseMajor = [int]$Matches[1]
$baseMinor = [int]$Matches[2]
$basePatch = [int]$Matches[3]

if ($sourceRef -notmatch "^[0-9a-fA-F]{40}$") {
    throw "ReShade dependency ref is not an immutable SHA: $sourceRef"
}

$shortRef = $sourceRef.Substring(0, 8).ToLowerInvariant()
$sourceVersion = "$baseTag-dev-$shortRef"

if ($Clean -and (Test-Path -LiteralPath $OutputDirectory)) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$sdkRoot = "C:\VulkanSDK\$VulkanSdkVersion"
$glslang = Join-Path $sdkRoot "Bin\glslangValidator.exe"
if (!(Test-Path -LiteralPath $glslang)) {
    if (!(Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget is required to install the pinned Vulkan SDK $VulkanSdkVersion."
    }

    Write-Host "Installing Vulkan SDK $VulkanSdkVersion..."
    winget install --id KhronosGroup.VulkanSDK --exact --version $VulkanSdkVersion --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Vulkan SDK $VulkanSdkVersion through winget."
    }
}

if (!(Test-Path -LiteralPath $glslang)) {
    throw "Vulkan SDK $VulkanSdkVersion is missing glslangValidator: $glslang"
}

$env:VULKAN_SDK = $sdkRoot
$env:VK_SDK_PATH = $sdkRoot
$env:Path = "$sdkRoot\Bin;$env:Path"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path -LiteralPath $vswhere)) {
    throw "vswhere.exe was not found. Install Visual Studio/MSBuild with C++ build tools."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild) -or !(Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild could not be resolved from the installed Visual Studio instance."
}

$hadVersionHeader = Test-Path -LiteralPath $versionHeader
$originalVersionHeader = if ($hadVersionHeader) { [IO.File]::ReadAllBytes($versionHeader) } else { $null }

try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $versionHeader) | Out-Null
    $seedLines = @(
        "#pragma once",
        "",
        "#define VERSION_FULL $baseMajor,$baseMinor,$basePatch,0",
        "#define VERSION_MAJOR $baseMajor",
        "#define VERSION_MINOR $baseMinor",
        "#define VERSION_REVISION $basePatch",
        "#define VERSION_BUILD 0",
        "",
        ("#define VERSION_STRING_FILE `"$baseMajor.$baseMinor.$basePatch.0`""),
        ("#define VERSION_STRING_PRODUCT `"$baseMajor.$baseMinor.$basePatch UNOFFICIAL`"")
    )
    $seed = ($seedLines -join [Environment]::NewLine) + [Environment]::NewLine
    [IO.File]::WriteAllText($versionHeader, $seed, [Text.Encoding]::ASCII)

    Write-Host "Building vendored ReShade 32-bit..."
    & $msbuild $solution /m /p:Configuration=Release /p:Platform=32-bit /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "ReShade 32-bit build failed with exit code $LASTEXITCODE." }

    Write-Host "Building vendored ReShade 64-bit..."
    & $msbuild $solution /m /p:Configuration=Release /p:Platform=64-bit /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "ReShade 64-bit build failed with exit code $LASTEXITCODE." }

    Write-Host "Building vendored ReShade Setup..."
    & $msbuild $solution /m "/p:Configuration=Release Setup" /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "ReShade Setup build failed with exit code $LASTEXITCODE." }
}
finally {
    if ($hadVersionHeader -and $null -ne $originalVersionHeader) {
        [IO.File]::WriteAllBytes($versionHeader, $originalVersionHeader)
    }
    elseif (Test-Path -LiteralPath $versionHeader) {
        Remove-Item -LiteralPath $versionHeader -Force -ErrorAction SilentlyContinue
    }
}

$setupExe = Join-Path $SourcePath "bin\AnyCPU\Release\ReShade Setup.exe"
if (!(Test-Path -LiteralPath $setupExe)) {
    throw "ReShade Setup.exe was not generated in the expected output directory."
}

$size = (Get-Item -LiteralPath $setupExe).Length
if ($size -lt 256KB) { throw "ReShade Setup.exe is unexpectedly small: $size bytes." }

$signature = Get-AuthenticodeSignature -LiteralPath $setupExe
if ($signature.Status -ne "Valid" -and $signature.Status -ne "NotSigned") {
    throw "ReShade Setup Authenticode status is unexpected: $($signature.Status)"
}

$assetBase = "ReShade-Setup-$sourceVersion-vendored"
$packageDir = Join-Path $OutputDirectory $assetBase
$zip = Join-Path $OutputDirectory "$assetBase.zip"

if (Test-Path -LiteralPath $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath $setupExe -Destination (Join-Path $packageDir "ReShade Setup.exe") -Force
Copy-Item -LiteralPath $license -Destination (Join-Path $packageDir "LICENSE-ReShade.md") -Force
Copy-Item -LiteralPath $sourceMetadata -Destination (Join-Path $packageDir "SOURCE-ReShade.json") -Force

$notice = @(
    "This ReShade setup package is built by DLSS NR Manager from the vendored ReShade source snapshot.",
    "It is not an official crosire/ReShade binary distribution.",
    "Source base tag: $baseTag",
    "Source commit: $sourceRef",
    "Authenticode status: $($signature.Status)",
    "The build is intentionally marked UNOFFICIAL when no upstream signing certificate is present."
) -join [Environment]::NewLine
[IO.File]::WriteAllText(
    (Join-Path $packageDir "NOTICE-DLSS-NR-MANAGER.txt"),
    $notice + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zip -Force
if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 256KB) {
    throw "ReShade release package was not generated correctly."
}

Write-Host "Vendored ReShade Setup verified: $setupExe ($size bytes)"
Write-Host "ReShade source version: $sourceVersion"
Write-Host "ReShade Authenticode status: $($signature.Status)"
Write-Host "ReShade package: $zip"
Write-Output $zip
