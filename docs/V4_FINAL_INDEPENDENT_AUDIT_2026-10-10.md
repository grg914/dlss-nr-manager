# V4.0 — independent final source/availability/FR-EN/lifecycle audit (2026-10-10)

**Disposition:** BLOCKED FOR PUBLIC V4 RELEASE. This is a source and upstream-page/API metadata review, not GPU/Windows validation, binary execution, full weight download, signature approval or redistribution permission. Stable production application version remains 3.2.0; the latest public release is `v3.2.0`.

**Scope:** V4 stable only. V4.5/V4.6 ComfyUI, Python/PyTorch, FLUX/Wan/LTX inference prototypes, experimental Caustica ReSTIR and Free Online Game work remain in separate private/experimental lanes and must not be silently promoted.

## Phase 1 — module/function inventory

The exact machine-readable reference is `manifests/feature-policy.json`: 13 feature groups (games-dlss, minecraft-rtx, media-neural, vsr-hdr-video, ai-detection, ai-studio partial, downloads partial, pc-update, pc-cleanup, diagnostics, advanced-optiscaler, optiscaler-log, application) plus the Hardware & Profiles UI. See `docs/V4_FUNCTIONAL_ACCEPTANCE_MATRIX_2026-10-10.md` for 14 full GUI scenarios and 10 cross-module scenarios; every physical runtime scenario remains NOT_RUN/BLOCKED.

Dependencies: .NET Windows desktop/WPF, Microsoft.ML.OnnxRuntime 1.30.0, NVIDIA Streamline, OptiScaler, ReShade, FFmpeg, video2dlssnr, Real-ESRGAN NCNN/Vulkan plus 12 pinned models, VLC 3.0.24 portable, Temurin 25, Fabric/Caustica/SPBRScandi and managed AI model packages. NVIDIA SDK and selected weights are separately restricted/manual, not automatic redistributable binaries. Read-only host VC++ x64 discovery integrated in signed #424; it does not solve OpenMP license #79.

## Phase 2 — French/English and errors

- Existing `UiLocalizationService` maintains hundreds of FR/EN string pairs and refreshes dynamically populated Download Center/upstream lists on locale change. Static extraction of WPF literals found most meaningful text in translation pairs; remaining untranslated interface labels include AI Upscale / Real-ESRGAN, Anime / Illustration and the initial VC++ status. These have mappings added in this security/localization candidate with xUnit round-trip regression tests.
- Existing application-level exceptions are logged but the fatal WPF dispatcher path previously did not present a module/cause/solution/log diagnostic. Candidate introduces `ApplicationFailureReport` and displays a FR/EN structured fatal dialog, then **continues the existing fail-closed crash exit** (`e.Handled=false`); never resume after corrupt state. Off-thread catastrophic crashes can terminate before a UI dialog and are still a real acceptance limitation.
- Full FR/EN visual verification of dynamic runtime messages, GPU status, all dialogs and DPI variants has NOT RUN on an actual Windows desktop. Language catalog coverage is not proof of GUI acceptance.

## Phase 3 — files, build and packaging

