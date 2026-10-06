<p align="center">
  <img src="assets/branding/logo.png" alt="DLSS NR Manager logo" width="128" />
</p>

# DLSS NR Manager

<p align="center">
  <img src="assets/branding/hero.png" alt="DLSS NR Manager banner" width="100%" />
</p>

A native Windows manager for installing, diagnosing and maintaining the experimental **OptiScaler DLSS Neural Rendering (DLSSNR)** stack, with PC update, cleanup, media AI and Minecraft RTX utilities.

Current application version: **v2.0.0**.

> Supports automatic candidate detection across **Steam, Epic, GOG, itch.io, Ubisoft Connect, EA App, Xbox App and Battle.net**, with generation-aware support for NVIDIA GeForce RTX 20/30/40/50 GPUs, official NVIDIA Streamline runtime provisioning, PC software/driver update checks and safe Windows/NVIDIA cache cleanup.

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

## Download

Use the latest GitHub Release for the Windows x64 single-file build. Each v2 release publishes:

- `DlssNrManager.exe`
- `DlssNrManager-win-x64.zip`
- `SHA256SUMS.txt` for independent SHA-256 verification

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
- Stable/prerelease selection from the upstream OptiScaler-DLSSNR fork
- Full managed-install backup before game-directory changes
- Multiplayer/anti-cheat safety guard: prominent warning on every general game install and hard block when known anti-cheat files are detected
- Install, update, restore and uninstall flows
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

- **Check NVIDIA files** performs a read-only availability probe against official `NVIDIA/DLSS` and `NVIDIA-RTX/Streamline` GitHub sources before any runtime staging
- Optional automatic download of the latest official **NVIDIA-RTX/Streamline** GitHub release
- Looks for `nvngx_dlssnr.dll` in the official package when no local runtime is selected; if the current public Streamline release does not contain it, the manager reports that an NVIDIA-authorized DLSS-NR runtime must be supplied instead of pretending provisioning succeeded
- Validates x64 architecture and NVIDIA Authenticode publisher before use
- Existing known SHA-256 runtime fingerprints remain accepted
- Can add missing Streamline/DLSS runtime resources to the selected game:
  - `sl.interposer.dll`
  - `sl.common.dll`
  - `sl.dlss.dll` / `nvngx_dlss.dll`
  - `sl.dlss_d.dll` / `nvngx_dlssd.dll` for DLSS Ray Reconstruction when supported by the renderer
  - `sl.dlss_g.dll` / `nvngx_dlssg.dll`
  - `sl.reflex.dll` plus `NvLowLatencyVk.dll` when present for the Vulkan Reflex path
  - `sl.dlss_nr.dll` / `nvngx_dlssnr.dll` when present in the selected Streamline package
- Existing vendor DLLs in a game are not overwritten by the resource-completion step
- NVIDIA binaries are **not committed to this repository**

Validated compatibility hashes currently retained by the manager:

