# Threat Model

## Scope

This threat model covers the DLSS NR Manager desktop application, its manager-owned release channel, vendored sources, helper processes, game-install modification, media/AI pipelines and update/cleanup functions.

## Trust boundaries

### 1. GitHub repository source

Trusted only after review/CI. Third-party source is pinned to immutable revisions and should not be treated as trusted merely because it exists on GitHub.

### 2. Manager-owned GitHub Releases

Runtime assets are project-controlled distribution inputs, but every asset must still be treated as untrusted until its expected identity/integrity checks pass.

Controls include:

- release digest/SHA-256;
- component manifests;
- NVIDIA signer checks where applicable;
- bounded/safe extraction;
- staged/atomic deployment.

### 3. External vendor/update/artwork services

PC Update, Steam/Epic artwork and official vendor links are external trust domains.

These services must not become implicit sources for manager-owned runtime components unless the component policy explicitly changes.

### 4. User-selected files/directories

Untrusted inputs include:

- games and executables;
- media files;
- AI model import folders;
- output paths;
- support-bundle destinations;
- custom scan roots.

Controls should include path normalization/containment, bounded parsing and no deletion of user-owned source content during managed-component removal.

### 5. Archives/packages

ZIP/tar/package inputs are untrusted until validated.

Threats:

- path traversal;
- archive bombs / excessive expansion;
- unexpected executable substitution;
- corrupted/truncated chunks;
- mismatched package identity.

### 6. Helper/native processes

FFmpeg, ffprobe, Real-ESRGAN, VLC, Java/Fabric helpers and other manager-owned tools run as separate processes.

Threats:

- stale background process after cancellation/exit;
- command-line/path injection;
- executing the wrong binary;
- loading modified native DLLs.

Use explicit executable paths, argument APIs/escaping, process lifetime tracking and integrity checks.

### 7. Game-install modification

Game directories are user-owned state.

Threats:

- overwriting user files;
- incomplete transaction;
- restoration outside the intended game root;
- following junctions/reparse points outside the target;
- anti-cheat compatibility hazards.

Controls include transaction journals, backups, managed-file manifests, containment checks and fail-closed safety behavior.

### 8. Cleanup

Cleanup is destructive by design.

Threats:

- deleting outside intended cache roots;
- deleting user data rather than regenerable cache;
- traversal/junction escape.

Cleanup targets must remain allow-listed and path-normalized.

### 9. AI Studio

Additional threats:

- model packages with executable Python/custom nodes;
- gated/restricted model license violations;
- arbitrary Python package installation;
- hidden internet dependency;
- malicious user-imported model files.

Controls:

- isolated Python/PyTorch/CUDA userspace;
- manager-owned wheelhouse for offline runtime packages;
- no global pip/system Python mutation;
- manager-owned model packages only where redistribution is allowed;
- manual-license acceptance/import for restricted models;
- package manifests and hashes;
- future custom-node installation should default to disabled or explicit trust approval.

## Data/privacy threats

The application should not upload user media, prompts, generated content, game files or local diagnostics without an explicit user action.

Support bundles are local exports. Any future upload/share feature must introduce a separate explicit consent path and update the network/privacy policy.

## Out of scope / inherited risk

The project cannot guarantee security against:

- a fully compromised Windows host;
- malicious GPU/OS drivers;
- compromised GitHub accounts/tokens;
- malicious upstream code that passes review and is deliberately promoted;
- vulnerabilities in Minecraft, games, VLC, FFmpeg, Python, PyTorch, GPU drivers or other upstream software.

The manager's responsibility is to reduce supply-chain and integration risk through pinning, validation, containment and explicit user consent.

