# Feature Policy Matrix

This matrix maps product features to their distribution/network/security class.

| Feature | Primary class | Runtime/network source | Offline after install | Central Downloads | License/integrity notes |
| --- | --- | --- | --- | --- | --- |
| Application self-update | Manager-owned release asset | DLSS NR Manager GitHub Releases | N/A | Application page / updater | Release digest + SHA256SUMS |
| Games & DLSS / OptiScaler | Source-vendored + manager-owned asset | DLSS NR Manager Releases | Yes for installed runtime | Yes for shared runtime assets | Pinned source + manager-owned package |
| NVIDIA Streamline / DLSS runtime | Manager-owned asset + restricted-local build boundary | DLSS NR Manager Releases | Yes after install | Yes | NVIDIA signer/hash policy; raw restricted SDK stays local |
| Media Neural / video2dlssnr | Source-vendored + manager-owned asset | DLSS NR Manager Releases | Yes | Yes | Hash/signature validation |
| FFmpeg media runtime | Source-vendored + manager-owned asset | DLSS NR Manager Releases | Yes | Shared with media runtime | LGPL/GPL obligations depend on build |
| Real-ESRGAN | Source-vendored + manager-owned engine/models | DLSS NR Manager Releases | Yes | Yes | Engine/model hashes pinned |
| AI-origin detector | Source-vendored model snapshots + manager-owned model assets | DLSS NR Manager Releases | Yes | Yes | Exact model hashes verified |
| ReShade | Source-vendored + manager-owned asset | DLSS NR Manager Releases | Yes | Yes when exposed as shared installable component | BSD-3-Clause |
| Minecraft RTX / Caustica | Project source + manager-owned runtime bundle | DLSS NR Manager Releases | Yes for installed integration | Yes for shared runtime assets | Minecraft itself remains external prerequisite |
| Temurin JRE | Manager-owned release asset | DLSS NR Manager Releases | Yes | Yes when exposed as shared component | Upstream Java license notices preserved |
| PC Update Center | External-link/discovery | Microsoft/WinGet and official vendor pages | No for discovery | No | Does not pretend vendor software is manager-owned |
| PC Cleanup | Local-only | None required | Yes | No | Destructive actions must stay bounded to intended caches/targets |
| Game artwork/catalog | External metadata/cache | Steam/Epic endpoints/CDNs | Cached data can work locally | No | Network used for metadata/artwork, not telemetry |
| Diagnostics/support bundle | Local-only/user export | None required | Yes | No | User-initiated export; sanitized paths where implemented |
| VSR-HDR / VLC | Manager-owned runtime + corresponding source when shipped | DLSS NR Manager Releases | Yes after install | Yes in final integrated state | VLC remains separate GPL process; corresponding source required |
| Restore HD Video | Local processing using installed components | Installed manager-owned media/AI components | Yes | Dependencies only | Must not imply display-time NVIDIA effects are permanently baked into output |
| Local AI Studio runtime | Source-vendored + manager-owned runtime | DLSS NR Manager Releases | Yes after runtime install | Yes | Python/PyTorch/CUDA userspace isolated |
| Permissive AI Studio models | Manager-owned model package | DLSS NR Manager Releases | Yes | Yes | Manifest + SHA-256 + >1 GiB consent |
| Restricted/gated AI Studio models | Manual-license/gated | Official upstream source + local import | Yes after import | Yes | License acceptance does not grant project redistribution rights |

## Rules that apply to every feature

1. No hidden duplicate installer for a shared downloadable component.
2. No silent third-party runtime fallback when the manager-owned channel is the declared source.
3. New runtime/model dependencies require license classification.
4. New user-visible functionality must support English and French.
5. Downloads over 1 GiB complete package size require explicit approval.
6. User-owned inputs must not be deleted when a managed component is removed.
7. Restricted SDK/model material must not be committed merely because it is publicly downloadable.
8. Release assets must remain auditable through hashes/manifests/notices.