- `tests/DlssNrManager.Tests` is the xUnit Windows regression project (with a CrashProbe test helper and a linked XAML markup fixture), referenced by Windows Build CI. It is **not an application runtime dependency**. App `DlssNrManager.csproj` explicitly excludes tests/**, third_party/**, third_party-local/** and Caustica-RTX/** compile/resources from published WPF artifacts.
- `tools/create-application-package.ps1` constructs a deterministic application-only ZIP containing exactly `DlssNrManager.exe`, `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `DATA_PRIVACY.md`. Developer tests, confidential AI/SDK files and vendored full trees do NOT belong in the release ZIP.
- `release.yml` is expected to generate `SHA256SUMS.txt`, `components-manifest.json`, `SBOM.spdx.json`, `release-provenance.json`, `realesrgan-model-manifest.json` for the **future immutable new release**, with signed exact commit, verified contents and legal notices. Existing historical v3.2.0 does NOT have all these.
- Source file placement is materially correct for reviewed main WPF/tests/services. Full LFS/native file/asset packaging and actually running the final published archive are **not certified**. No additional dependency should be vendored just to make a counter or checklist green.

## Phase 4 — external official availability versus installability

- Re-fetched the **25 pinned public GitHub commit references** in `third_party/DEPENDENCIES.lock.json` by exact `/commits/{SHA}` API: 25/25 reachable with matching SHA. Separate local-only NVIDIA SDK remains restricted; no redistribution inference.
- The 10 official Hugging Face model cards listed in `manifests/ai-studio-models.json` all returned an accessible public model page on 2026-10-10 (FLUX.2 klein/dev, Qwen Image 2.1, SDXL base/inpaint, Wan2.2 TI2V/T2V/I2V/Animate, LTX-2.5). A visible model card is NOT proof that each weight/chunk exists, that gated access is granted, or that a full SHA-256 verified model download succeeded.
- Public release v3.2.0: 29 GitHub API assets, all nonempty and with `sha256:` metadata; runtime-seed-v1: 30 such assets including VLC and offline NuGet. These are metadata checks only. The old release has FIVE Real-ESRGAN .param asset sizes disagreeing with application raw-byte pins due to LF→CRLF transformation (#411). Never overwrite immutable old assets. A new reviewed release and byte-level end-to-end test are necessary.
- Official upstream update scanning is **read-only**; it must never map an upstream tag/branch SHA directly into an unverified installed binary. Existing opt-in Download Center Update is approved for version-receipted manager-owned media and eligible manager-owned AI Studio model packages. VLC, Real-ESRGAN, AI-origin, OptiScaler, ReShade, Minecraft and other components lack consistent independently vetted end-to-end per-component immutable version receipts/rollback, so UNKNOWN/REPAIR/NO UPDATE is the correct safe behavior until #111. Do not auto-update restricted models, NVIDIA SDK or drivers or force any update at startup.

## Phase 5 — START → RUN → MONITOR → STOP → CLEANUP

- Existing owner lifecycle control: `ExternalProcessTracker.Start` attaches owned native helpers to a Windows Job Object with KILL_ON_JOB_CLOSE. It rejects new owned launches while shutting down; `MainWindow.Closed` cancels known download, update, media, AI and upstream probes and calls `ExternalProcessTracker.Shutdown`; `App.OnExit` calls it as a fail-safe. Crash handlers kill owned helpers. There is **no need for a second overlapping process manager**.
- Explicit detached operations (user games, Explorer, system elevating repair, external installer and self-update handoff) are exceptions, not processes to terminate by name or globally.
- This is a source-level lifecycle implementation, **not** proof of stopping every GPU worker, thread, local API/socket, native driver job or memory allocation. On-device test #95 must measure processes identified by PID and creation time, CPU/GPU/RAM/VRAM, ports, file handles, staged downloads and user-data preservation under normal close, forced kill, cancelled download, DLL/GPU error, reboot/restart, storage full and locked files. Do not equate job-object closure with GPU release metrics.

## Phase 6 — logging, configuration, security and tests

- Existing `AppLogger` provides centralized rolling logs under %LOCALAPPDATA%/DlssNrManager/logs and user-path substitution; candidate adds TRACE, DEBUG, INFO, WARNING, ERROR, CRITICAL APIs and best-effort token/bearer/API-key/password/GitHub PAT redaction with xUnit tests. One central log is intentional: adding 10 separate directories without consumers would add complexity, increase disk activity and duplicate sensitive material. Diagnostic exports must continue to sanitize.
- Config risks: app still pins net8.0-windows nearing Microsoft end of support 2026-11-10 (#408); ONNX Runtime 1.31.0 update branch is stale/not vetted (#183); 24-member OptiScaler native build compared across two identical source/tool scripts yields 21 identical and 3 differing PE/PDB files (#161), NOT ZIP timestamps only.
- Critical errors: explicit FR/EN fatal dispatcher message added, but fatal OS/process termination cannot always display a dialog. Disk access, network, driver/VRAM, permissions and external process exceptions must remain fail-closed with log evidence. Never print raw passwords, tokens or arbitrary URL credentials.
- Security-sensitive provenance and policies exist: AGENTS, LICENSE, SECURITY, SUPPORT, CONTRIBUTING, THIRD_PARTY_NOTICES, manifests, upstream locks, SHA checks, immutable publishers, release checklist and branch protections. Missing **evidence** rather than random files: OpenMP licensed redistribution #79, Real-ESRGAN exact model bytes #411, confirmed update receipts #111, real Windows RTX/process/rollback #95, .NET 10 migration decision #408.
- GUI testing matrix: for each module INIT, LOAD, RUN, ERROR, STOP, RESTART, SHUTDOWN with absent dependency/driver, GPU unsupported, offline, missing file, cancellation and restart. Integration matrix includes DLSS+OptiScaler, Caustica+SPBR/ScandiShader, Media+RTX, AI+GPU, cross-video pipelines and hardware profiles; mark all as not physically validated, and keep V4.5-only interactions out of V4 release requirements.

## Phase 7 — CLEANUP CANDIDATES only

See `docs/V4_CLEANUP_CANDIDATES_2026-10-10.md`. NO deletion performed as part of this review. Protected main latest commit, all old SHA history and private v4.5 sources are preserved.

## Required release disposition

P0 open: #411 models; #79 OpenMP licensing; #95 on-device rollback/process/VRAM; #111 approved per-component update route. P1 open: #161 native reproducibility, #408 .NET 10 qualification, #124 branch cleanup, #183 onnxruntime candidate. Remaining issues for coordination/V4.5/research intentionally stay open. No V4 stable release/tag/asset publish until actual RTX and license evidence and owner release approval. Repeat audit→patch→tests→audit after each significant change; only passing exact-head Build/CodeQL justifies a protected squash, not actual GPU certification.
