<p align="center">
  <img src="assets/branding/logo.png" alt="DLSS NR Manager logo" width="128" />
</p>

# DLSS NR Manager

<p align="center">
  <img src="assets/branding/hero.png" alt="DLSS NR Manager banner" width="100%" />
</p>

A native Windows manager for installing, diagnosing and maintaining the experimental **OptiScaler DLSS Neural Rendering (DLSSNR)** stack, with PC update, cleanup, media AI and Minecraft RTX utilities.

Current application version: **v3.1.0**.

> Supports automatic candidate detection across **Steam, Epic, GOG, itch.io, Ubisoft Connect, EA App, Xbox App and Battle.net**, with generation-aware support for NVIDIA GeForce RTX 20/30/40/50 GPUs, manager-owned validated NVIDIA runtime bundles, PC software/driver update checks and safe Windows/NVIDIA cache cleanup.

## Screenshots

<p align="center">
  <img src="docs/screenshots/Capture%20d'%C3%A9cran%202026-10-05%20204315.png" alt="DLSS NR Manager Windows dashboard" width="100%" />
</p>

<p align="center"><sub>Application capture on Windows — full-resolution PNG, not a generated UI mockup.</sub></p>

## DlssNrManager Menu

`MainWindow.xaml` now uses the production **DlssNrManager Menu** navigation: a fixed left-side menu replaces the previous single long scrolling dashboard while preserving every existing named control and event handler.

Design references are kept under:

- `docs/mockups/dlssnrmanager-menu/index.html` — interactive browser reference
- `docs/mockups/dlssnrmanager-menu/MainWindow_DlssNrManager_Menu_Mockup.xaml` — WPF layout reference

The production menu exposes focused pages for Games & DLSS, Minecraft RTX, Media Neural, AI-origin detection, PC Update Center, PC Cleanup, Diagnostics, Advanced OptiScaler, OptiScaler log and Application. Each page has its own vertical scroll area, while the left menu stays fixed.

## Self-contained monorepo migration

The project now uses a self-contained monorepo model for project-owned and redistributable dependencies. **Caustica RTX** lives directly under `Caustica-RTX/`, while pinned third-party source snapshots live under `third_party/`.

Use `tools/vendor-third-party.ps1` to import pinned source snapshots without nested Git repositories. `third_party/DEPENDENCIES.lock.json` records immutable source refs, and `third_party/minecraft/RUNTIME.lock.json` freezes the exact Minecraft 26.2 runtime artifacts/hashes.

The Minecraft installer now resolves Caustica only from **DLSS NR Manager releases**. Release automation prefers a local `Caustica-RTX/build/libs` production JAR and otherwise reuses a previously bundled Caustica JAR from this repository's own release history. It no longer queries the standalone `grg914/Caustica-RTX` release feed.

A literal zero-external-toolchain build is not possible: Windows, GPU drivers, Minecraft, Java/MSVC/Vulkan tooling and license-restricted NVIDIA SDK inputs remain external prerequisites. NVIDIA/DLSS is therefore treated as a local-only build input rather than blindly vendored into the public repository.

## Download

Use the latest GitHub Release for the Windows x64 single-file build. Current releases publish:

- `DlssNrManager.exe`
- `DlssNrManager-win-x64.zip`
- `SHA256SUMS.txt` covering every bundled release asset
- the latest compatible Minecraft 26.2 Caustica RTX production JAR
- `SPBRScandi.zip`, the validated SPBR-based Scandi resource pack
- manager-owned validated runtime assets for FFmpeg, Real-ESRGAN, OptiScaler, ReShade, AI-origin models, Streamline/video Neural Rendering and Minecraft 26.2

## What's new in v3.1.0

v3.1 completes the self-contained runtime/release migration while preserving the reliability-first behavior introduced in v3.0.

- **Manager-owned runtime channel.** OptiScaler, Real-ESRGAN, ReShade, FFmpeg, `video2dlssnr`, Streamline/NVIDIA runtime resources, AI detector weights and Minecraft runtime assets are consumed from DLSS NR Manager releases instead of separate upstream release feeds.
- **Strict dependency audit in CI.** Every build fails if runtime/release code reintroduces direct dependencies on the retired NVIDIA, OptiScaler, FFmpeg, Real-ESRGAN, ReShade, Hugging Face, Fabric or Modrinth runtime URLs.
- **Deterministic Minecraft 26.2 bundle.** `third_party/minecraft/RUNTIME.lock.json` freezes Fabric Loader 0.19.3, Fabric Installer 1.1.2, the Fabric profile/libraries and the selected Fabric/Modrinth artifacts by exact version, filename and hash. The application installs Fabric from the manager-owned bundle without contacting Fabric Meta/Maven or Modrinth at normal runtime.
- **Stable Minecraft performance set.** The validated bundle contains Fabric API, Lithium, FerriteCore, Krypton, BadOptimizations, Dynamic FPS and SPBR LabPBR. C2ME is intentionally excluded until a stable 26.2 release is available.
- **Reproducible native/media builds.** OptiScaler, Real-ESRGAN, ReShade and FFmpeg build from vendored sources with dedicated CI workflows; FFmpeg no longer depends on BtbN release binaries.
- **Validated NVIDIA bootstrap.** Streamline is pinned to the official v2.14.1 package and verified by digest/signature. Neural Rendering bootstrap accepts only a valid NVIDIA-signed `nvngx_dlssnr.dll`, which is never committed to the source tree.

