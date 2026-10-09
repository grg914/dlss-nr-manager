# V4.0 — Windows RTX acceptance protocol (2026-10-10)

**Status: PREPARED, NOT RUN, NOT APPROVED.** Complete this runbook on the actual supported Windows x64/RTX PC using the exact candidate EXE/ZIP and Git commit. Source CI checks do not replace physical runtime tests. Do not tag or publish v4 until issue #95, #79, #111 and owner acceptance are satisfied.

## Preflight and safety
- Back up every previously working manager-owned runtime, user model and output. Use an isolated test installation and a disposable working folder; never delete arbitrary user data. Log Windows build, GPU, NVIDIA driver, available space, full commit SHA and EXE/ZIP SHA-256.
- In ordinary Windows PowerShell, from a checkout of the exact candidate source SHA, replace the executable path below. These commands do not download, install or upgrade anything by themselves.
~~~powershell
$exe = 'C:\path\to\DlssNrManager.exe'
Get-FileHash -LiteralPath $exe -Algorithm SHA256
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,FreePhysicalMemory,TotalVisibleMemorySize
Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion
if (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue) {
  nvidia-smi.exe --query-gpu=index,name,driver_version,memory.used,memory.total,utilization.gpu --format=csv
}
~~~

## START → RUN → STOP → CLEANUP
**Normal:** launch manager, start a permitted manager-owned helper and representative workload, stop/cancel it, close the window yourself and inspect child processes, downloads and file locks. Script waits for your UI close, it does not close the application.
~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath $exe -TimeoutSeconds 900 -GraceSeconds 15 -ReportPath "$env:TEMP\dlssnr-v4-normal.json"
~~~
**Forced:** in a separate disposable instance with no unsaved work, test owner process abrupt termination after 60 seconds. The script kills the exact manager process handle only; it does not kill arbitrary helper PIDs by name.
~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath $exe -ForceTerminateAfterSeconds 60 -TimeoutSeconds 900 -GraceSeconds 15 -ReportPath "$env:TEMP\dlssnr-v4-forced.json"
~~~
The script exit code is 0 for no additional named process candidates, 2 for possible residual candidates, 3 for timeout. **A zero is not a GPU VRAM, port, RAM or file-handle cleanup proof.** Compare process identity and start time, Get-CimInstance Win32_Process parent IDs, Get-NetTCPConnection listening sockets, download file size/time, Windows Event Viewer, PowerShell/manager logs, CPU working set, GPU global VRAM/utilization before/during/after. NVIDIA per-process GPU telemetry may omit some Vulkan allocation. Separate unrelated applications may cause false-positive name-only process results.

## Pass/fail grid
| Scenario | Physical evidence needed | Status |
| --- | --- | --- |
| Offline clean installation | Disconnect before startup; no mandatory update lookup; offline-capable pages remain functional | BLOCKED until measured |
| Explicit downloads and cancellation | Valid manager-owned source, SHA-256, cancel/close and staging cleanup, previous runtime preserved | BLOCKED |
| Crash/forced stop/restart | PID tree, ports, worker cancellation, file locks, backup restore and final logs | BLOCKED |
| SHA/version error and rollback | Deliberately wrong hash/older manifest in throwaway path; fail closed, previous package preserved | BLOCKED |
| Reparse junction/locked file | No traversal outside owned roots, no deletion of user files, backup recovery | BLOCKED |
| French and English across all pages | Tooltips, errors, confirmation, Download Center and selected cached rows without network on language switch | BLOCKED |
| Supported RTX workloads | Same PC GPU driver, CPU/RAM, peak VRAM, performance/quality x2/x4, no process residue | BLOCKED |
| License and third-party dependency | Exact SHA, signed vendor provenance, all license notices, OpenMP fail-closed if no approval (#79) | BLOCKED |

For each run attach candidate commit SHA, EXE/ZIP hashes, Windows build, GPU/driver, timestamps, expected vs actual, JSON report, logs and reviewer disposition to issue #95. No claim of passing without user hardware evidence.

## Release candidate dry-run — NO publication
- Source .NET 8 win-x64 self-contained single-file build, regression xUnit and exact GitHub-signed-HEAD required Build and CodeQL / Analyze C# must pass.
- Check exact candidate executable ZIP, components-manifest.json, SHA256SUMS.txt, SBOM.spdx.json and release-provenance.json. Confirm 100% same commit and digests, license notices, restricted SDK exclusion, pinned manager-owned runtime sources.
- Stage 2 #111 demands version-aware Update only on approved strictly newer package, validated installed receipt, opt-in action and rollback. Stage 4 #79 demands lawful Microsoft OpenMP REDIST provenance or tested OpenMP-free build. #95 remains hardware blocked until this grid is executed.
- Current project/release still v3.2.0. Do not bump version, create v4 tag, publish or overwrite immutable previous release until all gates and user authorization pass. Experimental GPT A / GPT C v4.5+ tasks excluded.

Update all three append-only progress/coordination journals and docs/V4_FINALIZATION_AUDIT_2026-10-09.md with links to objective evidence only after acceptance.
