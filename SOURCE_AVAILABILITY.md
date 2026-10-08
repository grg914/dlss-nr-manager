# Source Availability

DLSS NR Manager distributes or republishes third-party components under different licenses. Source, notices, and provenance must be preserved according to the license that applies to each component.

## Repository source locations

The exact source revision used by a release is recorded by immutable dependency locks and release provenance.

Major source locations include:

- DLSS NR Manager: repository root, MIT.
- Caustica RTX: `Caustica-RTX/`.
- OptiScaler: `third_party/OptiScaler/`.
- FFmpeg: `third_party/FFmpeg/`.
- NVIDIA Streamline source snapshot: `third_party/NVIDIA-Streamline/`.
- Real-ESRGAN NCNN Vulkan: `third_party/Real-ESRGAN-ncnn-vulkan/`.
- ReShade: `third_party/ReShade/`.
- ONNX Runtime: `third_party/onnxruntime/`.
- Minecraft source/runtime provenance: `third_party/minecraft/`.

Vendored roots should retain upstream license and provenance metadata such as `SOURCE.json` where applicable.

## Copyleft components

When a distributed binary is covered by a copyleft license, the corresponding source revision and notices must remain available in the form required by that license.

Release provenance and dependency locks identify the source revision associated with manager-owned runtime assets.

## Proprietary or restricted components

This policy does not grant redistribution rights for proprietary SDKs.

License-restricted NVIDIA DLSS/NGX SDK inputs remain local-only build inputs under `third_party-local/` and must not be published unless redistribution is explicitly permitted.

## Related policy

See:

- [Third-party notices](docs/THIRD_PARTY_NOTICES.md)
- [Release artifact policy](docs/RELEASE_ARTIFACT_POLICY.md)
- [Machine-readable component policy](manifests/component-policy.json)