## What's new in v3.0.0

v3.0 is a full reliability, performance and AI-detection audit of the v2.1 codebase. The release keeps the existing transactional install/restore model while tightening network, integrity and inference behavior.

- **Managed-file integrity verification.** New installs store SHA-256 hashes for manager-owned files. **Verify managed files** reports missing, modified, legacy/unhashed files and interrupted transactions without touching user-owned files.
- **Lower-allocation renderer scanning.** PE/API marker discovery now streams bounded chunks instead of reading large executable/DLL regions into one string. DXVK/vkd3d detection keeps separate wrapper identity and Vulkan-loader evidence to reduce false positives.
- **More reliable downloads.** OptiScaler, media components, Real-ESRGAN engines/models, ReShade, NVIDIA Streamline and application self-updates use a bounded three-attempt retry policy for transient network/HTTP failures only. Permanent validation/hash failures are never retried. Large GitHub release downloads now have a 10-minute transfer budget instead of the previous 30-second timeout.
- **AI detector Quick / Balanced / Thorough modes.** Quick uses the model-native 224×224 full-frame view and a short video sample. Balanced combines the native view with three aspect-preserving crops and up to 20 uniform video frames. Thorough adds a second crop scale when useful plus scene-change sampling for broader temporal coverage.
- **AI confidence now measures internal consistency.** The detector tracks disagreement between the two ONNX models, spatial-view consistency, temporal video consistency and input resolution. Strong AI verdicts require both models plus spatial/temporal agreement; unstable or very low-resolution material is deliberately pushed toward **Uncertain**.
- **Safer provenance interpretation.** Structured image metadata and ffprobe tag metadata are separated from unverified strings found in raw file bytes. Generator metadata is supporting evidence only: it cannot produce a strong AI verdict unless the visual ensemble also agrees. Conflicting visual evidence is surfaced explicitly. C2PA / Content Credentials presence remains provenance information, not proof by itself.
- **Faster bounded video inference.** Frame classification uses limited parallelism to improve throughput without allowing ONNX sessions to oversubscribe the CPU.
- **Cancellable AI analysis.** Long image/video analyses can be cancelled from the UI instead of requiring the application to be closed.
- **Exportable AI report.** The detector can export a JSON report containing the source filename, source SHA-256, scores, confidence, consistency metrics, provenance signals and analysis mode.
- **Release integrity improved.** `SHA256SUMS.txt` is generated after Caustica/SPBRScandi validation so bundled Minecraft assets are included in the published checksum manifest. The release workflow now runs the regression test suite and verifies the produced EXE version before publishing.
- Existing v2.1 features remain: per-game transactional recovery/history, configurable scan roots, multi-executable renderer selection, recent OptiScaler builds per game, safe WPF software rendering, self-update, managed Minecraft updates and bundled Caustica/SPBRScandi assets.

## What's new in v2.1.0

v2.1 applies the strongest reliability and workflow ideas identified while auditing DLSS5-Swapper, reimplemented for the native WPF/.NET codebase.

- Added a per-game transaction journal with explicit PREPARED / BACKED_UP / WRITING / VERIFIED / COMMITTED stages and automatic recovery of interrupted manager-owned installs when a valid backup exists.
- Added bounded per-game history for install, update, rollback, restore, executable selection and OptiScaler build selection.
- Added richer renderer detection for DirectX 8/9/10/11/12, DirectDraw, Vulkan and OpenGL using executable markers, explicit renderer filenames and known engine renderer modules.
- Added DXVK/vkd3d wrapper recognition so Vulkan translation DLLs are not mistaken for normal DirectX targets.
- Existing newer NVIDIA DLSS/Streamline runtime files are preserved instead of being downgraded by an incoming package.
- Added game-context actions for launch, open folder, copy path, rescan, choose executable, history and restore.
- Added configurable custom scan folders plus an explicit opt-in full fixed-drive scan; launcher discovery remains the default.
- Expanded scan exclusions for asset trees, installers, redistributables, anti-cheat folders, logs, downloads and backup trees to reduce I/O and false positives.
- Added multi-executable selection with a per-game preferred executable marker.
- Expanded support bundles with renderer detection, game history and pending transaction state.
- Added an automated regression-test project covering history bounds, transaction path safety, renderer detection, engine-module detection, DXVK translation and preferred-executable containment.
- Added a persistent software-rendering mode for the WPF UI as a driver/compositor compatibility fallback.
- Added direct game launch from the manager.
- Added selection from recent OptiScaler builds with the chosen build remembered per game.
- Added a before/after installation review showing current/target OptiScaler build, proxy, renderer, executable and runtime preservation behavior.
- Retains the v2.0.2+ managed Minecraft updater and bundled Caustica/SPBRScandi release workflow, plus the v2.0.3 self-update button.

