# Third-Party Notices and Redistribution

This document is the repository-level policy for third-party code, runtimes, models and assets used by DLSS NR Manager.

It does not replace the actual upstream license text. The authoritative license for each component is the license shipped with or linked from that component.

## Core rule

Before a third-party component is vendored, bundled, mirrored into a manager-owned GitHub Release, or installed automatically, the project must determine:

1. exact source/project identity;
2. immutable source revision or exact release version;
3. applicable license;
4. whether source redistribution is permitted;
5. whether binary/weight redistribution is permitted;
6. attribution/source-offer obligations;
7. integrity verification method;
8. update policy.

## Repository classes

### Public vendored source

Stored under `third_party/`.

Requirements:

- immutable source commit;
- source URL;
- retained license/notice files;
- provenance metadata;
- no unresolved Git metadata or unmaterialized LFS pointers in promoted snapshots.

### Local-only restricted source

Stored under an ignored path such as `third_party-local/`.

Example:

- NVIDIA DLSS SDK input.

Requirements:

- never committed as normal public vendored material;
- never published raw unless redistribution rights are independently confirmed;
- may be used locally to build permitted manager-owned runtime packages.

### Manager-owned Release assets

Large redistributable binaries/models are distributed from this project's GitHub Releases rather than Git history.

Requirements:

- exact version/provenance;
- license/notice preservation;
- SHA-256/digest validation;
- deterministic naming/manifests where practical;
- no silent substitution with a different upstream file.

### User-supplied restricted/gated models

Some AI models can be selected in the UI but cannot be mirrored automatically by the project.

For these models:

- show the license identity;
- require explicit user acceptance when the product flow calls for it;
- obtain files from the official source;
- import the user's local copy into managed storage;
- do not infer redistribution rights from end-user acceptance.

## Important component families

The project currently or actively plans to manage component families including:

- NVIDIA DLSS / NGX;
- NVIDIA Streamline;
- video2dlssnr;
- FFmpeg;
- nv-codec-headers;
- OptiScaler;
- ReShade;
- Real-ESRGAN / ncnn / Vulkan runtime dependencies;
- ONNX Runtime and AI-origin models;
- Temurin JRE;
- Minecraft/Fabric dependencies;
- Caustica RTX and resource packs;
- VLC;
- ComfyUI;
- Diffusers;
- FLUX;
- Qwen Image;
- Wan;
- LTX.

The exact revision/status of a component must come from the lock/manifests, not this prose list.

## GPL components

GPL code such as ComfyUI must remain architecturally separate where required by the intended licensing model.

If GPL software is redistributed in binary form, preserve the license and satisfy corresponding-source obligations.

## NVIDIA material

NVIDIA source/runtime licensing must be evaluated per artifact.

Public accessibility does not imply unrestricted redistribution.

The project's existing rule for the NVIDIA DLSS SDK is local-only unless separately cleared.

## AI model weights

Model code licenses and model-weight licenses may differ.

Do not assume that an Apache/MIT reference implementation grants rights to redistribute separately licensed weights.

## Machine-readable sources of truth

Use:

- `third_party/DEPENDENCIES.lock.json`
- `third_party/UPSTREAMS.json`
- `third_party/minecraft/RUNTIME.lock.json`
- `manifests/component-policy.json`
- feature-specific model/runtime manifests

for exact machine-readable policy/provenance.

## Audit requirement

Any newly redistributed component must update the relevant machine-readable manifest or lock and preserve its license/notice before production promotion.
