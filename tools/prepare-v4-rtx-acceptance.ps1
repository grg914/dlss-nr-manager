param(
    [Parameter(Mandatory = $true)][string]$ManagerExecutablePath,
    [string]$EvidenceDirectory = (Join-Path $env:TEMP 'dlssnr-v4-rtx-acceptance')
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows-only RTX acceptance preparation; no test performed.' }
$exe = [IO.Path]::GetFullPath($ManagerExecutablePath)
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Manager executable missing: $exe" }
$dir = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$os = Get-CimInstance Win32_OperatingSystem
$gpu = @()
if (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue) {
    $gpu = @(& nvidia-smi.exe --query-gpu=name,driver_version,memory.total --format=csv,noheader 2>$null)
}
$probePaths = @(
    'tools/collect-v4-owned-process-evidence.ps1',
    'tools/verify-v4-windows-process-cleanup.ps1'
)
$missing = @($probePaths | Where-Object { !(Test-Path -LiteralPath $_ -PathType Leaf) })
$report = [ordered]@{
    schema_version = 1
    type = 'V4 Windows RTX acceptance preflight only'
    timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
    os_caption = [string]$os.Caption
    os_build = [string]$os.BuildNumber
    exe_name = [IO.Path]::GetFileName($exe)
    exe_size_bytes = (Get-Item -LiteralPath $exe).Length
    exe_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    nvidia_gpus = @($gpu)
    diagnostic_scripts_missing = @($missing)
    test_results = [ordered]@{
        normal_exit = 'NOT_RUN'
        forced_exit = 'NOT_RUN'
        owned_child_processes = 'NOT_RUN'
        gpu_memory_release = 'NOT_RUN'
        transactional_rollback = 'NOT_RUN'
        offline_restart = 'NOT_RUN'
        realesrgan_vulkan_photo_x2_x4 = 'NOT_RUN'
        realesrgan_vulkan_anime_x2_x4 = 'NOT_RUN'
        license_model_preservation = 'NOT_RUN'
        reparse_point_confinement = 'NOT_RUN'
    }
    disclosure = 'No physical acceptance scenario is executed by this inventory. Exclude private filenames, accounts, network destinations and licensed model content from shared reports.'
}
$output = Join-Path $dir 'v4-rtx-preflight.json'
[IO.File]::WriteAllText($output, (($report | ConvertTo-Json -Depth 7) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Host "Read-only RTX preflight: $output"
if ($gpu.Count -eq 0) { Write-Warning 'NVIDIA telemetry not available; physical GPU acceptance blocked.' }
if ($missing.Count -gt 0) { Write-Warning 'Run from the exact matching repository checkout; evidence scripts are missing.' }
Write-Host 'All destructive/forced-exit/rollback scenarios remain NOT_RUN until separately executed with disposable test data.'
