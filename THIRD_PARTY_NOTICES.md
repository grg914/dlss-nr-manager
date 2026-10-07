# Third-Party Notices

DLSS NR Manager itself is licensed under the MIT License. It vendors, builds, launches, or redistributes components with different licenses.

This file is a high-level notice. The exact upstream license text and notices included in vendored source directories and redistributed source/runtime packages prevail.

## Core/native/media components

| Component | Role | License / redistribution note |
| --- | --- | --- |
| Microsoft .NET 8 / WPF runtime | Self-contained Windows application runtime | MIT and bundled Microsoft/.NET third-party notices as applicable |
| Microsoft.ML.OnnxRuntime | AI-origin inference | MIT |
| Eclipse Temurin / OpenJDK 25 JRE | Minecraft/Java runtime when distributed | GPLv2 with Classpath Exception / bundled legal notices; preserve upstream legal files |
| FFmpeg | Media decode/encode | LGPL/GPL depending on build configuration; preserve corresponding notices/source obligations |
| nv-codec-headers | NVIDIA codec headers | NVIDIA permissive notices embedded upstream |
| Real-ESRGAN ncnn Vulkan | AI upscaling | MIT/BSD-style components as recorded in dependency lock; model notices preserved |
| ReShade | Graphics integration | BSD-3-Clause |
| OptiScaler | Upscaling/runtime integration | See `third_party/OptiScaler/LICENSE` and its bundled third-party notices |
| NVIDIA Streamline | NVIDIA runtime integration | See `third_party/NVIDIA-Streamline/license.txt` and `3rd-party-licenses.md` |
| video2dlssnr | Neural Rendering media pipeline | Upstream license plus separately licensed NVIDIA runtime material |
| NVIDIA DLSS SDK | Local build input | License-restricted; intentionally kept under `third_party-local/` and not blindly redistributed |

## AI-origin detection

The two manager-owned ONNX detector assets are pinned to immutable source revisions. Their source snapshots and redistribution notes are recorded in `third_party/DEPENDENCIES.lock.json`.

Current source families include:

- `onnx-community/ai-image-detection-ONNX` — Apache-2.0;
- `onnx-community/ai-image-detect-distilled-ONNX` — MIT.

## Local AI Studio

Source/runtime components:

| Component | Policy |
| --- | --- |
| ComfyUI | GPL-3.0; keep as a separate external process/runtime when redistributed; preserve source/license obligations |
| Hugging Face Diffusers | Apache-2.0 |
| FLUX.2 reference implementation | Apache-2.0; model weights are licensed separately |
| Wan2.2 reference implementation | Apache-2.0; model weights are handled separately |

Model-weight policy is machine-readable in `manifests/ai-studio-models.json`.

Examples:

- FLUX.2 [klein] 4B: permissive manager-owned redistribution path when all model terms are satisfied;
- FLUX.2 [dev]: gated / non-commercial license path, manual acceptance/import;
- Qwen-Image-2.1: research-license path, manual acceptance/import;
- LTX-2.5: community-license path, manual acceptance/import;
- SDXL family: preserve model license/attribution;
- Wan2.2 family: preserve model license/attribution.

Accepting a model license in DLSS NR Manager does not create redistribution rights that the upstream license does not grant.

## VLC / VSR-HDR Video

If/when the VLC runtime is distributed with DLSS NR Manager:

- VLC runtime remains a separate process;
- VLC is GPL-2.0-or-later, with libVLC portions under LGPL-2.1-or-later as applicable;
- the corresponding source archive and provenance must be made available through the manager-owned release channel;
- DLSS NR Manager must not imply that its MIT license relicenses VLC.

## Minecraft/Caustica

Caustica RTX has its own license and third-party notices under `Caustica-RTX/`.

Minecraft/Fabric-related source/runtime dependencies are recorded in `third_party/DEPENDENCIES.lock.json` and `third_party/minecraft/RUNTIME.lock.json`. Their upstream licenses must be preserved in source/runtime bundles as applicable.

## Release rule

A production release that redistributes third-party binaries/models must preserve the relevant notices and source-offer obligations.

The release workflow should publish, directly or inside the application bundle:

- DLSS NR Manager's `LICENSE`;
- this `THIRD_PARTY_NOTICES.md`;
- corresponding source archives where copyleft/source-offer obligations require them;
- provenance/manifests and SHA-256 material used by the manager-owned channel.