## What's new in v2.0.0

v2.0 is a reliability, diagnostics and workflow release built on the v1.5 audit work.

- **AI detection is now independent from Media Neural.** The **Détection IA** page has its own **Add media** picker, its own selected-media field, and a **Remove** action. Selecting or clearing media here no longer changes the Media Neural source.
- **Minecraft RTX now detects an existing managed installation.** The main action changes from **Install DLSS / RTX** to **DLSS / RTX installed**, is disabled to prevent stacking another install over it, and **Restore original** remains available.
- **Check & download NVIDIA files** now downloads the latest public x64 **NVIDIA-RTX/Streamline** release directly from NVIDIA's GitHub release, stages the public production SR/RR/FG/Reflex resources into the selected Minecraft instance's managed runtime directory, then reports which DLSS-NR-specific files are still absent upstream. The manager never fabricates, renames or substitutes an unofficial DLSS-NR DLL/header.
- Added **Save support bundle** under Diagnostics. It creates a ZIP containing a sanitized compatibility report, tail-limited manager logs, OptiScaler log/config and the managed install manifest when available. User-profile paths are replaced with environment tokens before export.
- Hardened game discovery so recursive probing does **not follow Windows junctions/reparse points**, preventing accidental scans outside a game tree and reducing duplicate I/O.
- Simplified update extraction to use the shared **SafeZip** path with the same traversal, entry-count and expanded-size protections used by component installs.
- Steam artwork catalog caching now detects corrupt/partial cache data, discards it and rewrites the cache atomically.
- Added asynchronous atomic text writes for large network-backed cache updates.
- Release automation now emits **SHA256SUMS.txt** beside the EXE and ZIP.
- Retained the v1.5.1 hardening for bounded downloads, model integrity, helper-process cleanup, anti-cheat fail-closed behavior, atomic pack deployment and self-update validation.
- Minecraft Scandi pack repair/update no longer reuses an already-installed destination ZIP as its own update source, preventing a stale/broken local copy from reinstalling itself indefinitely.
- Caustica RTX compatibility work is tracked separately in `grg914/Caustica-RTX`: ScandiShader now receives a stronger SDR/HDR-safe grade, and high-resolution alpha overlays use a conservative RT opacity path to avoid black triangular wedges with 512x packs.
- Reviewed the public **DLSS5-Swapper** project for workflow ideas. v2.0 adopts the broadly useful patterns of attachable diagnostics, checksummed release artifacts and tightly bounded discovery without importing its application code.

### DLSS5-Swapper audit notes

Useful ideas identified for future versions, but intentionally not rushed into v2.0:

- per-game installation/history timeline with recovery from retired manifests;
- richer rendering-API detection and explicit renderer overrides;
- configurable scan roots instead of any drive-wide default scan;
- game-context actions such as rescan, open folder and restore originals;
- a larger automated regression-test matrix for scanning, restore and compatibility edge cases;
- optional in-game controls only where a supported integration can expose them safely.

## What's new in v1.5.1

- Moved NVIDIA GPU detection off the WPF UI thread so startup stays responsive while registry/`nvidia-smi` probing runs.
- Improved multi-GPU selection: when multiple RTX adapters share the same generation, the highest RTX model number is preferred instead of alphabetical order.
- Hardened game-state refresh against rapid selection changes so an older anti-cheat scan cannot overwrite the status of a newly selected game.
- Anti-cheat scans now stay inside the selected game tree and skip directory reparse points/junctions instead of following them outside the target.
- Installation now fails closed if the anti-cheat safety scan itself errors, instead of allowing an unhandled async UI exception.
- Removed an unsafe direct `ComboBoxItem` cast in proxy selection and added a deterministic fallback.
- NVIDIA Neural Rendering discovery no longer trusts an empty/truncated cache file and writes its package index atomically.
- Streamline runtime provenance markers are written atomically.
- PC Update Center now only opens cached actions that resolve to HTTPS URLs or `ms-settings:` targets.
- Stale media helper `Process` handles are disposed deterministically after cleanup.
- ReShade and Minecraft launcher actions now report launch failures instead of silently continuing.
- Centralized ZIP extraction behind a bounded safe extractor with traversal protection, entry-count limits and a 4 GB expanded-size ceiling for downloaded component archives.
- Self-update ZIPs now accept only `DlssNrManager.exe`, cap archive size/entry count, and no longer fall back to an arbitrary executable found in the archive.
- Minecraft Scandi pack copies are now atomic, preventing a crash/interruption from leaving a partially written resource pack.
- Updated the optional `ScandiTextureV1.zip` release fingerprint to the sanitized pack with Fresh Flower Pots removed: `221D8A4B698120812B4ADDAA003EC5E67E2BD91513FAB6DABCF534FCF90151B0`.

