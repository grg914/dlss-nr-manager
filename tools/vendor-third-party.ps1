param(
    [switch]$Replace,
    [switch]$IncludeMinecraftSources,
    [switch]$IncludeRestrictedNvidiaSdk
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Import-Repo {
    param(
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Ref,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    $target = Join-Path $Root $Destination
    if (Test-Path $target) {
        if (-not $Replace) {
            Write-Host "SKIP $Destination (already exists; use -Replace to refresh)"
            return
        }
        Remove-Item $target -Recurse -Force
    }

    $temp = Join-Path $env:TEMP ("dlssnr-vendor-" + [Guid]::NewGuid().ToString("N"))
    try {
        Write-Host "IMPORT $Url @ $Ref -> $Destination"
        git clone --filter=blob:none --no-checkout $Url $temp
        if ($LASTEXITCODE -ne 0) { throw "git clone failed: $Url" }

        git -C $temp checkout $Ref
        if ($LASTEXITCODE -ne 0) { throw "git checkout failed: $Url @ $Ref" }

        if (Get-Command git-lfs -ErrorAction SilentlyContinue) {
            git -C $temp lfs pull | Out-Host
        }

        New-Item -ItemType Directory -Force -Path $target | Out-Null
        robocopy $temp $target /MIR /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

        $source = [ordered]@{
            url = $Url
            ref = $Ref
            imported_at_utc = [DateTime]::UtcNow.ToString("o")
        } | ConvertTo-Json
        Set-Content -Path (Join-Path $target "SOURCE.json") -Value $source -Encoding UTF8
    }
    finally {
        if (Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Import-Repo "https://github.com/grg914/Caustica-RTX.git" "27575c2ecf8578ca908a49c5d484ff3cc1ed0c7a" "Caustica-RTX"
Import-Repo "https://github.com/NVIDIA-RTX/Streamline.git" "2122257e0fce486f91b385aa63b9a09b0a34b363" "third_party/NVIDIA-Streamline"
Import-Repo "https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass.git" "1bd39091337cc07ba961c8e59ded57e21dc95b18" "third_party/OptiScaler"
Import-Repo "https://github.com/DaniilSokolyuk/video2dlssnr.git" "9e1bbfa5a700d0cd63b224ab6d95581bfe7ec959" "third_party/video2dlssnr"
Import-Repo "https://github.com/BtbN/FFmpeg-Builds.git" "9acad4a9ef1583096af7836cc1e9c8cbcb4d3950" "third_party/FFmpeg-Builds"
Import-Repo "https://github.com/FFmpeg/FFmpeg.git" "9008db55d29631ae4ec62d46edea77c840ce0663" "third_party/FFmpeg"
Import-Repo "https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan.git" "37026f49824c5cf84062e7c6a5dd71445dcf610f" "third_party/Real-ESRGAN-ncnn-vulkan"
Import-Repo "https://github.com/Tohrusky/realesrgan-ncnn-py.git" "900c0549a2fb3481b71d0369253522519308f1f2" "third_party/Real-ESRGAN-model-sources/realesrgan-ncnn-py"
Import-Repo "https://github.com/itsspin/spintexture.git" "9f291a8aa2afed34fc42e76696c2ce8317cf2143" "third_party/Real-ESRGAN-model-sources/spintexture"
Import-Repo "https://github.com/crosire/reshade.git" "7bf9de8b33bcc76c3177007e65d73c72dd0f34c0" "third_party/ReShade"
Import-Repo "https://huggingface.co/onnx-community/ai-image-detection-ONNX" "e3cfe99f2841930a040a6281682c10c989965603" "third_party/ai-models/ai-image-detection-ONNX"
Import-Repo "https://huggingface.co/onnx-community/ai-image-detect-distilled-ONNX" "7f067e23521eeb6d6525221af82c613fb746aaff" "third_party/ai-models/ai-image-detect-distilled-ONNX"

if ($IncludeMinecraftSources) {
    Import-Repo "https://github.com/FabricMC/fabric-installer.git" "master" "third_party/minecraft/fabric-installer"
    Import-Repo "https://github.com/FabricMC/fabric.git" "26.2" "third_party/minecraft/fabric-api"
    Import-Repo "https://github.com/CaffeineMC/lithium.git" "26.2" "third_party/minecraft/lithium"
    Import-Repo "https://github.com/malte0811/FerriteCore.git" "HEAD" "third_party/minecraft/ferritecore"
    Import-Repo "https://github.com/astei/krypton.git" "master" "third_party/minecraft/krypton"
    Import-Repo "https://github.com/RelativityMC/C2ME-fabric.git" "dev/26.2.0" "third_party/minecraft/c2me"
    Import-Repo "https://github.com/imthosea/BadOptimizations.git" "26.2" "third_party/minecraft/badoptimizations"
    Import-Repo "https://github.com/juliand665/Dynamic-FPS.git" "main" "third_party/minecraft/dynamic-fps"
}

if ($IncludeRestrictedNvidiaSdk) {
    Write-Warning "NVIDIA/DLSS is imported only into third_party-local because its SDK license restricts standalone redistribution."
    Import-Repo "https://github.com/NVIDIA/DLSS.git" "v310.7.0" "third_party-local/NVIDIA-DLSS"
}

Write-Host ""
Write-Host "Vendor import complete."
Write-Host "Review third_party/README.md and all upstream licenses before redistribution."