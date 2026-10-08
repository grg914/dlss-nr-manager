# Feature Policy Matrix

This matrix covers the current application menus plus active feature branches.

The machine-readable source is `manifests/feature-policy.json`.

| Feature | Network / downloads | Local mutation | Integrity / safety | Rollback / deletion |
| --- | --- | --- | --- | --- |
| Games & DLSS | Manager-owned component/release paths | Selected game directory | SHA-256, managed manifests, NVIDIA trust checks where applicable | Transaction journal + backups |
| Minecraft RTX | Manager-owned locked runtime | Selected Minecraft instance | Runtime lock, hashes, Vulkan/NGX policy, Caustica/SPBR audits | Managed instance changes only |
| Media Neural | Manager-owned FFmpeg/video2dlssnr | Runtime/cache + explicit output | Hash/signature policy | Original media not overwritten by default |
| VSR-HDR Video (#75) | Portable manager-owned VLC install | VLC runtime; optional new restored output | VLC SHA-256 + source obligations | Direct playback leaves source unchanged |
| AI Detection | Manager-owned ONNX models | Model cache | Pinned model SHA-256 | Remove/redownload model cache |
| Local AI Studio (#77) | Manager-owned packages or explicit official gated-model flow | Private runtime/models/jobs/outputs | Manifest + chunk/full SHA-256; AI runtime security policy | Managed remove/reimport; outputs preserved by default |
| Downloads (#77) | Explicit component operations | Managed component roots | Component-specific digest/signature | Install/remove/redownload/cancel |
| PC Update Center | WinGet/Chocolatey/Windows Update/vendor HTTPS | External software/system after confirmation | Trusted URI validation; external trust boundary documented | External updater responsibility |
| PC Cleanup | No network required | Reviewed temp/shader cache roots | Analyze + canonical root/path checks + explicit confirmation | No cache rollback; caches rebuild |
| Diagnostics | Local | Report file only | Read-only inspection where practical | N/A |
| Advanced OptiScaler | Manager-owned component paths | Managed game config/files | Pinned source/runtime + file hashes | Game backup/manifest |
| OptiScaler log | Local | Read-only | N/A | N/A |
| Application | Project GitHub Releases | App executable/cache/data | SHA-256, safe ZIP extraction, version validation | Update backup; explicit local-data deletion |

## Global invariants

- No hidden duplicate component download paths when a component is centrally managed.
- No production fallback from a manager-owned/offline operation to an unreviewed upstream download.
- User media is not uploaded by local processing features.
- Destructive PC cleanup requires explicit selection and confirmation.
- Game modifications use managed manifests/backups/rollback where available.
- Stable release artifacts are immutable.
- Restricted SDK/model licensing is independent from hash/signature validity.
- New executable/custom-node/script surfaces require security review.

## Feature-branch reconciliation

When #75 and #77 are reconciled:

- VLC must move into the centralized Downloads surface.
- The VSR-HDR page must not keep a duplicate VLC setup/download button.
- Media Neural / AI Detection / AI Upscale binaries/models must keep Downloads as the canonical install/remove/redownload surface.