## Previous v1.5.0 changes

- Replaced the production long-scroll dashboard with **DlssNrManager Menu**, a fixed left navigation layout.
- Preserved all existing WPF named controls and all existing event handlers while moving them into focused pages.
- Split **AI origin detection** into its own menu page without duplicating controls or logic.
- Dedicated pages now exist for Games & DLSS, Minecraft RTX, Media Neural, AI-origin detection, PC Update Center, PC Cleanup, Diagnostics, Advanced OptiScaler, OptiScaler log and Application.
- Increased the production window default width to 1440 px to accommodate the persistent navigation rail while retaining the existing minimum height and resizable custom window chrome.
- Renamed the UI design references to **DlssNrManager Menu** and moved them to `docs/mockups/dlssnrmanager-menu/`.
- The documentation XAML remains excluded from WPF compilation; only the production `MainWindow.xaml` is compiled.
- The migration was validated by comparing the old/new XAML inventories: all original named controls and all original click/selection/value/title-bar handlers are still present exactly once.

## Previous v1.4.2 changes

- Added the complete **DlssNrManager Menu navigation reference** under `docs/mockups/dlssnrmanager-menu/`, mapping every control currently exposed by the production WPF window into focused pages: Games & DLSS, Minecraft RTX, Media Neural, AI-origin detection, PC Update Center, PC Cleanup, Diagnostics, Advanced OptiScaler, Logs and Application.
- Hardened external helper lifetime with a Windows **Job Object** using `KILL_ON_JOB_CLOSE`; manager-owned Real-ESRGAN, video2dlssnr, FFmpeg/ffprobe, Fabric/Java/WinGet and preflight helpers are also explicitly terminated on cancellation.
- Added a **single-instance guard** to prevent concurrent installs or config mutations.
- Removed the abrupt `Environment.Exit` shutdown path and centralized clean process/resource shutdown.
- Fixed Windows download/file-lock races by closing download streams before hashing/moving release and NVIDIA Streamline files.
- Hardened runtime trust: NVIDIA-signed x64 runtimes accepted at install are preserved in the managed manifest, changed/unverified runtimes are not silently re-enabled, and RTX 20/30/40 diagnostics no longer require Neural Rendering.
- Removed sync-over-async hashing and made runtime validation cancellable.
- Made OptiScaler INI parsing tolerant of whitespace/comments and able to add missing supported keys; Neural Rendering still requires the upstream `[DlssNr]` section.
- Verifies both local AI-origin ONNX model hashes before the first inference session of each run.
- Improved multi-GPU detection by selecting the highest supported RTX generation instead of the first NVIDIA registry entry.
- Made ReShade downloads atomic, SHA-256 verified and restricted to the official HTTPS host.
- Added bounded artwork downloads, orphan artwork cleanup, stale self-update cleanup and a six-hour cache for automatic component-update checks.
- Isolated individual launcher discovery failures so one corrupt/inaccessible Steam/Epic/Xbox/etc. source does not abort the whole game scan.
- Verifies GitHub release digests for Scandi pack downloads and validates the complete manual Minecraft DLSS package before staging any file.
- Hardened managed paths against traversal and refuses destructive legacy uninstall when ownership cannot be established.
- Supplemental NVIDIA files are now registered in the uninstall manifest and optional resource-staging failures no longer invalidate an otherwise successful base install.
- GitHub Actions are pinned by commit SHA, build permissions are reduced, and release builds fail on compiler warnings.

## Previous v1.4.1 changes

- Reworked **AI origin detection** to reduce false positives on real photos and videos.
- Replaced the single-model decision with a conservative **two-model ONNX ensemble**:
  - CapCheck ViT AI-vs-Real (CIFAKE-derived)
  - AI Image Detect Distilled ViT
- Preserves the source aspect ratio before inference instead of stretching portrait/landscape media to 224×224.
- Evaluates **three spatial crops** per image/frame and uses median aggregation.
- Video analysis now samples up to **32 frames** across the full timeline and requires both detectors to agree strongly on most frames before returning `Likely AI-generated`.
- Large detector disagreement now returns `Detector disagreement / uncertain` instead of a false positive.
- C2PA / Content Credentials presence by itself is no longer treated as proof of AI generation.
- The result dialog now exposes both model scores, ensemble score and model disagreement.
- Increased the detected-games list height from 280 px to 430 px to use the available dashboard space more effectively.
- Added strict media-helper lifecycle cleanup: Real-ESRGAN, video2dlssnr, FFmpeg and ffprobe are tracked, terminated at operation end/cancellation, killed on app exit, and stale helpers from older interrupted runs are cleaned on next startup.

