# Component Lifecycle and Distribution Policy

## Purpose

This policy defines how DLSS NR Manager classifies source code, runtime binaries, models and external prerequisites across all product features.

## Classes

### 1. Source-vendored

Use when source redistribution is allowed and keeping an immutable snapshot materially improves reproducibility.

Requirements:

- immutable commit SHA in `third_party/DEPENDENCIES.lock.json`;
- upstream/update policy in `third_party/UPSTREAMS.json` where automated monitoring is useful;
- upstream license retained;
- no nested `.git` directory in staged vendored snapshots.

### 2. Manager-owned release asset

Use for redistributable binaries/models/runtime packages that the application installs.

Requirements:

- hosted in DLSS NR Manager GitHub Releases;
- provenance recorded by script/manifest;
- SHA-256/digest verification before installation/promotion;
- atomic/staged install where practical;
- centralized Downloads install/remove/redownload UX;
- explicit consent before downloads over 1 GiB total package size.

### 3. Restricted local build input

Use for material that may be required to build a manager-owned artifact but must not be committed/redistributed as a raw public dependency.

Current example:

- NVIDIA DLSS SDK under `third_party-local/NVIDIA-DLSS`.

Requirements:

- ignored by Git;
- never uploaded raw by public CI;
- affected output rebuilt locally under the applicable license;
- resulting redistributable output validated before manager-owned publication.

### 4. Manual-license / gated model

Use when user acceptance/access rights are required and project-owned redistribution is not clearly authorized.

Requirements:

- visible in the model catalog if useful;
- official license/model URL recorded;
- explicit license acceptance UI;
- user obtains official files under upstream terms;
- import copies files into managed local storage;
- acceptance must not be interpreted as project redistribution permission.

### 5. External-link / prerequisite

Use when the project should not redistribute the product.

Examples can include:

- Minecraft itself;
- GPU drivers;
- OEM driver/support packages;
- vendor applications surfaced by PC Update.

The manager may detect/open official resources without pretending those products are manager-owned.

## Installation/removal

Shared downloadable components must have one authoritative installation surface: **Downloads**.

Feature pages may expose configuration/use/status, but should not duplicate setup/download buttons for the same shared component.

Removal must:

- target only the managed local copy;
- preserve user-owned source/input files;
- leave reinstall possible;
- avoid deleting unrelated caches/data.

## Integrity

Use the strongest verification available:

- GitHub Release SHA-256 digest;
- project manifest SHA-256;
- Authenticode signer verification for NVIDIA native runtime files where required;
- exact file size plus hash for model assets;
- safe ZIP extraction with traversal protections;
- immutable source commit refs.

## Updates

Automatic promotion is allowed only where the dependency policy explicitly permits it and regression/integrity validation is available.

Use `notify-only` or manual promotion when:

- license boundaries are involved;
- native rebuilds require restricted local inputs;
- model quality may regress;
- runtime compatibility is sensitive;
- very large model assets would be replaced.

## Offline packages

Large runtime/model packages may be split into transport chunks for GitHub Release constraints.

Chunking is a transport detail only:

- no product-level total model/package size cap should be introduced solely because of transport chunking;
- every chunk and reconstructed archive must be verifiable;
- a complete package manifest must carry identity/version/size/hash/install destination.

## Language/UI policy

New user-visible functionality must support both English and French.

This includes:

- static labels;
- dynamic statuses;
- confirmation/error prefixes;
- download details;
- license acceptance UI.

Proper names, model names and legal license names should not be translated.

