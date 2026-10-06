param(
    [string]$SourcePath,
    [string]$NvidiaSdkPath,
    [string]$OutputPath,
    [ValidateSet("release", "debug")]
    [string]$Configuration = "release",
    [switch]$SkipTests,
    [switch]$NoPackage
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-RepoPath {
    param([string]$Path, [string]$Default)
    if ([string]::IsNullOrWhiteSpace($Path)) { $Path = $Default }
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $Root $Path }
    return [IO.Path]::GetFullPath($Path)
}

$SourcePath = Resolve-RepoPath $SourcePath "third_party/video2dlssnr"

if ([string]::IsNullOrWhiteSpace($NvidiaSdkPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DLSS_SDK)) {
        $NvidiaSdkPath = $env:DLSS_SDK
    }
    else {
        $NvidiaSdkPath = "third_party-local/NVIDIA-DLSS"
    }
}
$NvidiaSdkPath = Resolve-RepoPath $NvidiaSdkPath "third_party-local/NVIDIA-DLSS"
$OutputPath = Resolve-RepoPath $OutputPath "build-local/video2dlssnr_release"

if (!(Test-Path -LiteralPath (Join-Path $SourcePath "build.bat"))) {
    throw "Vendored video2dlssnr source is missing or incomplete: $SourcePath"
}

$sdkInclude = Join-Path $NvidiaSdkPath "include"
$sdkLib = Join-Path $NvidiaSdkPath "lib\Windows_x86_64\x64\nvsdk_ngx_d.lib"
$sdkDebugLib = Join-Path $NvidiaSdkPath "lib\Windows_x86_64\x64\nvsdk_ngx_d_dbg.lib"
$sdkLicense = Join-Path $NvidiaSdkPath "LICENSE.txt"

foreach ($required in @($sdkInclude, $sdkLib, $sdkLicense)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Required NVIDIA DLSS SDK input is missing: $required"
    }
}

if ($Configuration -eq "debug" -and !(Test-Path -LiteralPath $sdkDebugLib)) {
    throw "Required NVIDIA debug NGX library is missing: $sdkDebugLib"
}

# Safety boundary: an SDK located inside this repository must remain ignored.
$repoPrefix = $Root.TrimEnd("\") + "\"
if ($NvidiaSdkPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    $relativeSdk = $NvidiaSdkPath.Substring($repoPrefix.Length).Replace("\", "/")
    git -C $Root check-ignore -q -- $relativeSdk
    if ($LASTEXITCODE -ne 0) {
        throw "NVIDIA SDK path is inside the repository but is not Git-ignored: $relativeSdk"
    }
}

$stage = Join-Path $env:TEMP ("dlssnr-video2dlssnr-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    robocopy $SourcePath $stage /E /XD .git build out /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Failed to stage vendored video2dlssnr source. robocopy exit code: $LASTEXITCODE"
    }

    $stageNvngx = Join-Path $stage "third_party\nvngx"
    $stageInclude = Join-Path $stageNvngx "include"
    $stageLib = Join-Path $stageNvngx "lib"
    New-Item -ItemType Directory -Force -Path $stageInclude, $stageLib | Out-Null

    robocopy $sdkInclude $stageInclude /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Failed to stage NVIDIA NGX headers. robocopy exit code: $LASTEXITCODE"
    }

    Copy-Item -LiteralPath $sdkLib -Destination (Join-Path $stageLib "nvsdk_ngx_d.lib") -Force
    if ($Configuration -eq "debug") {
        Copy-Item -LiteralPath $sdkDebugLib -Destination (Join-Path $stageLib "nvsdk_ngx_d_dbg.lib") -Force
    }

    Write-Host "Building video2dlssnr [$Configuration] from vendored source with local-only NVIDIA SDK..."
    Push-Location $stage
    try {
        & cmd.exe /d /c "build.bat $Configuration"
        if ($LASTEXITCODE -ne 0) {
            throw "video2dlssnr build failed with exit code $LASTEXITCODE."
        }

        $tests = Join-Path $stage "out\video2dlssnr_tests.exe"
        if (-not $SkipTests) {
            if (!(Test-Path -LiteralPath $tests)) {
                throw "video2dlssnr CPU test executable was not generated."
            }

            & $tests --no-gpu
            if ($LASTEXITCODE -ne 0) {
                throw "video2dlssnr CPU regression tests failed with exit code $LASTEXITCODE."
            }
        }
    }
    finally {
        Pop-Location
    }

    $exe = Join-Path $stage "out\video2dlssnr.exe"
    $forwarder = Join-Path $stage "out\nvngx.dll_dlssnr.dll"
    foreach ($artifact in @($exe, $forwarder)) {
        if (!(Test-Path -LiteralPath $artifact) -or (Get-Item -LiteralPath $artifact).Length -lt 64KB) {
            throw "Expected video2dlssnr runtime artifact is missing or unexpectedly small: $artifact"
        }
    }

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

    Copy-Item -LiteralPath $exe -Destination $OutputPath -Force
    Copy-Item -LiteralPath $forwarder -Destination $OutputPath -Force
    Copy-Item -LiteralPath (Join-Path $SourcePath "LICENSE") -Destination (Join-Path $OutputPath "LICENSE-video2dlssnr.txt") -Force
    Copy-Item -LiteralPath $sdkLicense -Destination (Join-Path $OutputPath "LICENSE-NVIDIA-RTX-SDK.txt") -Force

    $notice = @(
        "DLSS NR Manager includes a video2dlssnr component built with NVIDIA RTX SDK/NGX inputs.",
        "The NVIDIA SDK itself is not vendored or distributed as a stand-alone SDK by this repository.",
        "This software contains source code provided by NVIDIA Corporation.",
        "NVIDIA SDK use and redistribution remain subject to the accompanying NVIDIA RTX SDK license."
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        (Join-Path $OutputPath "NOTICE-NVIDIA.txt"),
        $notice + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    $sourceMetadata = Join-Path $SourcePath "SOURCE.json"
    if (Test-Path -LiteralPath $sourceMetadata) {
        Copy-Item -LiteralPath $sourceMetadata -Destination (Join-Path $OutputPath "SOURCE-video2dlssnr.json") -Force
    }

    $zip = "$OutputPath.zip"
    if (-not $NoPackage) {
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
        Compress-Archive -Path (Join-Path $OutputPath "*") -DestinationPath $zip -Force
        if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 64KB) {
            throw "video2dlssnr package was not generated correctly."
        }
        Write-Host "Packaged video2dlssnr runtime: $zip"
    }

    Write-Host "video2dlssnr local-only SDK build verified."
    Write-Host "Runtime directory: $OutputPath"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
