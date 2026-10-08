# Source Availability

DLSS NR Manager distributes or republishes several third-party components whose licenses require preservation of notices and, for copyleft components, access to corresponding source.

## Repository source locations

The exact source revision used for a release is recorded by `release-provenance.json` and the immutable dependency locks.

Major source locations:

- DLSS NR Manager: repository root, MIT.
- Caustica RTX: `Caustica-RTX/`.
- OptiScaler: `third_party/OptiScaler/`.
- FFmpeg: `third_party/FFmpeg/`.
- NVIDIA Streamline source snapshot: `third_party/NVIDIA-Streamline/`.
- Real-ESRGAN NCNN Vulkan: `third_party/Real-ESRGAN-ncnn-vulkan/`.
- ReShade: `third_party/ReShade/`.
- ONNX Runtime: `third_party/onnxruntime/`.
- Minecraft source mirrors: `third_party/minecraft/` when materialized by the vendoring workflow.

Each vendored root should retain its upstream license and `SOURCE.json` provenance metadata.

## Copyleft components

When a release distributes a GPL-covered binary/component, the corresponding source revision must remain available from this repository (or an explicitly linked source archive) for the distributed version.

The release provenance manifest records the source commit/dependency refs needed to identify that source snapshot.

## Proprietary/restricted components

This source-availability policy does not grant redistribution rights for proprietary SDKs. NVIDIA DLSS/NGX SDK inputs that are license-restricted remain local-only build inputs under `third_party-local/` and are not published as public source artifacts.

## Release notices

Binary/runtime packages should carry their own license/source metadata where practical. The top-level release also publishes:

- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- `component-policy.json`
- `release-provenance.json`
- `SHA256SUMS.txt`

These files document the source/distribution boundary but do not replace any upstream license text that must accompany a specific component.
