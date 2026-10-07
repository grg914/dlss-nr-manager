param()

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

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
if ($Policy -notmatch 'RequiredBackends*=s*"vulkan"') {
    throw "Minecraft render policy no longer pins Vulkan as the required backend."
}

foreach ($requiredForbiddenName in @(
    "OptiScaler.ini",
    "OptiScaler.dll",
    "dxgi.dll",
    "d3d12.dll"
)) {
    if ($Policy -notmatch [regex]::Escape($requiredForbiddenName)) {
        throw "Minecraft render policy does not protect against '$requiredForbiddenName'."
    }
}

$ForbiddenOperationalPatterns = @(
    'OptiScaler',
    'dxgi.dll',
    'd3d12.dll',
    'VulkanUpscaler',
    'OutputScaling'
)

foreach ($relative in $OperationalMinecraftFiles) {
    $path = Join-Path $Root $relative
    if (!(Test-Path -LiteralPath $path)) {
        throw "Required Minecraft service is missing: $relative"
    }

    $content = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $ForbiddenOperationalPatterns) {
        if ($content -match $pattern) {
            throw "Minecraft native NGX policy violation in '$relative': forbidden pattern '$pattern'."
        }
    }
}

$OneClick = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftOneClickService.cs") -Raw
if ($OneClick -notmatch 'SetPreferredGraphicsBackend(root,s*MinecraftRenderPipelinePolicy.RequiredBackend)') {
    throw "Minecraft one-click no longer routes the backend through MinecraftRenderPipelinePolicy.RequiredBackend."
}
if ($OneClick -notmatch 'SPBRScandi.zip') {
    throw "Minecraft one-click no longer contains the validated SPBRScandi installation path."
}

$Integration = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftIntegrationService.cs") -Raw
if ($Integration -notmatch 'IsProductionCausticaJar') {
    throw "Minecraft integration no longer selects the production Caustica JAR through the validated selector."
}

$Package = Get-Content -LiteralPath (Join-Path $Root "Services/MinecraftDlssPackageService.cs") -Raw
$PolicyCalls = [regex]::Matches(
    $Package,
    'MinecraftRenderPipelinePolicy.EnsureManagerRuntimeIsNativeNgxOnly('
).Count
if ($PolicyCalls -lt 2) {
    throw "Minecraft DLSS package staging is not guarded in both staging paths."
}

foreach ($runtime in @(
    "nvngx_dlss.dll",
    "nvngx_dlssg.dll",
    "nvngx_dlssnr.dll"
)) {
    if ($Package -notmatch [regex]::Escape($runtime)) {
        throw "Minecraft direct NGX runtime marker is missing: $runtime"
    }
}

Write-Host "Minecraft native Vulkan/NGX policy audit passed."
Write-Host "OptiScaler/DXGI/D3D12 proxies remain excluded from operational Minecraft services."
Write-Host "Caustica selection, SPBRScandi path and NVIDIA NGX runtime markers remain present."
