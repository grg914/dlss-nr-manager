# v4 component and feature status reconciliation (source-level)

This note describes **what is present in the production source**, not a hardware/runtime release certificate. Source version is `3.2.0`; v4 remains unreleased. Refer to `AI_PROJECT_PROGRESS2.txt` and issues #95, #111, #123, #161.

## Status vocabulary

- `active`: source feature/service is merged into `main`; **not** a claim of RTX hardware acceptance, end-to-end runtime completion or up-to-date dependency assets.
- `integrated-partial`: code/UX exists in `main`, but at least one required execution/update/compatibility flow remains incomplete or not independently demonstrated.
- `integrated-manual-only`: manual import and licensing workflow exists; restricted weights must not be automatically provisioned.
- Legacy `feature-branch-75`, `feature-branch-77` and `planned/feature-branch` strings were obsolete after related code was merged.

## Evidence and limits

| Area | Present in production source | Remaining limitation |
| --- | --- | --- |
| VSR-HDR / VLC | `Services/VlcRuntimeService.cs`, `DownloadCenterService.cs`, VSR-HDR WPF menu | Physical playback tests; CI VLC ZIP PowerShell 5.1 error handled by draft PR #160 pending complete checks |
| AI Studio | `LocalAiStudioService`, `AiStudioPackageService`, Download Center models, manual gated imports | ComfyUI/Diffusers end-to-end automatic execution not proven; license restrictions; retain outputs and manual model data |
| Downloads | `DownloadCenterService`, single install/redownload/update action in `MainWindow.xaml.cs` | Media and eligible AI Studio model optional updates implemented; additional components #111 incomplete, no extra duplicate Update controls |
| Real-ESRGAN | Source and manager-owned runtime paths | OpenMP legal provenance #79 and real Windows/NVIDIA tests #95 |
| OptiScaler | Vendored source, manager-owned package pipeline | Rebuild same-name archive mismatch #161, no overwrites; consumer promotion #123 |

These machine-readable status adjustments **do not** enable, ship or authorize restricted NVIDIA SDKs, ComfyUI/Diffusers model weights, Microsoft OpenMP redistributables, or auto-update of third-party dependencies. Normal protected branch CI and source-license gates remain unchanged.

## Six-phase audit evidence

1. Dependency mapping: source lock `third_party/DEPENDENCIES.lock.json`, consumer/provider manifest reconciliation still ongoing (#123).
2. Versions: `DlssNrManager.csproj` still 3.2.0; Streamline source lock v2.14.1; OptiScaler v0.7.7-pre0 SHA collision #161; no fabricated compatibility claims.
3. NVIDIA: `GpuDetectionService`, `NvidiaSmiLocator`, `HardwareProfileService` detect without NVIDIA App dependency; additional physical driver-install matrix pending.
4. Hardware: AUTO/RTX5060Ti+9700X and Normal/Compatible modes in `HardwareProfileService`; physical RTX and driver-change acceptance pending #95.
5. UI options: Download Center controls and 14-page WPF UI have source paths; all-component version-aware optional update #111, cancellation/rollback and full FR/EN visual checks pending.
6. Tools: Streamline/VLC/Temurin append-only producer hardening in #157/#159; publisher/consumer pinning #123, Windows PowerShell ZIP regression #160 and OptiScaler reproducibility #161 still open.

This is a **partial source audit**, not a claim that all six phases are validated.
