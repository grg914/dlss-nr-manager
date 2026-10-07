# Third-party source layout

This directory is the source mirror used to make DLSS NR Manager independent from live upstream repositories during normal development and release preparation.

Intended layout:

- `../Caustica-RTX/` — project-owned Caustica RTX source at repository top level.
- `NVIDIA-Streamline/` — NVIDIA Streamline source snapshot.
- `OptiScaler/` — OptiScaler DLSS-NR source snapshot.
- `video2dlssnr/` — video neural-rendering processor source.
- `FFmpeg/` — pinned upstream FFmpeg source. `FFmpeg-Builds/` is retained only as a historical/reference build recipe snapshot and is not used by the manager runtime.
- `Real-ESRGAN-ncnn-vulkan/` — Real-ESRGAN NCNN Vulkan source.
- `Real-ESRGAN-model-sources/` — exact source snapshots currently used for model files.
- `ReShade/` — ReShade source.
- `ScoopInstaller-Versions/` — pinned manifest source currently used for ReShade metadata.
- `onnxruntime/` — pinned ONNX Runtime source matching the managed package version.
- `ai-models/` — exact Hugging Face model repositories used by the local AI-origin detector.
- `minecraft/` — Fabric/performance-mod source mirrors imported when `-IncludeMinecraftSources` is requested.

## Reproducible vendor import

`DEPENDENCIES.lock.json` is the single source of truth for public vendored dependencies. Every entry is pinned to an immutable 40-character commit SHA. Human-readable upstream branch/tag information can be retained as metadata, but it is never used as the checkout ref.

Run `tools/vendor-third-party.ps1` to materialize the snapshots. The importer:

- checks out the exact locked commit;
- initializes recursive submodules;
- materializes Git LFS content when Git LFS is available;
- refuses imports that still contain Git LFS pointer files;
- removes nested `.git` directories and submodule `.git` files;
- writes a `SOURCE.json` provenance file in each imported root;
- refuses to silently reuse an existing folder whose provenance differs from the lock file.

Use `-Replace` when intentionally refreshing an existing imported folder after changing the lock file.

Some locked sources may define a `retention` policy. This is used when the application only requires a small, immutable subset of a large upstream snapshot. The importer applies the policy after checkout/LFS materialization, and the verifier reports an error if excluded files reappear. For the two AI-origin detector repositories, all ONNX variants are retained until a benchmark demonstrates which variant is best for each supported application mode. The selection criteria are reliability/accuracy first, then inference performance and memory use, then repository size. INT8 remains the current runtime path, but full-precision, FP16 and lower-precision candidates must not be removed solely to save space before they are measured on representative hardware and a labeled real-vs-AI validation set.

Run `tools/verify-self-contained.ps1` to audit the checkout. With `-Strict`, the verifier fails on missing locked mirrors, mutable refs, provenance mismatches, nested Git metadata, unresolved Git LFS pointers, or remaining direct upstream runtime/release dependencies.

## Licensing boundary

Do not commit `NVIDIA/DLSS` as a normal vendored public dependency without a dedicated license review. NVIDIA RTX SDK licensing restricts standalone SDK redistribution. The bootstrap therefore places it under `third_party-local/`, which is Git-ignored and can be referenced locally through `DLSS_SDK`.

Streamline root source is MIT-licensed, but its tree contains third-party SDK material with separate license files; preserve all upstream notices.

FFmpeg media tooling is built from the pinned `third_party/FFmpeg` source snapshot plus the pinned `third_party/nv-codec-headers` headers. Use `tools/build-ffmpeg-windows.sh` on a Linux/MinGW build host. The resulting Windows x64 package keeps FFmpeg's native codec/demuxer coverage, ffprobe, AAC encoding, and NVIDIA NVENC/NVDEC/CUVID support without depending on BtbN release binaries. The configured build intentionally avoids `--enable-gpl` and `--enable-nonfree`.

`video2dlssnr` is vendored as public source, but its native build requires the NVIDIA NGX import library from the local DLSS SDK. Use `tools/build-video2dlssnr.ps1`: it stages the public source plus the local-only SDK in a temporary directory, runs the CPU regression suite, and emits application runtime object code under the ignored `build-local/` tree. A build-only validation can use `-NoPackage`. A complete `video2dlssnr_release.zip` additionally requires both `nvngx_dlss.dll` and a separately supplied `nvngx_dlssnr.dll`; the public NVIDIA/DLSS checkout contains the former but not the Neural Rendering runtime. Supply the latter with `-NeuralRuntimePath` or `DLSS_NR_RUNTIME`. The script records SHA-256 and Authenticode status for both runtime DLLs. The NVIDIA SDK itself must never be copied into the public repository or distributed as a standalone SDK, and no manager release should redistribute the Neural Rendering runtime until its provenance and redistribution terms have been explicitly cleared.

Caustica RTX and the OptiScaler fork contain GPLv3-covered code. Keep their license files and treat them as separately distributed components.

FFmpeg licensing depends on the build configuration. DLSS NR Manager's vendored Windows x64 build intentionally avoids `--enable-gpl` and `--enable-nonfree`, so the distributed FFmpeg runtime remains under FFmpeg's LGPL-compatible configuration. BtbN release binaries are no longer a runtime dependency.

The AI detector repositories currently used by v3 are Apache-2.0 (`ai-image-detection-ONNX`) and MIT (`ai-image-detect-distilled-ONNX`). Keep model cards and license metadata beside the weights.

## Definition of self-contained

A public repository can be self-contained for manager-owned/open-source source trees and redistributable model/runtime assets. It cannot remove dependencies on Windows, an NVIDIA driver, Minecraft, Java/MSVC/Vulkan toolchains, or license-restricted NVIDIA SDK inputs.

The production target is: all redistributable source mirrored here; all permitted runtime assets published from this repository; application services read local/bundled assets first; no production dependency on another GitHub repository; restricted SDK/toolchain inputs remain explicitly local build prerequisites.