## Previous v1.4.0 changes

- Added a generation-aware **RTX capability matrix** for GeForce RTX 20/30/40/50 Series.
- RTX 20/30: DLSS Super Resolution + Ray Reconstruction where the selected game/integration supports them.
- RTX 40: adds DLSS Frame Generation support; Multi Frame Generation and 3D-Guided Neural Rendering remain unavailable.
- RTX 50: enables the full supported stack, including Frame Generation, Multi Frame Generation and 3D-Guided Neural Rendering when the required NVIDIA runtime/SDK is available.
- Non-RTX or unrecognized NVIDIA GPUs now show an explicit compatibility warning and game installation is disabled instead of failing later.
- Added a prominent **offline/single-player only** warning for game injection/modification flows.
- Added local anti-cheat signal detection (Easy Anti-Cheat, BattlEye, EA AntiCheat, FACEIT, Riot/Vanguard, RICOCHET, EQU8, XIGNCODE/GameGuard families). Installation is blocked when known anti-cheat files are detected.
- General OptiScaler installation no longer requires a DLSS-NR runtime on RTX 20/30/40. Neural Rendering is disabled automatically and only hardware-supported NVIDIA resources are staged.
- Improved NVIDIA GPU detection with an `nvidia-smi` fallback in addition to the Windows registry.
- Updated the UI to disable Neural Rendering controls on GPUs that do not support them.

## Previous v1.3.0 changes

- Added **Check NVIDIA files** in Minecraft RTX to query only official `NVIDIA/DLSS` and `NVIDIA-RTX/Streamline` sources for public DLSS Neural Rendering headers and runtime DLLs.
- The availability check requires both an API/header signal and a runtime DLL signal before reporting a deployable public DLSS-NR integration; renamed or unofficial DLLs are never accepted as proof.
- Added local **AI origin detection (beta)** for images and videos using an ONNX classifier, multi-frame FFmpeg sampling and generator/provenance metadata hints.
- Added automatic ScandiTexture/ScandiShader staging support for the Minecraft RTX one-click flow, with ZIP validation and rollback.
- Added support for a locally supplied Caustica-adapted `ScandiTextureV1.zip` and the native **ScandiShader RTX Look** path. Public release-asset fallback is used only when an authorized matching asset exists.
- Fixed modern FFmpeg video frame extraction by replacing legacy `-vsync` usage with `-fps_mode passthrough`.
- Improved release automation so the current Windows x64 EXE/ZIP is published from CI.

## Features

### Game detection and DLSS Neural Rendering

- WPF / .NET 8, Windows x64
- Self-contained single-file executable
- Installed-game scanning through Steam, Epic, GOG, itch.io, Ubisoft Connect, EA App, Xbox App and Battle.net
- Compatibility confidence: Validated / Probable / Candidate
- Manual game-folder selection fallback
- NVIDIA GPU and RTX-generation detection with registry + `nvidia-smi` fallback
- Generation-aware feature gating: RTX 20/30 = SR + RR; RTX 40 = SR + RR + FG; RTX 50 = SR + RR + FG + MFG + 3D-Guided Neural Rendering, subject to game/runtime support
- Stable/prerelease selection plus recent-build selection from the upstream OptiScaler-DLSSNR fork, remembered per game
- Full managed-install backup before game-directory changes
- Multiplayer/anti-cheat safety guard: prominent warning on every general game install and hard block when known anti-cheat files are detected
- Install, update, restore and uninstall flows
- **Verify managed files** performs SHA-256 integrity checks for manager-owned files and reports missing/changed state without modifying the game
- Proxy conflict detection; unknown proxy DLLs are never silently overwritten
- Integrated `OptiScaler.log` viewer and per-game compatibility diagnostics
- Persistent application log with exception stack traces and one-click access to the log folder
- Neural Rendering presets plus advanced OptiScaler overlay/process settings
- Game cover artwork with Steam and multi-provider fallback resolution
- Dedicated Battlefield 6 / BF6 artwork handling
- Local artwork cache with **Clear cover cache**
- Multi-resolution Windows application icon generated from the sharp branding PNG before compilation, with exact Windows DPI frames and progressive downsampling
- In-app version check against the latest GitHub Release
- One-click self-update: downloads the newest published EXE/ZIP, verifies GitHub SHA-256 when available, validates executable version metadata, replaces the running EXE after shutdown and restarts automatically

### Application diagnostics and logs

DLSS NR Manager writes a persistent diagnostic log to:

```text
%LOCALAPPDATA%\DlssNrManager\logs\dlss-nr-manager.log
```

When the file reaches 5 MB, the previous log is retained as:

