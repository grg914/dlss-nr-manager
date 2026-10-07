param()

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Test-ContainsText {
    param(
        [Parameter(Mandatory=$true)][string]$Content,
        [Parameter(Mandatory=$true)][string]$Needle
    )

    return $Content.IndexOf(
        $Needle,
        [StringComparison]::OrdinalIgnoreCase
    ) -ge 0
}

$OperationalMinecraftFiles = @(
    "Services/MinecraftOneClickService.cs",
    "Services/MinecraftIntegrationService.cs",
    "Services/MinecraftPreflightService.cs",
    "Services/MinecraftDlssPackageService.cs"
)

$PolicyPath = Join-Path $Root "Services/MinecraftRenderPipelinePolicy.cs"
if (!(Test-Path -LiteralPath $PolicyPath)) {
    throw "Minecraft render pipeline policy file is missing."
}

$Policy = Get-Content -LiteralPath $PolicyPath -Raw
if (!(Test-ContainsText $Policy 'public const string RequiredBackend = "vulkan";')) {
    throw "Minecraft render policy no longer pins Vulkan as the required backend."
}

foreach ($requiredForbiddenName in @(
    "OptiScaler.ini",
    "OptiScaler.dll",
    "dxgi.dll",
    "d3d12.dll"
)) {
    if (!(Test-ContainsText $Policy $requiredForbiddenName)) {
        throw "Minecraft render policy does not protect against '$requiredForbiddenName'."
    }
}

$ForbiddenOperationalPatterns = @(
    "OptiScaler",
    "dxgi.dll",
    "d3d12.dll",
    "VulkanUpscaler",
    "OutputScaling"
)

foreach ($relative in $OperationalMinecraftFiles) {
    $path = Join-Path $Root $relative
    if (!(Test-Path -LiteralPath $path)) {
        throw "Required Minecraft service is missing: $relative"
    }

    $content = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $ForbiddenOperationalPatterns) {
        if (Test-ContainsText $content $pattern) {
            throw "Minecraft native NGX policy violation in '$relative': forbidden text '$pattern'."
        }
    }
}

$OneClick = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftOneClickService.cs") -Raw
if (!(Test-ContainsText $OneClick "SetPreferredGraphicsBackend(root, MinecraftRenderPipelinePolicy.RequiredBackend);")) {
    throw "Minecraft one-click no longer routes the backend through MinecraftRenderPipelinePolicy.RequiredBackend."
}
if (!(Test-ContainsText $OneClick "SPBRScandi.zip")) {
    throw "Minecraft one-click no longer contains the validated SPBRScandi installation path."
}

$Integration = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftIntegrationService.cs") -Raw
if (!(Test-ContainsText $Integration "IsProductionCausticaJar")) {
    throw "Minecraft integration no longer selects the production Caustica JAR through the validated selector."
}

$Package = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftDlssPackageService.cs") -Raw
$PolicyCall = "MinecraftRenderPipelinePolicy.EnsureManagerRuntimeIsNativeNgxOnly("
$PolicyCalls = [regex]::Matches(
    $Package,
    [regex]::Escape($PolicyCall)
).Count
if ($PolicyCalls -lt 2) {
    throw "Minecraft DLSS package staging is not guarded in both staging paths."
}

foreach ($runtime in @(
    "nvngx_dlss.dll",
    "nvngx_dlssg.dll",
    "nvngx_dlssnr.dll"
)) {
    if (!(Test-ContainsText $Package $runtime)) {
        throw "Minecraft direct NGX runtime marker is missing: $runtime"
    }
}

Write-Host "Minecraft native Vulkan/NGX policy audit passed."
Write-Host "OptiScaler/DXGI/D3D12 proxies remain excluded from operational Minecraft services."
Write-Host "Caustica selection, SPBRScandi path and NVIDIA NGX runtime markers remain present."
