# NVIDIA Driver Health — v4.5 technical design (proposal only)

**Owner:** GPT A | **Date:** 2026-10-09 | **Status:** DESIGN / NO RUNTIME IMPLEMENTATION / NO RELEASE APPROVAL  
**Target:** `Hardware & Profiles` > `NVIDIA Driver Health` > `Diagnose & Fix` (label means diagnosis plus guided options, *not* an automatic driver repair).  
**Source inspected:** manager `main=c29849ab1cc9d008283da28c3576e1b1ab2ce253`; check live main before implementation.

## 1. Problem statement and evidence (do not overclaim)

NVIDIA's official GeForce Game Ready 617.42 feedback thread (release 2026-10-06) explicitly lists **open issue [6007998]**: "Prefer Maximum Performance" power-management mode **may not be applied correctly**. This is a vendor-recognized issue, **not** a documented universal one-click remedy and **not** proof that an RTX 5060 Ti desktop or every machine is affected.

Other low-FPS, stutter and `NVDisplay.Container.exe` high-CPU reports may reflect *different* causes. Treat forum experiences as unverified anecdotes; do not infer a vendor-confirmed root cause from process name, driver number or an isolated utilization sample. NVIDIA requests comparable 30-second GPUView traces on affected and last-known-good drivers for some performance regressions.

Authoritative starting points:
- NVIDIA 617.42 driver page (release 2026-10-06): https://www.nvidia.com/en-us/geforce/drivers/details/280103/
- NVIDIA-staff 617.42 feedback thread/open issue [6007998]: https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/591391/geforce-grd-61742-feedback-thread-released-10626/
- NVIDIA's feedback thread links its official low-FPS/stutter GPUView reporting guidance. Link users there; do not silently collect ETW/GPUView traces.

**Offline condition:** The app must remain usable without Internet; no background NVIDIA issue lookups, auto-update discovery, telemetry uploads or new external binaries. Static issue-catalog metadata must carry a reviewed source URL and verified scope/date. A missing catalogue entry is "not listed locally", NOT "no driver issue".

## 2. Boundaries / safety contract

1. **Read-only by default and in P0.** No NVIDIA Control Panel / NVIDIA App or per-game driver-profile writes; no NVAPI DRS write calls; no GPU clocks, voltage, fan, power or WDDM tweaks; no `nvidia-smi -pm/-pl/-lgc`; no OS registry, driver service or task changes.
2. No automatic NVIDIA driver install, rollback, DDU, `pnputil` removal, Windows Update driver suppression, NVIDIA Container restart/termination, DISM/SFC, elevation, cache deletion, or hidden app restart.
3. No privileged NVIDIA SDK, third-party injector, standalone benchmark, external daemon, polling timer or package downloads. No connection required, no attempts to check new releases on launch.
4. User-initiated only. Explicit, revocable consent for any later optional action; destructive suggestions must remain *manual external guidance*. Reuse existing `PcCleanupService` only for separately confirmed cache cleanup, with "initial stutters/shader recompilation" warning.
5. Never claim **"driver fixed"**. Distinguish `Known issue listed`, `Anomaly observed, cause unknown`, `No anomaly in short sample`, `Insufficient data`, and `Unsupported`.
6. Never change `GpuAdaptiveProfileService` AUTO/Manual/Compatible, game DLSS/NR staging, `GameNvidiaSelectionPolicy.Effective` or hardware preferences as a side effect. No experimental Caustica/AI Studio integration.

## 3. Reuse existing code (avoid duplicates)

| Existing component | Reuse |
| --- | --- |
| `HardwareProfileService.Detect()` / `HardwareSnapshot` | Detected GPU, driver string, VRAM/CPU, snapshot identity; probe already read-only |
| `NvidiaSmiLocator.FindInstalled()` | Execute vendor `nvidia-smi.exe` only from trusted system/Program Files locations; NEVER search PATH or current directory |
| `GpuDetectionService` | Non-invasive RTX detection; account for multiple identical GPUs and uncertain adapter association |
| `ExternalProcessTracker` | Bounded owned process lifetime / process exit; do not kill NVIDIA-owned processes |
| `DiagnosticService` / `AppLogger` | Existing diagnostic conventions and local export/privacy rules; keep driver health report separate from game injection reports |
| `PcCleanupService` | Optional **existing** cache-clean action only, not included in P0 scan |
| `MainWindow.xaml` / `UiLocalizationService` | Existing Hardware & Profiles screen, reversible FR/EN labels/tooltips |

Do not introduce a second GPU detector, download center, driver updater, process lifecycle manager or global settings editor.

## 4. Proposed minimal architecture (P0)

