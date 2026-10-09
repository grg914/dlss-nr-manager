# DLSS NR Manager v4.0 — Windows Release Candidate acceptance handoff

**Status: NOT ACCEPTED.** Owner physical Windows/RTX tests and Microsoft REDIST authorization are outstanding. This checklist prepares testing; it does not change runtime files, unlock a release, or assert results. Owner requested completing v4 source work **before** installing a test build.

## Responsibility and verified source status

- **GPT B (v4 release owner):** final stable-source integration, exact-head Build/CodeQL, approved signer/release pipeline, Windows crash/rollback follow-ups and immutable v4.0.0 publication. Offline startup PR [#324](https://github.com/grg914/dlss-nr-manager/pull/324) **merged** as signed main [`dddd9ac76a9108a7607331966f9148245a23e686`](https://github.com/grg914/dlss-nr-manager/commit/dddd9ac76a9108a7607331966f9148245a23e686); validated Build/CodeQL and 194/194 xUnit tests. **No physical offline validation yet.**
- **GPT A:** dependency/source-provenance audit, Caustica license metadata and this Windows acceptance specification. Do not alter GPT B's process/update implementation or release pipeline without coordination.
- **GPT C:** isolated AI Studio v4.5; keep external Python/PyTorch/ComfyUI/model-weights and experimental features out of the v4.0 release.
- **Experimental Caustica PRs #311 → #316 → #319:** remain draft/unmerged and excluded from v4.0 stable.

## Preflight: before producing an RC

1. Resolve current v4 candidate SHA, all required checks and independent review. Ensure the signed candidate includes the merged offline fix, but excludes unreviewed v4.5/ReSTIR changes. Never republish different bytes under the existing `v3.2.0` tag.
2. Prepare a clearly identified `v4.0.0-rc` **test** build from the precise reviewed source, not a stable release; record the EXE/ZIP SHA-256, contents/versions, SBOM and native runtime assets.
3. Check that package sources are manager-owned or local-only as required; native NVIDIA SDK itself must not be redistributed. For OpenMP see the issue #79 gate below.
4. Record OS build, GPU model/VRAM, NVIDIA driver, Manager SHA and time of the run. Use nonessential/disposable game profiles and source media for destructive/failure tests.

## Windows 11 acceptance protocol — issue #95

| Scenario | Operator action | Pass condition / evidence |
| --- | --- | --- |
| Normal launch | Start the approved RC on supported Windows x64, verify hardware detection and main pages | Startup and FR/EN strings work; no unexpected elevated privileges, blocking network errors or missing runtime dialogs |
| Offline launch | After completing one authorized online installation, disconnect network, restart Manager, inspect Games & DLSS / Minecraft RTX / Media / Downloads | Local game discovery and cached artwork remain; no automatic remote update attempt on startup. Manual checks can fail visibly but safely |
| Install and rollback | On a disposable managed game/Media target, install component then replace/update and restore | User-owned originals and receipts/hashes unchanged after restore; no stale mixed-release native set |
| Network interruption | Interrupt a manager-owned archive download mid-stream, then resume/retry | No partially downloaded archive promoted to installed; hash validation and retry state correct |
| Locked files | With a test DLL/file legitimately opened by another process, trigger update/restore | No untracked overwrite; clear error and original content preserved |
| Filesystem containment | Exercise test-only missing parent/symlink/junction cases where safely supported | No escape into user files; fail closed without deleting unrelated files |
| Normal helper exit | Start at least one real VLC/FFmpeg/Real-ESRGAN helper and close Manager normally | No orphaned manager-owned helper after grace period; collect process JSON and separate GPU VRAM observations |
| Forced Manager exit | **Only with explicit consent and disposable content**, force-stop the specifically launched Manager process and restart | Recovery preserves data/backups; no unrelated process is killed, crash output/receipts auditable |
| Real-ESRGAN | Run a permitted model on copy of input, compare x2/x4 output correctness/performance | No missing native DLL / OpenMP errors, stable GPU utilization; do not report pass from packaging checks alone |
| Minecraft RTX stable | Test only already approved stable Minecraft 26.2/Fabric/Caustica runtime | Game launches and native policies hold; experimental ReSTIR is **not** part of this v4.0 acceptance |

**Existing non-destructive observation tool** (source `tools/verify-v4-windows-process-cleanup.ps1`):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath "C:\path\to\reviewed-v4-rc\DlssNrManager.exe" -ReportPath ".\v4-normal-exit.json"
```

This tool launches a Manager process and waits for it to exit; **exercise an actual helper** during the session. By default it does **not** kill any process or test VRAM/recovery; its PID-name report can include unrelated processes and must be reviewed. The `-ForceTerminateAfterSeconds` mode explicitly kills the **started Manager only** (not helper processes); run **only** after backups and operator consent, with disposable files. Save separate crash/restart, VRAM and rollback evidence. Never conflate process-name snapshots with ownership proof.

Record **PASS / FAIL / BLOCKED / NOT RUN**, precise steps, logs and SHA per scenario. If any critical operation fails, create a minimal reproducible issue rather than asserting RC ready.

## OpenMP / Real-ESRGAN distribution gate — issue #79

Current `runtime-refresh.yml` is **fail closed**: when `DLSSNR_OPENMP_REDIST_APPROVED != 1`, it skips Real-ESRGAN OpenMP publication, preserving the existing asset. Approval is not inferred from the CI runner or Microsoft Authenticode alone.

Before enabling any new `vcomp140.dll` distribution, the actual licensed operator must independently document rights under the applicable Microsoft Visual Studio edition/terms, supported non-debug x64 `VC/Redist/MSVC` provenance, exact version and SHA-256, valid Microsoft signature, generated `MICROSOFT_OPENMP_PROVENANCE.json`, notice/asset coverage, and the real Windows functional test above. If evidence is absent, leave approval OFF and **do not advertise a newly updated OpenMP-dependent Real-ESRGAN package as validated**.

## Sign-off / release decision

- **Code gates:** reviewed/signed exact commit, Build + CodeQL + all xUnit green, deterministic app ZIP, immutable asset SHA256SUMS / manifest / SPDX SBOM / source provenance.
- **Owner gates:** recorded Windows tests (especially offline and rollback), Real-ESRGAN licensing/disabled decision, FR/EN UI, legitimate redistribution rights.
- **Final publication:** GPT B performs serialized protected merges and release cut, rechecks the live main SHA and release assets. Do not mark v4.0.0 completed until its exact downloadable artifacts exist and the acceptance evidence is linked.

Related: `docs/RELEASE_CHECKLIST.md`, `docs/BACKUP_ROLLBACK_POLICY.md`, issues [#95](https://github.com/grg914/dlss-nr-manager/issues/95) and [#79](https://github.com/grg914/dlss-nr-manager/issues/79).
