param(
    [string]$SourcePath,
    [string]$OutputDirectory,
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

function Read-VersionDefine {
    param([string]$Path, [string]$Name)
    $match = Select-String -LiteralPath $Path -Pattern ("^#define\s+" + [regex]::Escape($Name) + "\s+(\d+)\s*$") | Select-Object -First 1
    if (-not $match) { throw "Version define $Name was not found in $Path." }
    return [int]$match.Matches[0].Groups[1].Value
}

$SourcePath = Resolve-RepoPath $SourcePath "third_party/OptiScaler"
$OutputDirectory = Resolve-RepoPath $OutputDirectory "build-optiscaler"

$solution = Join-Path $SourcePath "OptiScaler.sln"
$resource = Join-Path $SourcePath "OptiScaler\resource.h"
$license = Join-Path $SourcePath "LICENSE"

foreach ($required in @($solution, $resource, $license)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Vendored OptiScaler input is missing: $required"
    }
}

if ($Clean -and (Test-Path -LiteralPath $OutputDirectory)) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path -LiteralPath $vswhere)) {
    throw "vswhere.exe was not found. Install Visual Studio/MSBuild with C++ build tools."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild) -or !(Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild could not be resolved from the installed Visual Studio instance."
}

$forceIncludeManifest = Join-Path $SourcePath "VENDORED_FORCE_INCLUDE.txt"
if (!(Test-Path -LiteralPath $forceIncludeManifest)) {
    throw "Vendored OptiScaler snapshot is missing VENDORED_FORCE_INCLUDE.txt. Re-import the locked source with tools/vendor-third-party.ps1 -Replace -StageImported."
}

$missingForced = @(
    Get-Content -LiteralPath $forceIncludeManifest |
        ForEach-Object {
            $entry = ([string]$_).Trim()
            if ([string]::IsNullOrWhiteSpace($entry) -or $entry.StartsWith("#")) {
                return
            }

            $required = Join-Path $SourcePath $entry
            if (!(Test-Path -LiteralPath $required -PathType Leaf)) {
                $entry
            }
        }
)

if ($missingForced.Count -gt 0) {
    throw "Vendored OptiScaler snapshot is incomplete. Missing upstream-tracked file(s): $($missingForced -join ', '). Re-import with tools/vendor-third-party.ps1 -Replace -StageImported."
}

# The upstream Release pre-build step otherwise embeds the current clock
# in resource_build_date.h, so byte-identical sources yield different DLLs.
# Pin the stamp to the imported immutable source receipt, not wall-clock time.
$sourceReceipt = Join-Path $SourcePath "SOURCE.json"
if (!(Test-Path -LiteralPath $sourceReceipt -PathType Leaf)) {
    throw "OptiScaler source receipt missing; a reproducible native build requires SOURCE.json."
}
$receipt = Get-Content -LiteralPath $sourceReceipt -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$receipt.ref) -or
    [string]::IsNullOrWhiteSpace([string]$receipt.imported_at_utc)) {
    throw "OptiScaler source receipt lacks immutable ref/import timestamp."
}
$imported = [DateTimeOffset]::Parse(
    [string]$receipt.imported_at_utc,
    [Globalization.CultureInfo]::InvariantCulture)
$buildStamp = $imported.UtcDateTime.ToString(
    "yyyyMMdd_HHmmss", [Globalization.CultureInfo]::InvariantCulture)
$previousStamp = $env:DLSSNR_OPTISCALER_BUILD_DATE
try {
    $env:DLSSNR_OPTISCALER_BUILD_DATE = $buildStamp
    Write-Host "Building locked OptiScaler ref $($receipt.ref) with pinned source stamp $buildStamp..."
    & $msbuild $solution /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Vendored OptiScaler build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:DLSSNR_OPTISCALER_BUILD_DATE = $previousStamp
}

$packageSource = Join-Path $SourcePath "x64\Release\a"
$optiDll = Join-Path $packageSource "OptiScaler.dll"
if (!(Test-Path -LiteralPath $optiDll)) {
    throw "OptiScaler.dll was not generated in the expected package directory: $packageSource"
}

$dllSize = (Get-Item -LiteralPath $optiDll).Length
if ($dllSize -lt 256KB) {
    throw "OptiScaler.dll is unexpectedly small: $dllSize bytes."
}

$major = Read-VersionDefine $resource "VER_MAJOR_VERSION"
$minor = Read-VersionDefine $resource "VER_MINOR_VERSION"
$hotfix = Read-VersionDefine $resource "VER_HOTFIX_VERSION"
$build = Read-VersionDefine $resource "VER_BUILD_NUMBER"
$tag = "v$major.$minor.$hotfix-pre$build"
$assetBase = "OptiScaler-NR-$tag-vendored-win-x64"
$packageDestination = Join-Path $OutputDirectory $assetBase
$zip = Join-Path $OutputDirectory "$assetBase.zip"

if (Test-Path -LiteralPath $packageDestination) {
    Remove-Item -LiteralPath $packageDestination -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDestination | Out-Null

# Keep the native forwarder PDB in the build workspace for diagnostics,
# but exclude it from the user runtime package. /Brepro stabilizes both DLLs;
# MSVC's separate forwarder PDB still varies between fresh builds and is not
# needed to run the released package.
robocopy $packageSource $packageDestination /E /NFL /NDL /NJH /NJS /NP /XF nvngx.dll_dlssnr.pdb | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Failed to stage OptiScaler package. robocopy exit code: $LASTEXITCODE"
}

$licenseDir = Join-Path $packageDestination "Licenses"
New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
Copy-Item -LiteralPath $license -Destination (Join-Path $licenseDir "OptiScaler_LICENSE.txt") -Force

$sourceMetadata = Join-Path $SourcePath "SOURCE.json"
if (Test-Path -LiteralPath $sourceMetadata) {
    Copy-Item -LiteralPath $sourceMetadata -Destination (Join-Path $licenseDir "SOURCE-OptiScaler.json") -Force
}

# Normalize entry order, nested paths and ZIP timestamps. Keep the immutable
# publisher responsible for any same-version conflict (never overwrite release).
& (Join-Path $PSScriptRoot "create-deterministic-flat-zip.ps1") `
    -InputDirectory $packageDestination `
    -OutputPath $zip `
    -TimestampUtc "1980-01-01T00:00:00Z" `
    -Compression Optimal -Recursive

if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 256KB) {
    throw "OptiScaler release package was not generated correctly."
}

# The next CI collision can now be attributed to individual rebuilt files
# rather than guessing whether the ZIP metadata or native binary changed.
$packageFiles = @(Get-ChildItem -LiteralPath $packageDestination -Recurse -File | Sort-Object FullName)
foreach ($packageFile in $packageFiles) {
    $relative = $packageFile.FullName.Substring($packageDestination.Length).TrimStart([char[]]@([char]92, [char]47)).Replace('\', '/')
    $fileHash = (Get-FileHash -LiteralPath $packageFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "OptiScaler package input: $relative SHA256=$fileHash size=$($packageFile.Length)"
}
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "OptiScaler package ZIP SHA256=$zipHash size=$((Get-Item -LiteralPath $zip).Length)"
Write-Host "OptiScaler MSBuild: $msbuild"
Write-Host "Vendored OptiScaler build verified: $optiDll ($dllSize bytes)"
Write-Host "OptiScaler source version: $tag"
Write-Host "OptiScaler package: $zip"
Write-Output $zip
