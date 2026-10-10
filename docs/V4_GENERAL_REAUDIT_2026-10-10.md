# V4.0 independent general re-audit — 2026-10-10 (GPT B)

> Scope: public main V4 readiness only. Source, connected GitHub evidence, official-source reference checks. **No Windows RTX acceptance, full model weight execution, licensed OpenMP authorization or V4 public release**. This is a verification report, not a release approval. The experimental V4.5/V4.6 integration remains private/out of release.

## Verified GitHub / CI baseline

- Protected signed main at re-audit start: `45fa40686083c5b6e4d019fff22209c82737a2db`, signed squash PR #429 (six documentary files; final receipts in coordination, progress 1/2/3, README, CHANGELOG). Signed merge Git tree `b01d3020e77d415414639e774e36e1800ed82809` exactly matched its audited source tree.
- Earlier protected PR #426 had 303/303 passing Windows xUnit, Build and CodeQL, deterministic single-file Windows x64 packaging, and signed merge. No on-device GPU/driver tests were claimed. Previous first CI `CS0103` was corrected before protected squash.
- Open PR count after #429: 23 remaining, all future/experimental or historically unique journal/handoff records. Open issues: 12. Do not close historical journal PRs absent exact preservation proof or merge V4.5 experiments into V4.
- Latest public release: `v3.2.0`. Assembly version is still 3.2.0; **no final V4 ZIP/release has been published**.

## Features, source files, packaging, languages

- `manifests/feature-policy.json` has 13 declared groups plus Hardware & Profiles. AI Studio and parts of Download Center are marked partial. V4.5 cloud/local ComfyUI, FLUX and LTX graph execution must not be described as V4 complete.
- `tests/DlssNrManager.Tests` is the xUnit regression project plus CrashProbe and a linked WPF markup fixture: development/CI only, NOT an installed application dependency. The production .csproj excludes tests, local SDK/vendored trees and documentation mockups from compilation and publishes only the approved application payload/metadata.
- FR/EN WPF catalog and bilingual fatal dispatcher diagnostics are in main. xUnit covers translation pairs and application failure formatting. Visual QA for 100% of windows, runtime-generated messages, DPI, drivers and exceptions cannot be certified without actual Windows GUI operation.
- Existing policies and packaging gates include LICENSE, SECURITY, SUPPORT, DATA_PRIVACY, THIRD_PARTY_NOTICES, signed protected branches, release checklist, lock manifests, deterministic ZIP, SHA256SUMS, SBOM/provenance production workflow. Final new release metadata must be generated and rechecked for the actual candidate, not inferred from old v3.2.0.

## Dependencies, official source availability and optional updates

