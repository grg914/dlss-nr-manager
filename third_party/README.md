# Third-party source layout

This directory is the source mirror used to make DLSS NR Manager independent from live upstream repositories during normal development and release preparation.

Intended layout:

- `../Caustica-RTX/` — project-owned Caustica RTX source at repository top level.
- `NVIDIA-Streamline/` — NVIDIA Streamline source snapshot.
- `OptiScaler/` — OptiScaler DLSS-NR source snapshot.
- `video2dlssnr/` — video neural-rendering processor source.
- `FFmpeg-Builds/` and `FFmpeg/` — build recipes and upstream FFmpeg source.
- `Real-ESRGAN-ncnn-vulkan/` — Real-ESRGAN NCNN Vulkan source.
- `Real-ESRGAN-model-sources/` — exact source snapshots currently used for model files.
- `ReShade/` — ReShade source.
- `ai-models/` — exact Hugging Face model repositories used by the local AI-origin detector.
- `minecraft/` — optional Fabric/performance-mod source mirrors.

Run `tools/vendor-third-party.ps1` to materialize these folders without nested `.git` directories. Each imported folder receives a `SOURCE.json` with its upstream URL and pinned ref.

## Licensing boundary

Do not commit `NVIDIA/DLSS` as a normal vendored public dependency without a dedicated license review. NVIDIA RTX SDK licensing restricts standalone SDK redistribution. The bootstrap therefore places it under `third_party-local/`, which is Git-ignored and can be referenced locally through `DLSS_SDK`.

Streamline root source is MIT-licensed, but its tree contains third-party SDK material with separate license files; preserve all upstream notices.

Caustica RTX and the OptiScaler fork contain GPLv3-covered code. Keep their license files and treat them as separately distributed components.

FFmpeg licensing depends on the build configuration. The current BtbN package selected by the manager is a GPL build; mirrored binaries need the corresponding license/source obligations.

The AI detector repositories currently used by v3 are Apache-2.0 (`ai-image-detection-ONNX`) and MIT (`ai-image-detect-distilled-ONNX`). Keep model cards and license metadata beside the weights.

## Definition of self-contained

A public repository can be self-contained for manager-owned/open-source source trees and redistributable model/runtime assets. It cannot remove dependencies on Windows, an NVIDIA driver, Minecraft, Java/MSVC/Vulkan toolchains, or license-restricted NVIDIA SDK inputs.

The production target should be: all redistributable source mirrored here; all permitted runtime assets published from this repository; application services read local/bundled assets first; no production dependency on another GitHub repository; restricted SDK/toolchain inputs remain explicitly local build prerequisites.