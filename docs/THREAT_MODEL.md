# Threat Model

## Assets to protect

DLSS NR Manager protects:

- the integrity of its own executable and updates;
- game/runtime files it manages;
- Minecraft backup/restore state;
- locally installed AI/media runtimes and models;
- local user files selected for processing;
- restricted build inputs that must never enter the public repository;
- release provenance and dependency locks.

## Primary threats

### Supply-chain substitution

An attacker replaces a runtime/model/archive with a different payload.

Mitigations:
- manager-owned release channel;
- immutable source refs;
- GitHub release digests;
- explicit SHA fingerprints;
- Authenticode validation for NVIDIA runtimes;
- final release checksum/provenance manifests.

### Path traversal / archive escape

A crafted archive writes outside its managed install directory.

Mitigations:
- bounded safe archive extraction;
- staging directories;
- canonical-path containment checks;
- atomic replacement where practical.

### Silent upstream drift

A mutable tag/latest release changes behavior without review.

Mitigations:
- dependency lock file;
- source commit SHA pins;
- explicit upstream-monitor PRs;
- notify-only policy for restricted/high-risk dependencies.

### Local privilege / destructive mutation

A cleanup/update/install action removes or overwrites unrelated user data.

Mitigations:
- manager-owned path tracking;
- backup/restore before Minecraft mutation;
- conservative cleanup scope;
- explicit confirmation for destructive/bulk actions;
- fail-closed behavior when ownership cannot be established.

### Malicious or corrupted models/runtimes

A downloaded AI model or native runtime executes unsafe code or consumes unexpected resources.

Mitigations:
- controlled sources;
- hash/provenance verification;
- isolated Python/runtime directories;
- no global pip/Python/CUDA mutation;
- explicit license/import flow for gated models;
- download-size confirmation for large packages.

### Credential/secret leakage

A token, SDK credential or private key is committed or written to logs/releases.

Mitigations:
- ignored local-only SDK directory;
- policy check for common private-key/secret file types;
- no normal logging of access tokens;
- PR checklist and release-policy audit.

## Trust boundaries

Trusted only after validation:
- files downloaded from GitHub Releases;
- vendored source trees;
- user-imported gated model files;
- third-party runtime archives;
- NVIDIA native binaries.

Not automatically trusted:
- filenames;
- upstream `latest` metadata;
- archive internal paths;
- model cards without the corresponding license;
- unsigned native binaries merely because they have an NVIDIA-like name.

## Out of scope

DLSS NR Manager cannot guarantee:
- safety of arbitrary user-supplied executables/models;
- behavior of third-party games/anti-cheat systems;
- correctness of external GPU drivers or Windows;
- continued availability of upstream services;
- redistribution rights that an upstream license does not grant.
