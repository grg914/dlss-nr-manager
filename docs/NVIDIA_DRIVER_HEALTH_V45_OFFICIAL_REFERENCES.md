# NVIDIA Driver Health v4.5 — official references and verified design decisions

**Source review:** 2026-10-09. **Owner:** GPT A. **State:** documentation/research only; no feature or driver fix implemented.

Companion to [NVIDIA_DRIVER_HEALTH_V45_DESIGN.md](NVIDIA_DRIVER_HEALTH_V45_DESIGN.md). Only first-party NVIDIA and Microsoft publications substantiate these decisions. User forum replies and third-party workarounds are not vendor confirmations. This is a dated **offline** snapshot; the application must not check these links at startup.

## 1. First-party evidence matrix

| ID | Official reference | Evidence | Decision |
|---|---|---|---|
| NV-1 | [NVIDIA GeForce Game Ready Driver 617.42, Windows 11](https://www.nvidia.com/en-us/geforce/drivers/details/280103/) | Release entry documents version 617.42 and WHQL/Windows 11. | Parse driver versions as numeric dotted components; never compare as floating-point or assume it is universally latest. |
| NV-2 | [NVIDIA 617.42 feedback thread, staff opening message](https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/591391/geforce-grd-61742-feedback-thread-released-10626/) | Official open issue [6007998]: "Prefer Maximum Performance" power management mode may not be applied correctly. | Banner states **vendor issue listed for this release**, not diagnosed, confirmed on a particular desktop, or repaired. |
| NV-3 | [NVIDIA nvidia-smi manual](https://docs.nvidia.com/deploy/nvidia-smi/index.html) | Supports read-only --query-gpu and --format=csv,noheader,nounits; utilization refers to a recent sample window. Windows WDDM may prevent per-process GPU memory reporting. Other command flags can mutate GPU state. | Strictly allowlist read-only queries and allow missing/unsupported/N/A values. No reset, clocks, power or mode commands. |
| NV-3b | [NVIDIA Control Panel: Manage 3D Settings](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/3D%20Settings/Manage_3D_Settings_%28reference%29.htm) | Power management mode describes GPU performance behavior for most 3D DirectX/OpenGL applications; it does not say low idle clocks imply a fault. Global and program settings have different scopes. | No automatic edit of global or game-specific setting; do not infer issue 6007998 from idle clock or a static choice. |
| NV-4 | [NVIDIA NVAPI DRS reference](https://docs.nvidia.com/nvapi/group__drsapi.html) | Driver Settings APIs include state-changing setting/profile/save/reset operations. | No NVAPI dependency, global or per-game driver-profile writes, restore defaults or silent change. |
| NV-5 | [NVIDIA App feature announcement](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/) and [NVIDIA App homepage](https://www.nvidia.com/en-us/software/nvidia-app/) | NVIDIA App already offers optional performance statistics overlays including FPS and 1% lows. | Refer to the existing overlay for manual controlled in-game comparisons; do not implement a second in-game benchmark. |
| NV-6 | [Official NVIDIA driver FAQ (French)](https://www.nvidia.com/fr-fr/drivers/drivers-faq/) | Documents version verification, finding older drivers and manual earlier-driver reinstall. | Link to official manual guidance only; no driver reinstall/rollback/elevation from the manager. |
| MS-1 | [Microsoft Process.TotalProcessorTime](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime) | Total per-process CPU time combines user and privileged time. | Require stable process identity and CPU time delta over measured elapsed time. |
| MS-2 | [Microsoft Collecting Performance Data](https://learn.microsoft.com/en-us/windows/win32/perfctrs/collecting-performance-data) | Raw process CPU percentage can exceed 100% on multicore systems. Name-only counter instances can change after process termination; rates need two samples. | Normalize by logical CPU count and verify stable PID plus process creation time; no single snapshot "high CPU" verdict. |
| MS-3 | [Microsoft About Performance Counters](https://learn.microsoft.com/en-us/windows/win32/perfctrs/about-performance-counters) | Counters are diagnostic, not designed for high-frequency application profiling. | One short user-click scan; no persistent timer, daemon or background sampling. |
| MS-4 | [Microsoft PIX timing captures](https://learn.microsoft.com/en-us/windows/win32/direct3dtools/pix/articles/timing-captures/pix-timing-captures) | Workload-aware graphics timing and presentation counters require specialized capture. | No claim that nvidia-smi utilization proves FPS, 1% lows or GPU-limited game conditions. |

## 2. Claims NOT supported by the sources

- No proof that every RTX 5060 Ti desktop or RTX 50 card is affected by issue 6007998.
- No proof that NVIDIA Container CPU activity, low FPS or a low idle GPU clock are caused by 617.42 or this issue.
- No vendor-backed automatic one-click repair for the internal issue. Retoggling "Prefer Maximum Performance" is not a demonstrated fix.
- No guarantee issue 6007998 is fixed or still present in other driver versions. The offline notice is version-scoped.
- No guarantee all GPU telemetry exists on Windows WDDM. Missing data must not be presented as zero.
- No source supports running DDU, forcibly changing global/per-game profiles or stopping NVIDIA services as a safe default fix.

## 3. Read-only probe implementation contract

Use existing HardwareProfileService, NvidiaSmiLocator, ExternalProcessTracker and DiagnosticService infrastructure, without adding NVAPI/NVML SDK binaries or another download path.

### NVIDIA command allowlist (candidate — verify on real test hardware)

~~~text
nvidia-smi --query-gpu=index,uuid,name,driver_version --format=csv,noheader,nounits
nvidia-smi --query-gpu=index,uuid,utilization.gpu,memory.used,clocks.gr,temperature.gpu,power.draw --format=csv,noheader,nounits
~~~

- Execute only the trusted-location nvidia-smi executable with literal allowlisted argument strings/ArgumentList. No PATH search, shell expansion or user arguments. **A trusted location is not itself a cryptographic signature verification.**
- Identity data and optional metrics should be independent stages: one unsupported field should not erase all identity data. Prefer a bounded optional-field fallback over unbounded retries.
- Proposed limits: 3 seconds per child, around 10 seconds wall-clock per scan, 32 KiB maximum stdout; robust async stdout/stderr draining, cancellation and process disposal. Child cancellation applies only to the helper started by the manager, **not NVIDIA Container or driver services**.
- Parse real CSV quoting, whitespace, N/A, missing values, invalid numerics and multiple rows. Use invariant culture for parsed numeric fields. Driver version 617.42 is dotted version, not a decimal quantity.
- Use GPU UUID/PCI bus ID when available for stable attribution. Existing GPU detector ranks model names rather than identifying one uniquely; in dual-identical-GPU cases report ambiguity instead of misattribution.
- Each metric has its own measured/unavailable/unsupported/timeout state. Timestamp measured data. A single utilization sample is not a sustained-load or in-game FPS benchmark.

### NVIDIA Container CPU observation (optional P0)

- A process named NVDisplay.Container.exe does not by itself prove the process is NVIDIA-signed or the cause of an issue. Treat identity as unverified unless validated.
- Sample TotalProcessorTime twice approximately 3–5 seconds apart with stable PID and StartTime. Machine-normalized percent = 100 × change in process CPU seconds / (elapsed seconds × logical processor count).
- Reject zero/negative elapsed time, PID reused, exited process, failed access and invalid deltas. Aggregate distinct stable processes without duplicating names.
- A brief sample may describe observed CPU activity only; never assert a persistent defect or assign it to the NVIDIA driver from a threshold.

## 4. UX and action boundaries

The Hardware & Profiles section gains a single opt-in **Diagnose & Fix** entry, with subtitle **Read-only diagnosis • Guided manual actions**. Outcome labels:

- Vendor issue listed (617.42 / #6007998) — scope and source date shown; no confirmation on user's device.
- Observation available, no causal conclusion.
- Data insufficient or unsupported.
- Observed elevated activity, origin unknown.

Buttons: **Diagnose** (read only), **View NVIDIA official guidance** (open URL on click), **Export local report** (on explicit choice), and optionally **Go to PC Cleanup** (navigation to existing separately confirmed screen). No driver/profile/system/cache writes, telemetry upload, automatic internet check, Windows elevation, process restart or deletion.

NVIDIA's official driver thread provides instructions to compare GPUView traces with the same game scene/settings on current and last-known-good drivers. It is **manual troubleshooting**, outside the manager's automatic telemetry capture. NVIDIA App's in-game overlay is the supported reference for comparable FPS and 1% low samples; preserve the current excluded scope of a full built-in benchmark.

## 5. Implementation gate and tests

1. In a new isolated v4.5 code PR, pure issue catalogue/assessment and on-click read-only probe; no WPF modifications until GPT A profile PR #353 overlap is reconciled against GPT B's GameNvidiaSelectionPolicy.
2. Unit test: exact driver match vs neighboring version; unknown/offline; N/A/quoted/broken CSV; unsupported optional query; process timeout/cancel/oversized output; dual-GPU ambiguity; CPU PID swap/restart/multicore normalization; no generated commands outside explicit allowlist.
3. UI tests: FR/EN reversible ToolTip translations (LocalizedOptionTooltipTests), no duplicate NVIDIA controls, no install/preset/staging changes or automatic startup activity.
4. Windows 11 RTX 5060 Ti 16 GB physical offline/no-admin acceptance, and a non-NVIDIA device negative test; verify no change in NVIDIA Control Panel/App, clocks, services, caches, system settings or installed driver.
5. Require exact signed final SHA Build/xUnit, Analyze C#, CodeQL, review and protected merge separately. GPT B owns v4.0 safety/release; GPT C owns AI Studio; neither lane altered by this design.

## 6. Review constraints

These sources were checked 2026-10-09. This table is an evidence ledger, not a substitute for future release-note review or real on-device acceptance. Before adding a new alleged NVIDIA issue or automated mitigation, verify a fresh NVIDIA official source, exact affected versions/hardware, user authorization, rollback behavior and test coverage. Default to "insufficient evidence" rather than a fabricated diagnosis.