```text
%LOCALAPPDATA%\DlssNrManager\logs\dlss-nr-manager.previous.log
```

The log is designed for end-to-end troubleshooting and records timestamps, application version, OS/framework/process architecture, GPU detection, update checks, Minecraft RTX preflight results, Minecraft installation/restore progress and exception stack traces. Use **Open diagnostic logs** in the application and send the latest `dlss-nr-manager.log` when reporting a runtime problem.

The logger is best-effort and is never allowed to prevent the application from starting or shutting down.

### Automatic NVIDIA runtime/resources

- **Check NVIDIA files** inspects the current DLSS NR Manager release for the validated manager-owned Streamline and Neural Rendering runtime bundles
- Runtime downloads come from this repository's release assets, not directly from NVIDIA release feeds during normal application use
- Streamline is pinned to the validated v2.14.1 x64 package; the bootstrap verifies its upstream SHA-256 before repackaging the required runtime files
- `nvngx_dlssnr.dll` is accepted only when the bootstrap/runtime validation confirms x64 architecture and a valid NVIDIA Authenticode signer
- Existing vendor DLLs in a game are not overwritten by the resource-completion step
- The manager can stage the supported Streamline/DLSS resources required by the selected feature set, including SR/RR, Frame Generation, Reflex and the validated Neural Rendering runtime when available
- Proprietary NVIDIA SDK/runtime material is not committed as standalone source-tree content; restricted SDK inputs remain local build/bootstrap prerequisites

### PC Update Center

- Scans installed software for available updates using WinGet
- Merges `winget upgrade` and `winget list --upgrade-available` results
- Optional Chocolatey `outdated` detection when Chocolatey is installed
- Scans Windows Update for Windows, driver and firmware updates
- Reads connected-device driver metadata when Windows Update exposes it
- Detects NVIDIA GPU/driver version through `nvidia-smi` when available
- Reports whether Windows Update currently offers a newer NVIDIA display driver
- Links to the official NVIDIA driver page for the definitive Game Ready / Studio check
- **WinGet update all** button with explicit confirmation before running `winget upgrade --all`
- WinGet action never installs BIOS/firmware or Windows Update items
- UTF-8-safe PowerShell/Windows Update parsing for localized Windows installations

### PC Cleanup

Safe, explicit cache analysis and cleanup inspired by system-cleaner workflows:

- Current-user temporary files
- Windows Temp
- DirectX shader cache
- NVIDIA DXCache
- NVIDIA GLCache
- NVIDIA legacy `NV_Cache`
- Per-category selection
- Analyze total reclaimable size before deletion
- Locked/in-use and inaccessible files are skipped
- Cache directories are preserved; only contents are cleaned
- Re-analysis after cleanup shows remaining cache size

The cleaner intentionally does **not** touch browser profiles, documents, downloads, registry entries, restore points, Recycle Bin data or Windows Update storage.

### Media Neural Rendering, AI Upscale and AI-origin detection

- Local image/video Neural Rendering
- Media engine downloads the validated `video2dlssnr` and FFmpeg packages from DLSS NR Manager releases
- Native, 2× and 4K media output presets
- Default / Natural / Cinematic Neural Rendering styles
- Real-ESRGAN NCNN Vulkan AI Upscale
- AI Upscale 2× / 3× / 4×
- General, conservative, illustration/anime and anime-video/Minecraft models
- Optional TTA and tile-size controls
- Combined **Neural Rendering + AI Upscale** processing mode
- Video audio is preserved during processing
- **AI origin detection (beta)** runs locally with two independent ONNX classifiers and three selectable modes: **Quick** uses the model-native full-frame view with a short video sample; **Balanced** adds aspect-preserving spatial crops and uniform video coverage; **Thorough** adds a second crop scale plus scene-change frames
- AI-origin confidence incorporates model disagreement, spatial-view consistency, temporal consistency and source resolution; strong AI verdicts require agreement across these signals. Generator metadata alone never produces a strong AI verdict
- Structured generator metadata is distinguished from unverified raw byte markers; C2PA / Content Credentials presence is reported as provenance but is not treated as proof of AI generation
- Long AI-origin jobs can be cancelled, results can be exported as a JSON report with the media SHA-256 for reproducibility, and the detector models can be explicitly verified/repaired against their pinned SHA-256 fingerprints
- AI-origin results remain explicitly advisory: absence of a signal does not prove human origin, and the manager intentionally prefers `Uncertain` over an unsupported confident verdict
- The two bundled open classifiers are screening signals, not universal forensic proof. New generators, compression, screenshots, rescaling and editing can substantially reduce detector accuracy; v3 therefore deliberately lowers confidence when models, spatial views, temporal samples or provenance evidence disagree

### Minecraft Java RTX

