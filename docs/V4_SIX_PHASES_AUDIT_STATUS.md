# v4 source-level component and six-phase audit status

> Reconciled from the unique document in historical draft PR #162 (GPT B).
> Reference: `main` at `b2500c4280a84ce2ee6fc1cd44df31049ff64056` on 2026-10-09.
> This is **source evidence, not a v4 release certificate or a live CI dashboard**. Refresh GitHub before further changes. GPT A owns experimental Caustica/six-phase implementation; this file tracks only Manager integration boundaries.

The current application source version is **3.2.0** in `DlssNrManager.csproj`. No v4.0 production release has been authorized.

## Status vocabulary

- **active**: a Manager feature or service is present in `main`, without a guarantee of end-to-end Windows/RTX acceptance.
- **integrated-partial**: code/UI and some management paths are present, but execution, update coverage, compatibility or testing remain incomplete.
- **integrated-manual-only**: a user-provided restricted model may be imported after required license/access steps; no automatic weight redistribution.
- Old `feature-branch-75`, `feature-branch-77`, and `planned/feature-branch` status labels are obsolete for already integrated source code. Corresponding JSON policy statuses were reconciled in PR #254.

## Current Manager source and release limits

| Area | Present in `main` | Still unverified or incomplete |
| --- | --- | --- |
| VSR-HDR / VLC | `Services/VlcRuntimeService.cs`, `DownloadCenterService.cs`, VSR-HDR menu and pinned manager-owned VLC runtime | Physical Windows playback/performance and distribution obligations; **PowerShell 5.1 ZIP fix PR #160 was merged**, not pending |
| AI Studio | `LocalAiStudioService`, `AiStudioPackageService`, model catalog, job queue and manual-license import | Complete ComfyUI/Diffusers model inference/output workflow not certified for v4; GPT C v4.5 runtime work is separate |
| Downloads | `DownloadCenterService`, central install/redownload UI; optional updates for Media and eligible manager-owned AI Studio packages; official upstream read-only discovery #219 and stable tag normalization #246 | Other eligible component version-aware offers and complete failure/rollback/offline/FR-EN testing (issue #111); no automatic upstream promotion |
| Real-ESRGAN | Local upscale service and manager-owned producer; `tools/package-approved-openmp.ps1` requires Visual Studio REDIST path and signed provenance | Legal license attestation and production packaging/runtime/performance tests (#79, #95); **never copy System32 OpenMP DLLs** |
| OptiScaler | Vendored source, deterministic nested ZIP packaging and per-file hash diagnostics introduced by #186 | Repeatable **native MSBuild** binary output and canonical release comparison (#161); no same-name asset overwrite |
| Managed processes / recovery | Job Object tracking, startup owned-backup recovery, guarded AI Studio model transactions and forced-termination surrogate test | Full on-device abrupt-exit and old/manual-backup recovery acceptance (#95); Windows acceptance probe PID identity fix is tracked separately in PR #259 |
| Runtime publication | Append-only Streamline #157 and VLC/Temurin/Real-ESRGAN #159 producers, plus reviewed immutable consumer rules | Audit every version-pinned consumer and licensed artifact before promoting a runtime; historical issue #123 is **closed**, not an open release gate |

## Manager evidence across the six-phase roadmap

1. **Dependency provenance:** `third_party/DEPENDENCIES.lock.json`, upstream catalog and source notices identify locked inputs. Restrict NVIDIA proprietary SDK to authorized local/build contexts.
2. **Versions and packaging:** current source version 3.2.0; Streamline locked at v2.14.1; OptiScaler reproducibility issue #161 remains open. A passing deterministic ZIP test does not certify native DLL byte reproducibility.
3. **GPU integration:** `GpuDetectionService`, `NvidiaSmiLocator` and `HardwareProfileService` are present. Native NGX/Streamline/Caustica functionality is not certified merely because its wiring compiles.
4. **Hardware policy:** AUTO and RTX 5060 Ti 16 GB / Ryzen 7 9700X profiles, Normal/Compatible policy, and general RTX generation gating are implemented at source level. Driver/GPU/RTX physical trials remain necessary.
5. **UX and updates:** production WPF menu includes 14 pages, including Downloads, VSR-HDR, Minecraft RTX, AI Studio, AI-origin detection and Hardware & Profiles. Full FR/EN runtime visual coverage, optional update safety (#111), cancellation and recovery (#95) need final acceptance.
6. **Release pipeline:** package writers are append-only and CodeQL/Build run under protected main rules. Verify signed exact-head CI, pinned consumers, immutable digests, licensing and Windows RTX results before promoting v4.0.

## Outstanding final validation

Issues [#79](https://github.com/grg914/dlss-nr-manager/issues/79), [#95](https://github.com/grg914/dlss-nr-manager/issues/95), [#111](https://github.com/grg914/dlss-nr-manager/issues/111), [#124](https://github.com/grg914/dlss-nr-manager/issues/124) and [#161](https://github.com/grg914/dlss-nr-manager/issues/161) retain independent acceptance criteria. Follow `docs/RELEASE_CHECKLIST.md` for production gating and `AI_PROJECT_COORDINATION.md` to avoid conflicts with GPT A/GPT C. No proprietary NVIDIA runtime, restricted model weights or Microsoft redistributables are authorized by this note.