- Re-queried GitHub commits API on re-audit: **25/25 exact public GitHub SHA locks reachable and matching** `third_party/DEPENDENCIES.lock.json`. This certifies source refs, not binaries. Restricted NVIDIA SDK remains local-only. Official model card pages were independently checked in the preceding 2026-10-10 audit, 10/10 visible; gated file access, actual full weight byte checks, signatures/licenses and inference are **NOT verified**.
- Existing released `v3.2.0` has 29 GitHub asset entries with SHA256 metadata and separate manager `runtime-seed-v1` has 30; historical five Real-ESRGAN `.param` files mismatch locked raw-byte sizes because LF→CRLF (#411). No overwrite of the immutable old release; a new approved byte-verified immutable candidate is required.
- `manifests/component-policy.json`: 18 categories, while Download Center has five operation kinds (media video2dlssnr/FFmpeg, VLC, Real-ESRGAN, AI-origin, AI Studio model packages). Some other components belong to installer/game/Minecraft/config flows or are restricted SDK/build dependencies. An official upstream HEAD/tag does **not** authorize an executable update. Only a supported newer, pinned, manager-owned package with installed receipt and SHA/license/rollback may show a selectable *optional* Update. Suppress Update for Unknown/Offline/Older/Gated/Unreviewed; leave #111 open until more component routes are approved.
- Main .NET target `net8.0-windows` becomes unsupported by Microsoft **2026-11-10**. Upgrade to .NET 10 LTS (supported until **2028-11-14**) on a separate signed compatibility/test candidate before long-lived V4 release (#408). Do not arbitrarily bump ONNX 1.30 to 1.31 (#183) or replace nondeterministic native OptiScaler assets (#161) without compatibility, licenses and hashes.

## Process lifecycle / shutdown / GPU / error handling

- Existing `ExternalProcessTracker` starts app-owned helpers under Windows Job Object KILL_ON_JOB_CLOSE and blocks new starts during Shutdown; `MainWindow.Closed` cancels known media/AI/download/update tasks; `App.OnExit` shuts down owned process tree. Detached user-opened Explorer, games, installer/self-update handoffs intentionally have separate ownership. A second overlapping shutdown manager would be harmful.
- A true START → RUN → MONITOR → STOP → CLEANUP guarantee requires **physical Windows RTX** observation of children PID/creation time, subprocesses, native and Python runtimes, sockets, downloads, files, RAM/VRAM/GPU, rollback and force-kill/restart on real data (#95). CI/xUnit confirms no such operational measurements.
- Existing centralized `AppLogger` exposes TRACE/DEBUG/INFO/WARNING/ERROR/CRITICAL, message credential masking, user-path substitution and rolling logs. Its original logging redaction did **not** cross the `DiagnosticService` support ZIP boundary: arbitrary external `OptiScaler.log`, `OptiScaler.ini` and JSON/history/manifest strings were exported with only home-directory substitution. This is a confirmed privacy flaw exposed by source inspection.
- Re-audit candidate fix: apply centralized `AppLogger.RedactSensitiveData` and `SanitizeUserPaths` at **every `DiagnosticService.AddTextEntry` ZIP entry**; strengthen redaction for Basic/Token auth and JSON quoted key cases; add an xUnit ZIP-level test with artificial secret-bearing external log, INI and query string, assert secrets absent and unrelated status retained. Do not claim completion before exact-head Windows Build + CodeQL and protected squash.
- Regex masking is best effort, not guaranteed arbitrary secret detection. Never store actual credentials as fixture literals or make broad claims of perfect anonymization. Users should review any external support bundle before sharing it.

## CI / test matrix & release hold

- Coverage plan per feature: INIT, LOAD, RUN, ERROR, STOP, RESTART, SHUTDOWN; offline, missing DLL/model/file, incompatible GPU/driver, permission denied, cancellation, multi-process, forced exit, locked file, download rollback and restart. Integration cross-check DLSS+OptiScaler, Minecraft Caustica+SPBR/ScandiShader, media+GPU/VSR-HDR, AI+GPU and hardware profiles, only for modules truly in public V4.
- Last fully passed candidate prior to this new change: #426 exact signed Windows CI, 303/303 xUnit, packaging and CodeQL; public main #429 requires its own push CI conclusion. New privacy regression candidate must be validated separately before protected merge. CI is not equivalent to native RTX acceptance.
- **Release BLOCKED**: #411 Real-ESRGAN assets, #79 OpenMP redistribution/license, #95 physical Windows/RTX and transactional acceptance, #111 approved per-component selective Update. Maintain #408 .NET support, #161 OptiScaler PE/PDB reproducibility, #183 upstream ONNX candidate, #124 stale branch archival. No release tag, no asset replacement, no automatic external installs.

## CLEANUP CANDIDATES / no deletion

Inventory remains `docs/V4_CLEANUP_CANDIDATES_2026-10-10.md`: historical docs/source branches, content-addressed OptiScaler builds, old released model assets, excluded mockups/tests, app-managed caches. Every row has reason, dependencies, deletion risk and action. **No deletion or force-push is authorized by this audit.**

## Phase outcome and next actions

1. Audit — verified signed main, source/CI, 25 source locks, license/source/readiness and privacy defect.
2. Correction — targeted ZIP privacy boundary and tests in isolated branch; no native/SDK/release changes.
3. Tests — exact-head Build/xUnit, CodeQL, deterministic ZIP, signed Git tree parity required.
4. Re-audit — compare candidate with stable main (only planned files); verify semantic security, no accidental test/runtime inclusion; note any physical limitations explicitly.
5. Release — hold until real RTX/transactional proof, upstream byte/rights approval and owner authorization.
