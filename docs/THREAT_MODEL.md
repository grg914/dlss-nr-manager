# Threat Model

## Assets to protect

DLSS NR Manager protects:

- the integrity of its executable and updates;
- game/runtime files it manages;
- Minecraft backup/restore state;
- locally installed AI/media runtimes and models;
- local user files selected for processing;
- restricted build inputs that must never enter the public repository;
- release provenance, dependency locks, and manager-owned runtime assets.

## Primary threats

### Supply-chain substitution

An attacker replaces a runtime, model, archive, or source snapshot with different bytes.

Mitigations include manager-owned release channels, immutable refs, SHA-256 validation, signer checks where applicable, SBOM/provenance, and fail-closed release promotion.

### Path traversal / archive escape

A crafted archive writes outside its managed install directory.

Mitigations include bounded extraction, staging directories, canonical-path containment checks, and atomic replacement where practical.

### Silent upstream drift

A mutable tag or latest release changes behavior without review.

Mitigations include dependency locks, immutable source SHAs, explicit upstream-monitor changes, and notify-only policy for restricted/high-risk dependencies.

### Local destructive mutation

A cleanup/update/install action removes or overwrites unrelated user data.

Mitigations include manager-owned path tracking, backups before managed mutations, conservative cleanup scope, explicit confirmation for destructive actions, and fail-closed behavior when ownership cannot be established.

### Malicious or corrupted models/runtimes

A downloaded AI model or native runtime executes unsafe code or consumes unexpected resources.

Mitigations include controlled sources, hash/provenance verification, isolated runtimes, no global Python/pip/CUDA mutation, license gating, and explicit approval for large downloads.

### Credential or secret leakage

A token, SDK credential, private key, or restricted binary is committed or written to logs/releases.

Mitigations include local-only ignored SDK directories, repository policy checks, no normal token logging, and release/PR governance.

## Trust boundaries

Trusted only after validation:

- manager-owned GitHub Release assets;
- vendored source trees;
- user-imported gated files;
- third-party runtime archives;
- NVIDIA native binaries.

Not automatically trusted:

- filenames;
- mutable upstream metadata;
- archive internal paths;
- model cards without the corresponding license;
- unsigned native binaries merely because they resemble vendor files.

## Out of scope

DLSS NR Manager cannot guarantee the safety of arbitrary user-supplied executables/models, third-party anti-cheat policy, external GPU drivers/Windows behavior, upstream service availability, or redistribution rights not granted by an upstream license.
