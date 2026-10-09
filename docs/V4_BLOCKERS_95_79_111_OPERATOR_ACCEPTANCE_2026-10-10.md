# V4 release blockers #95 / #79 / #111 — evidence and safe disposition

Baseline: signed main `8313565324659ba4a46b6c79ef0777ec36b8feea` (after reviewed PR #407); application/release remains `v3.2.0`. This file is an execution checklist, NOT evidence that physical Windows RTX tests or legal approval have occurred.

## #95 — real Windows/RTX and transactional recovery

Use a throwaway Windows 11 x64 NVIDIA RTX test account and explicitly back up existing Manager local data, games, videos, model files, installed workloads, and license records. Do not copy private content into GitHub. Use a signed exact-source candidate EXE/ZIP and record SHA-256 using `Get-FileHash`.

From the matching checkout, in normal PowerShell (non-elevated), with the installed candidate running:

```powershell
$exe = 'C:\PATH\TO\DlssNrManager.exe'
$report = Join-Path $env:TEMP 'dlssnr-v4-acceptance'
New-Item -ItemType Directory -Path $report -Force | Out-Null
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$mgr = Get-CimInstance Win32_Process -Filter "Name='DlssNrManager.exe'" | Select-Object ProcessId,CreationDate
$mgr | Format-Table
# While one exact manager PID is running a manager-owned media workload:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-v4-owned-process-evidence.ps1 -Phase Active -ManagerPid <EXACT_PID> -EvidenceDirectory $report
# Close that manager normally, then collect the exact sampled PID+creation identities:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-v4-owned-process-evidence.ps1 -Phase After -EvidenceDirectory $report
# Separately, after backing up disposable workloads, use existing normal/forced probes:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath $exe -TimeoutSeconds 900 -GraceSeconds 15 -ReportPath (Join-Path $report 'normal.json')
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath $exe -ForceTerminateAfterSeconds 60 -TimeoutSeconds 900 -GraceSeconds 15 -ReportPath (Join-Path $report 'forced.json')
```

The new collector is READ-ONLY: it never kills anything, enumerates exact descendants by initial PID ancestry and compares PID+creation time after shutdown to resist PID reuse. It snapshots manager-owned TCP endpoints and global `nvidia-smi` telemetry if available. If descendants are reparented or spawn after the capture, the sample can miss them. Exit code 0 does **not** prove full VRAM release, open handles, file integrity or rollback. The existing forced probe kills only its own started manager handle, never third-party processes; do not run it while editing unsaved real data.

Execute distinct on-device scenarios: connected install then offline startup; manual reconnect; download cancel; forced interruption in stage/swap; bad SHA, truncated ZIP, inaccessible destination/locked file; nested junction/reparse input; exhausted disk in a disposable volume; restart/recovery; rollback restored previous SHA; no mutation of licensed manually imported models, user jobs or outputs. Exercise VLC playback, FFmpeg/media, Real-ESRGAN Vulkan x2/x4 and AI-origin only after lawful trusted packages are present, plus game/Minecraft helpers if in V4 scope. Compare logs, processes, handles, GPU VRAM, TCP endpoints and file trees before/during/after. Mark each PASS/FAIL/BLOCKED and attach redacted evidence. Issue #95 remains OPEN until these are actually performed.

## #79 — lawful Real-ESRGAN OpenMP release gate

Already integrated: `runtime-refresh.yml` skips Real-ESRGAN publishing unless `DLSSNR_OPENMP_REDIST_APPROVED=1`; `tools/package-approved-openmp.ps1` permits only signed Microsoft x64 DLLs under the legitimate installed Visual Studio `VC/Redist/MSVC` release subtree and records digest/version. It excludes arbitrary System32 copies and debug/nonredist. The flag alone is **not evidence of a licensed redistributor**.

Before setting any approval or creating a release asset, the actual redistributing operator must independently review the applicable Visual Studio edition's license terms and confirm eligibility. Capture an internal audit record with: operator identity and date, exact Visual Studio edition/build/license basis, allowed REDIST path, unmodified Microsoft signed vcomp140.dll SHA256 and file version, verified Authenticode, exact build/workflow and Git SHA, output ZIP/EXE hash, notice obligations, and release authorization. Record a non-sensitive approval reference in issue #79, not keys/license documents. Official references: https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution and https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files .

Alternative: build `tools/build-realesrgan.ps1 -DisableOpenMp`, audit direct imports with `tools/audit-realesrgan-openmp.ps1`, inspect transitive imports of *all* shipped DLLs, then validate actual Vulkan x2/x4 photo/anime jobs, throughput, peak RAM/VRAM, drivers, quality and overnight stability on RTX. It is not currently a production-approved package. Do not use this path to claim `OpenMP-free` success without those results. Until one complete path is evidenced, **do not publish the runtime and keep #79 open**.

## #111 — approved optional component updates

The existing Download Center displays optional Update only for manager-owned validated newer Media/AI Studio packages. Other components remain Install/Redownload (repair) without claiming a new version. Do not equate upstream tags or changed SHA with release authorization.

New code-source safeguard in this candidate: `DownloadCenterService.RequireSupportedVerifiedUpdateRoute` refuses confirmed `Update` for Media (separate verified updater) and VLC, Real-ESRGAN, AI-origin (no approved per-component receipts); it checks before any download/removal. Ordinary explicit Redownload and verified AI Studio model Upgrade remain functional. `ComponentUpdateService.IsValidComponentDigest` now rejects non-hex pseudo-SHA256 manifest fields. Added independent xUnit positive/negative regression tests; CI + Windows acceptance still required.

Remaining eligible-component completion: each VLC, Real-ESRGAN, AI-origin release needs a versioned manager-owned package schema, exact immutable asset ID/digest, approved publisher/source/license, locally verified installed receipt tied to the actual files, strict stable version comparison and execution-time replay/snapshot recheck. Only after that implement/update eligibility and FR/EN statuses on existing one-button Download Center, with offline, equal/older, invalid/corrupt, restricted, cancelled and rollback tests. Do not silently promote arbitrary upstream releases. Preserve existing valid v3.2.0 assets; no release overwrite.

## Current closure decision

| Gate | Source state | Missing proof |
| --- | --- | --- |
| #95 | Software rollback & process tracker + additional read-only ownership collector | Actual target PC test logs, data/VRAM/process validation |
| #79 | Fail-closed official REDIST provenance guard | Licensed operator entitlement and same-package signed evidence OR validated OpenMP-free on RTX |
| #111 | Confirmed media/model update guards; new extra fail-closed route+digest checks | Approved per-component receipts and full end-to-end Windows update checks |

Never close a gate on an unexecuted test. Do not tag or publish V4 without signed exact-head CI, all mandatory hardware and license evidence, updated SHA256SUMS/asset-manifest/SBOM/provenance, and explicit product owner release authorization.
