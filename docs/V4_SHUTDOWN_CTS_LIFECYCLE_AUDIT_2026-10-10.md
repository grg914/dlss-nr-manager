# V4 shutdown / cancellation-source ownership — 2026-10-10

Status: **candidate source correction only** in draft PR #439. No v4 release, no hardware acceptance, and no Windows machine modified.

## Finding and scope

MainWindow.Closed previously canceled, then immediately disposed, `_mediaOperationCts`, `_permanentVideoCts`, `_aiOriginCts` and `_downloadCenterCts` while their asynchronous operations might still be awaiting native helpers or downloads. Those operation methods already dispose their sources in `finally`. Disposing the source prematurely can cause `ObjectDisposedException` and premature cancellation teardown, especially when closing during a large download or ONNX/video analysis.

## Correction

- Keep `_isClosed = true` and cancellation of all active operations at window close.
- Call `ExternalProcessTracker.BeginShutdown()` at window close to reject new app-owned process launches BEFORE cancellation; existing jobs stay alive until `Shutdown()` terminates leftovers and closes its Windows Job Object.
- Preserve a real child-process regression in `tests/CrashProbe/Program.cs`: the early gate rejects late starts without eagerly killing the current helper, and final Shutdown terminates it.
- The asynchronous operations retain ownership of their `CancellationTokenSource` and dispose them in their `finally` block. The close handler no longer races that ownership.
- The ONNX AI Origin detector owns native `InferenceSession`, a setup semaphore and HTTP resources. If analysis is in progress at close, `MainWindow.Closed` now cancels it but defers detector disposal until the analysis handler's `finally`; otherwise it disposes immediately. Late inference progress, result dialogs and error dialogs are suppressed after close. Only one active inference handler is permitted.
- Download Center async `finally` suppresses post-close list/selection UI refresh, and error branches for official/version probes avoid post-close UI writes.
- Download Center progress callbacks skip updates when closed; operation/import failures are written to the existing redacting centralized logger.
- Tests under `tests/DlssNrManager.Tests/DownloadCenterUpdateProbeShutdownTests.cs` assert cancellation, operation-owned disposal, guarded UI updates and error logging.

## Limits and acceptance requirements

This only addresses a source-level lifecycle hazard. A real Windows RTX test must still exercise media processing, Real-ESRGAN, ONNX inference and interrupted downloads, with exact owned PID+creation identity, no orphan subprocess, no unexpected socket/thread handles, RAM/VRAM attribution, correct preserved outputs and rollback. A user-owned process or intentionally detached updater/cleanup must never be killed by executable name. Existing issue #95 remains OPEN. Run Windows xUnit Build and CodeQL on exact PR head; source commits from API are unsigned and must be staged in signed commits before protected squash merge.

## Regression sequence

Audit => fix => Windows source regression tests => exact-head CI => protected signed merge => owner Windows tests => re-audit. If an earlier head passed but the candidate changed, its verdict is no longer sufficient.