- `Services/NvidiaDriverHealthService.cs` (new): on-demand read-only `RunAsync(CancellationToken)`. Separates probe failures from health findings; returns structured status and source timestamps. Run asynchronously off the WPF UI thread; disable reentry while scanning, cancel on window closing, no startup polling.
- `Services/NvidiaDriverIssueCatalog.cs` (new, internal/pure): immutable source-backed rules, initially the vendor-recognized 617.42/6007998 **catalog notice**. Use exact reviewed driver version and avoid claiming other versions are unaffected. A notice NEVER proves the observed performance problem.
- `NvidiaDriverHealthSnapshot` (record): observed GPU identifier/name, detected driver, observed timestamp, optional utilization (%), VRAM (MiB), clock (MHz), optional power (W), optional temperatures, `NVDisplay.Container` sampled CPU percentage, independent per-field availability, notices, diagnostic warnings. GPU stats/CPU samples are *observations*, not a game-FPS benchmark.
- `NvidiaDriverHealthAssessment` (pure function): map available observation + catalog to non-alarmist status and deterministic recommended **manual** next steps. Do not equate low clocks at idle to a defect.
- WPF: one `Diagnose & Fix` button near existing `Refresh hardware detection` in the Hardware tab; results text with `Read-only diagnosis`, driver/issue notice, observed metrics (or "unavailable"), and `View official NVIDIA guidance` / `Export report`. Labels reversible FR/EN. Keep a single control area even after merging #353.
- Use existing `HardwareSnapshot` where possible; do NOT duplicate persisted preferences or launch background networking.

### Probe details

1. Validate Windows and RTX support; if unsupported or unknown, return `Unsupported/Insufficient data`, not an error crash.
2. Use trusted `NvidiaSmiLocator`; `nvidia-smi --query-gpu=...` static allowlisted args with `--format=csv,noheader,nounits`, `UseShellExecute=false`, redirected bounded output, finite timeout (e.g. 3 seconds/probe) and cancellation. Query minimal guaranteed identity/driver separately from optional utilization/clocks/VRAM/power/temperature; optional field failure must not discard successful identity. Never pass user strings to the executable.
3. **Multi-GPU:** verify selected device mapping before reporting per-device measurements. Matching only the display name is ambiguous; if identifier/selection cannot be resolved, report "adapter ambiguous" rather than assign metrics to the wrong GPU.
4. Optionally sample `NVDisplay.Container.exe` CPU over two process-time readings separated by 3–5 seconds, with CPU usage normalized to total logical processors, PID continuity and process-exit handling. No magic "bug" threshold from one sample, never restart/kill the service. If process info is unavailable, display "unavailable".
5. Avoid sampling inside games and claiming FPS / 1% lows unless the user supplies a controlled before/after benchmark; **full integrated benchmark explicitly excluded from v4 scope**. No GPUView ETW capture in P0.
6. Process output, GPU metrics and vendor issue notices must be independently failure-tolerant. `nvidia-smi` not installed or blocked => graceful read-only fallback with limited hardware snapshot.
7. Export only on user action. Restrict fields to diagnostics, driver, GPU and observations; no username, full filesystem paths, command lines, game secrets, serials, tokens or private logs; redact as needed; do not upload.

## 5. UX contract / guided actions

| Finding | UI wording | Permitted action |
| --- | --- | --- |
| Driver 617.42 | "NVIDIA lists an open power-mode issue [6007998]; applicability to this PC is unconfirmed." | Open the official NVIDIA issue/source in the browser, opt-in |
| Sustained NVIDIA Container CPU observed | "CPU activity observed; cause unknown." | Present measured value, local report and official NVIDIA support guidance |
| Clock/power/utilization low at *idle* | "Idle measurements cannot diagnose in-game performance." | Explain comparable game testing only; no forced maximum clocks |
| NVIDIA diagnostics unavailable | "Cannot verify NVIDIA telemetry." | Allow refresh/manual troubleshooting; no fabricated results |
| Driver regression suspected by user | "Compare with last known good version using same scene/settings." | Official vendor instructions, user-driven installation/rollback only |
| User explicitly wants shader-cache maintenance | Warning: "Shader recompilation may temporarily increase stutter." | Navigate to existing PC Cleanup; **never automatically delete** |

No one-click "Fix 617.42", "FPS boost", guaranteed compatibility badge, per-game or global NVIDIA profile edit, Windows power-plan edit, forced NVAPI setting, hidden subprocess or shader-cache reset.

## 6. Execution plan / ownership

**Step 0 — this documentation PR (GPT A):** verify source/branches/journals, record design and unresolved vendor claims, coordinate through shared GitHub. No runtime modification and no release impact.

**Step 1 — independent P0 code PR (GPT A, after B/C acknowledge overlap):**
- Add pure `NvidiaDriverIssueCatalog` + `NvidiaDriverHealthAssessment`, then a bounded `NvidiaDriverHealthService` reading actual Windows/NVIDIA state.
- xUnit: version parsing (617.42 vs close versions), no "known issue" false certainty, missing nv-smi, timeout, cancellation, malformed CSV, comma-decimal/locale, multi-GPU ambiguity, exit/permission failures, bounded output, unknown optional metrics, NVIDIA Container PID changes/normalization, privacy report.
- Build one optional WPF button + read-only results. FR/EN tooltip/translations and existing `LocalizedOptionTooltipTests`; avoid touching existing GPU mode selections, install, preset, or stage code.
- P0 is fully offline, starts **only on click**, no admin and no new runtime assets. No write operations beyond explicit export.

