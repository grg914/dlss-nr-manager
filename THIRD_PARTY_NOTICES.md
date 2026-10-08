# Third-Party Notices

DLSS NR Manager contains, vendors, builds, or distributes components owned by third parties. The root `LICENSE` (MIT) covers DLSS NR Manager's own code only and does **not** replace upstream licenses.

The canonical machine-readable source provenance is:

- `third_party/DEPENDENCIES.lock.json`
- `third_party/UPSTREAMS.json`
- `third_party/minecraft/RUNTIME.lock.json`
- per-component `SOURCE.json`, license and notice files inside vendored trees
- manager-owned release manifests and SHA-256 files

## Major component families

| Component family | Typical license/boundary | Distribution policy |
| --- | --- | --- |
| Caustica RTX | GPL-covered project component | Preserve GPL/source/license obligations |
| OptiScaler | GPL-covered component/fork | Preserve GPL/source/license obligations |
| NVIDIA Streamline | MIT root with separately licensed bundled SDK/runtime material | Preserve all notices; proprietary NVIDIA runtimes require separate validation |
| NVIDIA DLSS / NGX SDK | NVIDIA proprietary SDK terms | Local-only build input unless redistribution is explicitly cleared |
| FFmpeg | LGPL-compatible configuration in this project | Build intentionally avoids `--enable-gpl` and `--enable-nonfree` |
| Real-ESRGAN / ncnn / Vulkan support | Upstream open-source licenses | Preserve upstream licenses and model notices |
| ONNX Runtime | MIT | Preserve upstream license |
| AI-origin detector models | Model-specific Apache-2.0 / MIT metadata in vendored model repositories | Preserve model cards/licenses beside weights |
| Minecraft Fabric/mod ecosystem | Per-project open-source licenses | Versions/hashes frozen in runtime lock; preserve bundled notices |
| ReShade | Upstream license | Preserve upstream license in manager-owned package |
| VLC (when distributed) | GPL/LGPL component boundaries | Keep VLC as a separate process/runtime and provide corresponding source/notices |
| ComfyUI (when distributed) | GPL-3.0 | Keep as a separate process/runtime and preserve corresponding source/license |
| Diffusers (when distributed) | Apache-2.0 | Preserve license/notice |
| AI generation model weights | Per-model license | Redistribution is model-specific; gated/restricted models require explicit acceptance/import policy |

This summary is intentionally not a substitute for each upstream license. When a component is added or updated, its exact license files and required notices must remain with the redistributed source/runtime or be included in the release's corresponding notice/source package.