- Minecraft Java 26.2 profile
- Detects vanilla and common custom launcher instances
- Optional manual instance selection
- **RTX preflight** runs before any instance modification and reports **Ready / Warning / Unsupported** for:
  - NVIDIA RTX GPU detection
  - NVIDIA driver presence/version
  - Vulkan ray-tracing capability when `vulkaninfo` is available, with Vulkan-driver fallback detection
  - Java 25
  - Minecraft 26.2
  - Fabric Loader 0.19.3+
  - known renderer conflicts
  - instance write access
- **Install DLSS / RTX** one-click workflow:
  - blocks on unsupported preflight results and requires explicit confirmation for warnings
  - creates an application-managed backup snapshot before changing the instance
  - sets `preferredGraphicsBackend:"vulkan"`
  - verifies Java 25 x64 before Fabric/Caustica setup
  - installs Eclipse Temurin 25 automatically with WinGet when Java 25 is missing
  - installs pinned Fabric Loader 0.19.3 from the validated manager-owned Minecraft runtime bundle on Mojang/Microsoft-style instances
  - requires launcher-managed Fabric to be installed from Prism/Modrinth/CurseForge/GDLauncher when those launchers own the instance metadata
  - installs pinned Fabric API 0.161.0+26.2 from the manager-owned bundle and re-verifies its SHA-512 hash
  - uses the tested Caustica RTX Minecraft 26.2 production JAR bundled in DLSS NR Manager releases; the standalone Caustica release feed is no longer a runtime fallback
  - optionally installs the validated non-renderer performance set: **Lithium + FerriteCore + Krypton + BadOptimizations + Dynamic FPS**; C2ME is excluded from the stable 26.2 bundle while only Alpha builds are available
  - optionally installs the bundled **SPBRScandi** resource pack directly into Minecraft `resourcepacks`; it keeps the compatible SPBR LabPBR terrain/material base and adds the validated Scandi sky, End, GUI and visual assets
  - the optional legacy ScandiShader archive can still be staged when available; the RTX renderer itself uses Caustica's native ScandiShader RTX Look rather than Iris
  - verifies the locked SHA-512/SHA-1/SHA-256 fingerprints again before installing bundled mods, Fabric libraries and installer provenance artifacts
  - temporarily backs up known conflicting world-renderer mods such as Sodium, Iris, VulkanMod, Nvidium, Canvas and OptiFine/OptiFabric
  - requires the official Minecraft Launcher to be closed while Fabric/profile files are modified
  - patches the Mojang launcher Fabric profile with `-Xss16m` and `--enable-native-access=ALL-UNNAMED`, and surfaces a warning instead of silently hiding profile-patch failures
  - warns when a third-party launcher manages JVM arguments separately
  - relies on Caustica RTX's implemented Vulkan/NVIDIA path for path tracing, DLSS Ray Reconstruction, Frame Generation/MFG and Reflex
- **Restore original** reverses the one-click changes:
  - removes manager-installed Caustica/Fabric API files
  - restores `options.txt` and the previous launcher profile
  - restores renderer mods moved into the backup
  - restores previous Caustica config/native data when it existed
  - removes Fabric version directories only when the one-click installer created them
- Local DLSS/Streamline ZIP and `nvngx_dlssnr.dll` staging remain available as advanced/manual tools
- Minecraft resource documentation under `resources/minecraft/`

The project Caustica RTX build implements path tracing, DLSS Ray Reconstruction, Frame Generation/MFG and Reflex, and now contains a **capability-gated DLSS Neural Rendering / 3D-Guided Neural Rendering integration** in the renderer. The public build keeps a safe fallback when the feature-specific NVIDIA DLSS-NR SDK/runtime is unavailable; it does not fake Neural Rendering by merely copying DLLs. Real DLSS-NR execution still requires an NVIDIA-authorized compatible DLSS-NR/NGX SDK/runtime and supporting GPU/driver. Proprietary NVIDIA SDK binaries are not committed or redistributed.

The same build adds **RTX Performance Mode** and a native **ScandiShader RTX Look** converted into Caustica's display pipeline, avoiding Iris/Sodium renderer replacement while preserving the path-traced/DLSS pipeline. NVIDIA documents Ray Reconstruction as part of the DLSS reconstruction path; when RR is active it handles reconstruction using the selected quality/performance mode.

**Sodium/Iris are intentionally not installed in the Caustica RTX profile.** Sodium and Iris modify/replace major portions of Minecraft's renderer/shader pipeline, while Caustica owns the Vulkan/path-traced world renderer. The manager keeps them in the conflict detector instead of presenting an unsupported "DLSS + Iris shaderpack" combination. For RTX visuals, use Caustica plus a LabPBR resource pack such as SPBR; for non-renderer performance gains, use the optional performance pack above. Caustica remains experimental, so final compatibility is still verified at runtime.

## Default Neural Rendering preset

For the current upstream INI the manager applies conservative defaults under `[DlssNr]`:

```ini
Enabled=true
RunBeforeSR=true
Passes=1
WorkingScale=1.0
Style=1
```

The manager updates supported keys in the upstream configuration and inserts missing supported keys when the target section exists. Neural Rendering is refused if the upstream package does not provide the required `[DlssNr]` section.

## Installation

1. Download the Windows x64 artifact/release.
2. Close the target game and its launcher.
3. Run `DlssNrManager.exe`.
4. Select a detected game or choose its executable folder manually.
5. On RTX 50, use the validated manager-owned NVIDIA runtime package, or manually select your own trusted `nvngx_dlssnr.dll` where supported. On RTX 20/30/40, Neural Rendering is disabled automatically and no DLSSNR runtime is required.
6. Review the runtime validation and recommended proxy.
7. Run **Diagnose game** when compatibility is not upstream-validated.
8. Optionally keep **Add missing NVIDIA Streamline/DLSS resources** enabled. The manager stages only validated manager-owned resources supported by the detected RTX generation.
9. Click **Install**.

The manager downloads the validated OptiScaler package from DLSS NR Manager releases, creates a backup, validates runtime components and then installs into the selected executable directory.

## PC Update Center

**Scan PC updates** performs read-only discovery across WinGet, Windows Update, connected-device driver metadata, firmware metadata and NVIDIA driver state.

**WinGet update all** is the only software-install action in this panel. It requires explicit confirmation and runs:

```powershell
winget upgrade --all --include-unknown --include-pinned
```

Driver, Windows Update, BIOS and firmware entries continue to open official vendor/Microsoft destinations instead of being installed automatically.

## PC Cleanup

Use **Analyze caches** first to calculate file counts and reclaimable storage. Select or deselect categories, then use **Clean selected**.

Shader caches are disposable performance caches. Games and GPU drivers rebuild them after cleanup, so the first launch after cleaning can temporarily compile shaders again.

## Loader compatibility

Games using ReShade, script extenders or other DLL loaders may already own common proxy names. DLSS NR Manager does not silently replace an unknown proxy DLL.

For Cyberpunk 2077, `dbghelp.dll` remains the upstream-validated recommendation with `dxgi.dll` as a fallback when appropriate.

## Runtime and third-party licensing

DLSS NR Manager does not store NVIDIA proprietary binaries in this source repository.

When automatic runtime provisioning is enabled, the application downloads validated manager-owned runtime packages from DLSS NR Manager releases. Streamline bootstrap provenance remains tied to the pinned official NVIDIA package, while restricted NVIDIA SDK/runtime inputs are kept out of the source tree unless redistribution is explicitly permitted. Users may instead supply a trusted local runtime where supported.

OptiScaler, NVIDIA Streamline/DLSS, Fabric, Caustica RTX, Real-ESRGAN, FFmpeg, video2dlssnr, ReShade and other third-party components retain their respective licenses and ownership.

DLSS NR Manager itself is MIT licensed.

## Vendored / integrated components

The monorepo contains pinned source snapshots or controlled bootstrap workflows for:

- `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`
- `NVIDIA-RTX/Streamline`
- `FabricMC/fabric-installer`
- `FabricMC/fabric-api`
- `Caustica-RTX/` (vendored project fork; based on AriesAlex/Caustica-RTX)
- Minecraft components: Fabric Installer/API, Lithium, FerriteCore, Krypton, BadOptimizations, Dynamic FPS and SPBR; C2ME source is mirrored but excluded from the stable 26.2 runtime bundle
- `xinntao/Real-ESRGAN-ncnn-vulkan`

## Limitations

- Automatic game compatibility remains evidence-based; non-validated titles require in-game verification.
- Launcher metadata formats and permissions can change.
- Windows Update may lag behind NVIDIA's newest Game Ready/Studio driver, so the official NVIDIA page remains the authoritative vendor check.
- Some temporary/cache files are locked while Windows, games or drivers are running and will be skipped.
- Cleaning shader caches can make the next game launch spend time rebuilding shaders.
- Automatic runtime provisioning depends on validated manager-owned release assets being present; bootstrap/update failures fail closed instead of falling back to arbitrary upstream downloads.
- The manager does not silently chain arbitrary third-party proxy loaders.
- **Do not use the general game injection/install flow in multiplayer or anti-cheat-protected games unless the game developer explicitly allows it.** Proxy DLL injection can be treated as tampering and may lead to account sanctions or bans. The manager blocks installation when known anti-cheat files are detected, but absence of a local signal is not proof that online use is safe.

## Build

```powershell
dotnet restore
dotnet restore tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj
dotnet test tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj -c Release
dotnet publish DlssNrManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

GitHub Actions publishes `DlssNrManager.exe` as a build artifact. Tags matching `v*` can create a GitHub Release with the executable and ZIP. The release workflow requires the Git tag to match the project version exactly; for example, project version `1.4.2` must use tag `v1.4.2`.
