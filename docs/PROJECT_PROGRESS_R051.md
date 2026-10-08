# Project continuity checkpoint R051 — 2026-10-08

## Scope: manager-owned process isolation

Source: the user's `prompt.txt` requires START → RUN → STOP → CLEANUP and no unintended surviving or terminated child processes.

- Inspected `Services/ExternalProcessTracker.cs` on `main` (blob `9feac16135f93db002db9c852d3613d475e13acf`). `KillAll()` previously enumerated *all* processes called `ffmpeg`, `ffprobe`, `video2dlssnr` or `realesrgan-ncnn-vulkan` whose image path was under the shared `%LOCALAPPDATA%\\DlssNrManager` directory. This does **not** establish ownership by this manager instance, and may terminate helpers belonging to another concurrent instance.
- Created branch `fix/v4-owned-helper-isolation` based on `main` before its pending PR #125/#127 merges.
- Changed `KillAll()` to stop only the processes recorded in the instance's `Active` dictionary. Kept Windows `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` for assigned children, and existing `Shutdown()` closing behavior. Removed path/name based global helper termination.
- Added a Windows regression test that runs a separate, **untracked** helper from a temporary `%LOCALAPPDATA%\\DlssNrManager` subfolder, invokes `KillAll()`, and asserts the unrelated helper survives. Test always terminates its own fixture in `finally`; test collection disables parallel execution to avoid cross-test interference.
- Limitation: the code still starts Windows processes *before* assigning them to a Job Object, and a Job Object assignment failure currently logs a warning rather than failing closed. Those gaps warrant a separate review; this patch does not prove absolute crash isolation under every Windows configuration.
- No Windows GPU tests or physical manager shutdown tests executed from ChatGPT. Regression/CodeQL status is pending on this branch. No v4.0 release has been produced.

## Prior checkpoints

- The canonical journal on `main` held R039–R045 at the start of this work. PR #125 carries R046–R050 and comprehensive branch audit and was updated to current `main` to restore required checks.
- PR #127 proposes bounded reader-side retry for temporarily missing Streamline seed assets. Both PRs require their latest commit's Build and CodeQL green before merging.
- Issues #79, #82, #95, #111, #123, #124 remain release/audit blockers or active work items.

## Next action

1. Verify Build and CodeQL for PR #125; merge if both green and branch/head current.
2. Verify PR #127 similarly and merge only if current required checks pass.
3. Check this ownership-isolation branch's Windows xUnit and CodeQL, reconcile latest `main`, then merge if verified.
4. Consolidate R050–R051 as a prominent pointer or checkpoint in `AI_PROJECT_PROGRESS.txt` without truncating older history, and keep issues #95/#124 updated.
5. Continue release acceptance against `prompt.txt`; do not claim hardware verification, licensing approval, or v4.0 readiness without evidence.


## R052 — Prompt re-audit and live GitHub state (2026-10-08)

- Read the complete `prompt.txt` from the project file library. It requires hardware profiles, FR/EN, localized tooltips, single Download Center with optional updates, safe START/RUN/STOP/CLEANUP, regression/security/dependency/performance review, branch reconciliation and a verified 4.0 release.
- Audited `MainWindow.xaml`, `Services/HardwareProfileService.cs` and `MainWindow.xaml.cs`: Auto and RTX 5060 Ti/Ryzen 9700X profiles and Normal/Compatible modes are present; GPU/CPU/VRAM/driver/DirectX/Vulkan probes and a fingerprint re-probe on activation after five minutes are implemented. The existence of a 617.42-specific driver compatibility certification or end-to-end GPU test is **not** established.
- Static translation audit: 59 distinct literal `ToolTip` attribute values on production `MainWindow.xaml` (the 59 non-binding values); 57 found as exact English keys in the 467 two-language entries of `UiLocalizationService`. The remaining two are literal local file paths, intentionally not natural-language strings. This is *not* proof of complete FR/EN coverage for dynamic runtime messages.
- Reviewed 20 process-heavy services and `MainWindow.xaml.cs`: worker tools such as FFmpeg/Real-ESRGAN, AI analysis, VLC and NVIDIA detection use `ExternalProcessTracker.Start` in reviewed paths. Deliberate `Process.Start` exceptions include Minecraft Launcher, selected user game, ReShade setup, updater helper, Explorer/Settings and elevated DISM/SFC; the latter must **not** be indiscriminately killed during critical OS servicing.
- The current `DlssNrManager.csproj` version and latest public GitHub release both remain **3.2.0**, released 2026-10-07; v4 is not published.
- GitHub ruleset `Protect main` #24538398 requires `build` but **does not require CodeQL**. Issue #82 remains open; the available GitHub connector allows reading this ruleset but not altering repository administration settings.
- PR #127 (bounded retry for transient Streamline asset absence; original head `24ec809ac3a72b7d86c21966579b6d0004bb549b`) passed its Build and CodeQL, and was **squash-merged** to `main` commit `58761014f8e4962a5c271e9c0801677935e399e2`. Producer-side atomic publication issue #123 remains open.
- PR #125 (125-branch audit, R046–R050) was reconciled a second time with the latest main; current branch head `b9a8fd6b064cbcdf8e8026146ace141dc0f5efcd`. New Build/CodeQL runs are still active; **do not merge without current green checks**.
- Draft PR #128 contains R051 ownership-isolation fix and Windows regression test. It is **not verified or merged** as of this checkpoint, and must be reconciled against main after the #125 merge. Prior branch runs do not validate the eventual merged SHA.
- Release blockers: #79 OpenMP redistribution/legal provenance, #82 required CodeQL, #95 physical Windows GPU/crash/rollback/VRAM acceptance, #111 optional updates beyond media, #123 durable Streamline seed publication, #124 semantic branch cleanup. Do not delete any divergent branch or publish 4.0 prematurely.

**Next action:** inspect actual CI runs of #125/#128, diagnose failures if any; merge #125 when required green, then rebase/reconcile #128 against main, retest, merge only after verified. Keep the root journal and append-only checkpoint documents consistent; no full journal truncation.
