# Security Policy

## Supported versions

Security fixes target the latest stable DLSS NR Manager release and the current `main` branch.

Older releases may not receive fixes unless a regression in the current release requires backporting.

## Reporting a vulnerability

Do not publish exploitable details in a public issue.

Preferred reporting path:

1. Use GitHub **Security > Report a vulnerability** / Private Vulnerability Reporting when available.
2. Include the affected version/commit, component, reproduction steps, impact, and any proof-of-concept needed to reproduce safely.
3. If private vulnerability reporting is unavailable, contact the repository owner privately through GitHub before public disclosure.

Do not attach passwords, API keys, access tokens, proprietary NVIDIA SDK material, DRM-protected content, personal files, or other secrets.

## Security boundaries

DLSS NR Manager handles several high-risk classes of components:

- executable Windows binaries and DLLs;
- manager-owned GitHub Release assets;
- NVIDIA runtimes and locally licensed SDK inputs;
- video/media decoders;
- Minecraft mods/resource packs;
- AI models and Python runtimes;
- large archives and chunked packages;
- optional third-party source snapshots.

Changes affecting these areas must preserve:

- HTTPS-only production download paths;
- immutable source refs where applicable;
- SHA-256 verification for promoted binary assets;
- Authenticode verification for NVIDIA binaries where the project already requires it;
- archive path traversal protections;
- size/entry-count safety checks;
- no silent system-wide Python/CUDA mutation;
- no raw redistribution of license-restricted NVIDIA SDK material;
- no hidden component downloads outside the centralized download/update flows.

## Secrets

Never commit:

- GitHub tokens;
- Hugging Face tokens;
- NVIDIA developer credentials or restricted SDK archives;
- private signing certificates/keys;
- personal account credentials;
- machine-specific secrets.

Use GitHub Actions secrets/environment protection for credentials required by automation.

## Dependencies and supply chain

Production Build/Release must remain reproducible from manager-owned or vendored inputs according to the repository's zero-upstream policy.

Dependency updates must go through the lock/provenance system and the upstream refresh workflow rather than silently changing production inputs.

See:

- `third_party/README.md`
- `third_party/DEPENDENCIES.lock.json`
- `third_party/UPSTREAMS.json`
- `docs/UPSTREAM-SYNC.md`
- `docs/RELEASE_ARTIFACT_POLICY.md`
- `docs/THIRD_PARTY_NOTICES.md`

## Security-sensitive review

At minimum, changes to these paths require explicit review of integrity and licensing implications:

- `.github/workflows/`
- `tools/`
- `Services/*Update*`
- `Services/*Runtime*`
- `Services/*Download*`
- `third_party/`
- `third_party-local/`
- `manifests/`
- release packaging code;
- archive extraction code;
- installer/update code.

## Disclosure

After a fix is available, a public issue/advisory may summarize the impact without exposing user secrets or restricted vendor material.