**Step 2 — optional guided actions PR (GPT A; requires fresh user approval):**
- Surface official NVIDIA troubleshooting links, manual rollback guidance and `PcCleanupService` navigation only. Each destructive action remains in its existing separately confirmed workflow; do not bundle all actions under "Fix".
- Add persistence only if a real demonstrated use case requires it; default no background monitors, no saved raw telemetry.

**Step 3 — acceptance/release gates (GPT B owns stable release, GPT A owns feature validation):**
- Exact final SHA: Windows WPF Build/xUnit + Analyze C# + CodeQL, signed review if promoting to protected main, no unresolved review threads; re-evaluate main/three shared journals before merge; serialize A/B/C merges, compare ancestry and relevant Git blobs afterward.
- Manual Windows 11 RTX 5060 Ti 16GB (or other real GPU), driver 617.42 where available: supported/unsupported probes, no-admin/no-Internet behavior, idle-vs-load distinction, multi-GPU fallback, non-invasiveness and unchanged NVIDIA Control Panel/App profiles, no network/background process, no startup regression and teardown.
- Fail-closed/offline + FR/EN. No hardware validation is claimed by code or CI alone. No v4.0 release gating on this v4.5 proposal.

## 7. GPT A / B / C coordination and risks

- **GPT B:** Stable v4.0 installer/updater/rollback/release ownership; current issue #95 and #79 unresolved. At design checkpoint, source main `c29849ab...` and GPT B B02 PR #377 are active. This proposal must not change these lanes.
- **GPT A:** Existing GPU adaptive profiles [#353](https://github.com/grg914/dlss-nr-manager/pull/353) are DRAFT/unmerged and overlap the Hardware WPF screen and `UiLocalizationService`. Integrate new button only after reconciling #353 against current B-owned `GameNvidiaSelectionPolicy` or place on an isolated code branch with a single later reviewed UI integration. **Do not rebase or merge #353 implicitly.**
- **GPT C:** AI Studio/ComfyUI/FLUX and v4.6 experimental stacks remain untouched; do not reuse their queue/process worker or risk shared Download Center modifications.
- **Shared journals:** historical checkpoints are not locks; read latest live `AI_PROJECT_COORDINATION.md`, `AI_PROJECT_PROGRESS.txt`, `AI_PROJECT_PROGRESS2.txt`, active PRs and branch heads before implementation. Maintain factual append-only checkpoints through own reviewed PR; do not blindly merge old journal blobs from #339/#345.
- **Dependency/license:** no new fork, NVIDIA SDK, NVAPI binary, Python, CUDA package, model, OS service or redistributable required for P0. Vendor links are documentation only. A future write-capable NVIDIA API proposal requires a separate security/license review and explicit user authorization.

## 8. Explicit definition of done

Design is done when this document has been reviewed against current repo and linked to GPT B/C handoff. **Feature is NOT implemented.** P0 implementation is done only after read-only click-driven UI/service, exact-head CI passing, FR/EN tests, offline/no-admin/unknown-driver tests and real Windows acceptance. Any observed power/performance issue remains NVIDIA/vendor-owned unless a separately validated reversible mitigation exists.

External references were checked 2026-10-09. Revalidate NVIDIA's known-issue status and the current latest driver on every future user-facing release/issue-label update; no evergreen automatic "latest driver" claim.

## 9. Official first-party source ledger — design follow-up (2026-10-09)

For verified official NVIDIA and Microsoft evidence, precise CLI query prototypes, Windows WDDM limitations, CPU delta normalization, device identity ambiguity, safe UX wording, threat model and CI acceptance cases, see [NVIDIA_DRIVER_HEALTH_V45_OFFICIAL_REFERENCES.md](NVIDIA_DRIVER_HEALTH_V45_OFFICIAL_REFERENCES.md).

Source-confirmed points: NVIDIA lists [6007998] for 617.42 as *may not apply maximum-performance mode correctly*, not proof of a particular desktop/GPU failure or supported repair; nvidia-smi reports sampled counters with unsupported values on some configurations; Microsoft CPU utilization requires two stable process-time observations; NVIDIA App already provides optional in-game FPS and 1% low statistics. **Do not infer actual game FPS, driver causality or persistent load from a short idle snapshot.**

No third-party framework, native NVIDIA SDK, auto-rollback, DRS mutation, cache deletion, background polling or new benchmark is approved. This is documentation only. Last source recheck: 2026-10-09. Main inspected for document update: 0d9c05556528b633a52619932b3fa36da8e122fc.
