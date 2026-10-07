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

Streamline root source is MIT-licensed, but its tree contains third-party SDK material with separate license files; preserve all upstream notices. The locked source commit is NVIDIA Streamline v2.14.1, and the lock file also pins the matching official x64 SDK archive and SHA-256. `tools/publish-streamline-runtime.ps1` verifies that official package, extracts only the runtime DLLs used by DLSS NR Manager, validates NVIDIA signatures on proprietary `nvngx_*`/low-latency runtimes, preserves licenses/provenance, and uploads a reduced manager-owned runtime bundle. `release.yml` carries that bundle forward from DLSS NR Manager release history so normal production releases do not need to query NVIDIA's release feed.

FFmpeg media tooling is built from the pinned `third_party/FFmpeg` source snapshot plus the pinned `third_party/nv-codec-headers` headers. Use `tools/build-ffmpeg-windows.sh` on a Linux/MinGW build host. The resulting Windows x64 package keeps FFmpeg's native codec/demuxer coverage, ffprobe, AAC encoding, and NVIDIA NVENC/NVDEC/CUVID support without depending on BtbN release binaries. The configured build intentionally avoids `--enable-gpl` and `--enable-nonfree`.

`video2dlssnr` is vendored as public source, but its native build requires the NVIDIA NGX import library from the local DLSS SDK. Use `tools/build-video2dlssnr.ps1`: it stages the public source plus the local-only SDK in a temporary directory, runs the CPU regression suite, and emits application runtime object code under the ignored `build-local/` tree. A build-only validation can use `-NoPackage`. A complete `video2dlssnr_release.zip` additionally requires both `nvngx_dlss.dll` and a separately supplied `nvngx_dlssnr.dll`; the public NVIDIA/DLSS and Streamline SDK packages do not currently provide the Neural Rendering runtime. Supply it with `-NeuralRuntimePath` or `DLSS_NR_RUNTIME`. The build rejects NVIDIA runtime DLLs unless Authenticode is valid and the signer identifies NVIDIA, and records SHA-256 for both runtimes. For the one-time bootstrap, `tools/publish-video2dlssnr.ps1` builds the validated package, uploads it to an existing DLSS NR Manager release with GitHub CLI, and verifies the remote size/digest. `tools/bootstrap-manager-runtime-assets.ps1` is the preferred entry point: it bootstraps the pinned Streamline runtime bundle, the locally supplied/signed Neural Rendering video runtime, and the verified Minecraft 26.2 runtime bundle into the same manager release, then verifies that all three assets are present. `release.yml` then carries that manager-owned asset forward into every later release, so normal production releases no longer need the upstream `video2dlssnr` release or a public NVIDIA NR download channel. The NVIDIA SDK and raw Neural Rendering runtime must never be committed as standalone SDK/runtime material.

Caustica RTX and the OptiScaler fork contain GPLv3-covered code. Keep their license files and treat them as separately distributed components.

FFmpeg licensing depends on the build configuration. DLSS NR Manager's vendored Windows x64 build intentionally avoids `--enable-gpl` and `--enable-nonfree`, so the distributed FFmpeg runtime remains under FFmpeg's LGPL-compatible configuration. BtbN release binaries are no longer a runtime dependency.

The AI detector repositories currently used by v3 are Apache-2.0 (`ai-image-detection-ONNX`) and MIT (`ai-image-detect-distilled-ONNX`). Keep model cards and license metadata beside the weights.

## Definition of self-contained

A public repository can be self-contained for manager-owned/open-source source trees and redistributable model/runtime assets. It cannot remove dependencies on Windows, an NVIDIA driver, Minecraft, Java/MSVC/Vulkan toolchains, or license-restricted NVIDIA SDK inputs.

The production target is: all redistributable source mirrored here; all permitted runtime assets published from this repository; application services read local/bundled assets first; no production dependency on another GitHub repository; restricted SDK/toolchain inputs remain explicitly local build prerequisites.

For Minecraft 26.2, `tools/bootstrap-minecraft-runtime.ps1` mirrors the stable Fabric Loader profile, every Maven library referenced by that profile, the Fabric Installer provenance artifact, Fabric API, the selected RTX-safe performance mods, and SPBR LabPBR into a single `minecraft-runtime-26.2.zip`. Fabric/Modrinth hashes are verified during bootstrap and again by the application. The application installs Fabric directly from the bundled profile and libraries, so normal Minecraft setup does not call Fabric Meta, Fabric Maven, or Modrinth.