- RTX 50 DLSSNR SHA-256: `E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E`
- Legacy RTX 20/30/40 compatibility-runtime SHA-256: `E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A` (retained for older managed installs; general RTX 20/30/40 installs do **not** require a DLSS-NR runtime)

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
- Media engine downloads `video2dlssnr` and FFmpeg from upstream releases
- Native, 2× and 4K media output presets
- Default / Natural / Cinematic Neural Rendering styles
- Real-ESRGAN NCNN Vulkan AI Upscale
- AI Upscale 2× / 3× / 4×
- General, conservative, illustration/anime and anime-video/Minecraft models
- Optional TTA and tile-size controls
- Combined **Neural Rendering + AI Upscale** processing mode
- Video audio is preserved during processing
- **AI origin detection (beta)** runs locally using a two-model ONNX ensemble, three aspect-ratio-preserving spatial crops per image/frame and up to 32 samples across videos; it reports an ensemble AI score, per-model scores, disagreement, confidence, temporal consistency and known generator/provenance metadata signals
- AI-origin results are explicitly advisory: absence of a signal does not prove human origin, and the manager intentionally prefers `Uncertain` over declaring AI when its detectors disagree

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
  - resolves the latest stable Fabric Loader for Minecraft 26.2 from Fabric Meta (minimum supported: 0.19.3) and installs/updates it automatically on Mojang/Microsoft-style instances
  - requires launcher-managed Fabric to be installed from Prism/Modrinth/CurseForge/GDLauncher when those launchers own the instance metadata
  - downloads the latest stable Fabric API build for Minecraft 26.2 from Modrinth and verifies its SHA-512 hash
  - downloads only a tested `grg914/Caustica-RTX` prerelease produced from a green main-branch CI run and explicitly targeting Minecraft 26.2, preventing future 26.3/26.4 builds from being installed into the wrong instance
  - optionally installs a performance pack that avoids renderer replacement: **Lithium + FerriteCore + Krypton + C2ME + BadOptimizations + Dynamic FPS**; unavailable optional components are skipped without invalidating the core RTX installation
  - optionally installs **SPBR** as the compatible LabPBR material/resource pack for Caustica; if no compatible stable build is available, the core RTX installation continues
  - stages a locally supplied Caustica-adapted **ScandiTextureV1** resource pack (or an authorized matching release asset when available) and the optional legacy ScandiShader archive; the RTX renderer itself uses Caustica's native ScandiShader RTX Look rather than Iris
  - verifies Modrinth SHA-512 hashes before installing downloaded mods/resource packs
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
5. On RTX 50, leave **Automatically download the latest official NVIDIA Streamline DLSSNR runtime** enabled, or manually select your own `nvngx_dlssnr.dll`. On RTX 20/30/40, Neural Rendering is disabled automatically and no DLSSNR runtime is required.
6. Review the runtime validation and recommended proxy.
7. Run **Diagnose game** when compatibility is not upstream-validated.
8. Optionally keep **Add missing official NVIDIA Streamline/DLSS resources** enabled. The manager stages only the resources supported by the detected RTX generation.
9. Click **Install**.

The manager downloads OptiScaler from its upstream release, creates a backup, validates runtime components and then installs into the selected executable directory.

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

When automatic runtime provisioning is enabled, the application downloads the selected runtime resources directly from NVIDIA's official `NVIDIA-RTX/Streamline` GitHub release assets and validates the runtime before use. Users may instead supply a local runtime.

OptiScaler, NVIDIA Streamline/DLSS, Fabric, Caustica RTX, Real-ESRGAN, FFmpeg, video2dlssnr, ReShade and other third-party components retain their respective licenses and ownership.

DLSS NR Manager itself is MIT licensed.

## Upstream components

The project integrates or automates workflows around:

- `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`
- `NVIDIA-RTX/Streamline`
- `FabricMC/fabric-installer`
- `FabricMC/fabric-api`
- `grg914/Caustica-RTX` (project fork; based on AriesAlex/Caustica-RTX)
- Modrinth API projects: Lithium, FerriteCore, Krypton, C2ME, BadOptimizations, Dynamic FPS and SPBR
- `xinntao/Real-ESRGAN-ncnn-vulkan`

## Limitations

- Automatic game compatibility remains evidence-based; non-validated titles require in-game verification.
- Launcher metadata formats and permissions can change.
- Windows Update may lag behind NVIDIA's newest Game Ready/Studio driver, so the official NVIDIA page remains the authoritative vendor check.
- Some temporary/cache files are locked while Windows, games or drivers are running and will be skipped.
- Cleaning shader caches can make the next game launch spend time rebuilding shaders.
- Automatic runtime provisioning depends on the layout/content of NVIDIA's upstream Streamline release assets.
- The manager does not silently chain arbitrary third-party proxy loaders.
- **Do not use the general game injection/install flow in multiplayer or anti-cheat-protected games unless the game developer explicitly allows it.** Proxy DLL injection can be treated as tampering and may lead to account sanctions or bans. The manager blocks installation when known anti-cheat files are detected, but absence of a local signal is not proof that online use is safe.

## Build

```powershell
dotnet restore
dotnet publish DlssNrManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

GitHub Actions publishes `DlssNrManager.exe` as a build artifact. Tags matching `v*` can create a GitHub Release with the executable and ZIP. The release workflow requires the Git tag to match the project version exactly; for example, project version `1.4.2` must use tag `v1.4.2`.
