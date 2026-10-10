param(
    [ValidateSet('Active','After')][string]$Phase = 'Active',
    [int]$ManagerPid = 0,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows-only diagnostic; no test was performed.' }
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!(Test-Path -LiteralPath $root)) { New-Item -Path $root -ItemType Directory -Force | Out-Null }
$activeFile = Join-Path $root 'owned-processes-active.json'
$afterFile = Join-Path $root 'owned-processes-after.json'

function Get-ProcessSnapshot {
    return @(Get-CimInstance Win32_Process | ForEach-Object {
        [pscustomobject]@{
            Pid = [int]$_.ProcessId
            ParentPid = [int]$_.ParentProcessId
            Name = [string]$_.Name
            Creation = [string]$_.CreationDate
        }
    })
}

function Get-DescendantPids {
    param([object[]]$Processes, [int]$RootPid)
    $owned = New-Object 'System.Collections.Generic.HashSet[int]'
    [void]$owned.Add($RootPid)
    do {
        $previousCount = $owned.Count
        foreach ($proc in $Processes) {
            if ($owned.Contains([int]$proc.ParentPid)) { [void]$owned.Add([int]$proc.Pid) }
        }
    } while ($owned.Count -ne $previousCount)
    return @($Processes | Where-Object { $owned.Contains([int]$_.Pid) })
}

if ($Phase -eq 'Active') {
    if ($ManagerPid -le 0) { throw 'Specify the exact manager PID with -ManagerPid.' }
    $all = @(Get-ProcessSnapshot)
    $manager = @($all | Where-Object { $_.Pid -eq $ManagerPid })
    if ($manager.Count -ne 1) { throw 'The specified manager PID is not running.' }
    if ($manager[0].Name -ne 'DlssNrManager.exe') { throw 'The selected PID is not DlssNrManager.exe.' }
    $owned = @(Get-DescendantPids -Processes $all -RootPid $ManagerPid)
    $tcp = @()
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        $tcp = @(Get-NetTCPConnection -ErrorAction SilentlyContinue |
            Where-Object { $id = [int]$_.OwningProcess; @($owned | Where-Object { $_.Pid -eq $id }).Count -gt 0 } |
            Select-Object OwningProcess, LocalAddress, LocalPort, State)
    }
    $gpu = @()
    if (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue) {
        $gpu = @(& nvidia-smi.exe --query-gpu=index,name,driver_version,memory.used,memory.total,utilization.gpu --format=csv 2>$null)
    }
    $payload = [ordered]@{
        Phase = 'Active'
        CapturedAt = (Get-Date).ToUniversalTime().ToString('o')
        ManagerPid = $ManagerPid
        ManagerCreation = $manager[0].Creation
        ProcessTree = @($owned)
        TcpEndpoints = @($tcp)
        GpuTelemetry = @($gpu)
        Limitations = 'Point-in-time PID ancestry only; reparented descendants, other sessions and per-process VRAM attribution require separate inspection.'
    }
    $payload | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $activeFile -Encoding UTF8
    Write-Host ('Active process evidence: ' + $activeFile)
    exit 0
}

if (!(Test-Path -LiteralPath $activeFile -PathType Leaf)) { throw 'Capture -Phase Active while the manager and workload run first.' }
$previous = Get-Content -LiteralPath $activeFile -Raw | ConvertFrom-Json
$all = @(Get-ProcessSnapshot)
$survivors = @()
foreach ($old in @($previous.ProcessTree)) {
    $matches = @($all | Where-Object { $_.Pid -eq [int]$old.Pid -and $_.Creation -eq [string]$old.Creation })
    if ($matches.Count -gt 0) { $survivors += $matches[0] }
}
$output = [ordered]@{
    Phase = 'After'
    CapturedAt = (Get-Date).ToUniversalTime().ToString('o')
    SourceActiveSnapshot = $activeFile
    ManagerPid = [int]$previous.ManagerPid
    PotentialOwnedSurvivors = @($survivors)
    Limitations = 'Only descendants captured during the Active snapshot are checked; absent processes do not prove VRAM/filesystem/rollback acceptance. Review manually.'
}
$output | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $afterFile -Encoding UTF8
Write-Host ('Post-close process evidence: ' + $afterFile)
if ($survivors.Count -gt 0) { Write-Warning 'Potential owned process survivors. Investigate before acceptance.'; exit 2 }
Write-Host 'No sampled owned PID+creation identity remains. Other acceptance gates still require manual proof.'
exit 0
