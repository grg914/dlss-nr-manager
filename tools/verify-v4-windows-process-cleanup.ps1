param(
    [Parameter(Mandatory = $true)]
    [string]$ManagerExecutablePath,

    [ValidateRange(10, 7200)]
    [int]$TimeoutSeconds = 900,

    [ValidateRange(1, 60)]
    [int]$GraceSeconds = 10,

    [ValidateRange(0, 3600)]
    [int]$ForceTerminateAfterSeconds = 0,

    [string[]]$AdditionalProcessNames = @(),

    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "This acceptance probe must run on Windows."
}

$executable = [IO.Path]::GetFullPath($ManagerExecutablePath)
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Manager executable not found: $executable"
}

$knownNames = @(
    "video2dlssnr",
    "ffmpeg",
    "ffprobe",
    "realesrgan-ncnn-vulkan"
)
$processNames = @($knownNames + $AdditionalProcessNames |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique)

function Get-Candidates {
    param([string[]]$Names)
    $found = @(Get-Process -Name $Names -ErrorAction SilentlyContinue |
        Select-Object Id, ProcessName)
    return $found
}

# Never terminate third-party processes. Snapshot PID+process name before
# launching this particular manager instance; only report new candidates.
$before = @(Get-Candidates -Names $processNames)
$beforeIds = @{}
foreach ($candidate in $before) {
    $beforeIds[[int]$candidate.Id] = $true
}

$startedAt = Get-Date
$mode = if ($ForceTerminateAfterSeconds -gt 0) { "forced-parent-kill" } else { "normal-exit" }
Write-Host "Starting manager: $executable"
Write-Host "Mode: $mode. Report candidates only; no helper will be killed by this script."

$manager = Start-Process -FilePath $executable -PassThru
$pidManager = [int]$manager.Id
$timedOut = $false
$forced = $false

try {
    if ($ForceTerminateAfterSeconds -gt 0) {
        Start-Sleep -Seconds $ForceTerminateAfterSeconds
        $current = Get-Process -Id $pidManager -ErrorAction SilentlyContinue
        if ($null -ne $current) {
            Write-Warning "Forcing ONLY the manager process $pidManager to exit. Any unsaved app state may be lost."
            Stop-Process -Id $pidManager -Force -ErrorAction Stop
            $forced = $true
        }
    }

    try {
        Wait-Process -Id $pidManager -Timeout $TimeoutSeconds -ErrorAction Stop
    }
    catch {
        $stillAlive = Get-Process -Id $pidManager -ErrorAction SilentlyContinue
        if ($null -ne $stillAlive) {
            $timedOut = $true
            Write-Warning "Manager did not exit within the timeout; it was NOT killed by the timeout."
        }
    }

    Start-Sleep -Seconds $GraceSeconds

    $remaining = @(
        Get-Candidates -Names $processNames |
        Where-Object { -not $beforeIds.ContainsKey([int]$_.Id) } |
        ForEach-Object {
            [pscustomobject]@{
                Name = $_.ProcessName
                Pid = [int]$_.Id
            }
        }
    )

    $report = [ordered]@{
        Test = "DLSS NR Manager Windows process cleanup"
        StartedAt = $startedAt.ToString("o")
        FinishedAt = (Get-Date).ToString("o")
        ManagerPid = $pidManager
        Mode = $mode
        ForcedTerminationAttempted = $forced
        TimedOut = $timedOut
        GraceSeconds = $GraceSeconds
        NamesMonitored = @($processNames)
        PotentialResidualProcesses = @($remaining)
        Interpretation = "PID-baseline comparison only; other applications may launch these names. No GPU VRAM or user-data deletion was tested."
    }

    if ([string]::IsNullOrWhiteSpace($ReportPath)) {
        $ReportPath = Join-Path ([IO.Path]::GetTempPath()) (
            "dlssnr-v4-cleanup-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".json")
    }
    $output = [IO.Path]::GetFullPath($ReportPath)
    $folder = Split-Path -Path $output -Parent
    if (!(Test-Path -LiteralPath $folder)) {
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding UTF8
    Write-Host "Report: $output"

    if ($timedOut) { exit 3 }
    if ($remaining.Count -gt 0) {
        Write-Warning "$($remaining.Count) newly observed helper process(es) remain; review the JSON report."
        exit 2
    }
    Write-Host "PASS: no new monitored helper process remains after manager exit."
    exit 0
}
finally {
    $manager.Dispose()
}
