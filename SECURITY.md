# Security Policy

## Supported versions

Security fixes are targeted at:

- the current stable release line (`v3.2.x` at the time this policy was added);
- the current `main` branch;
- active pull requests that are expected to merge into `main`.

Older release lines may not receive fixes.

## Reporting a vulnerability

Do not publish exploit details, malicious payloads, tokens, private keys, restricted SDK files, or user data in a public issue.

Preferred reporting path:

1. Use GitHub private vulnerability reporting / a private security advisory if the repository exposes that option.
2. If private reporting is unavailable, open a minimal public issue asking for a private contact channel. Do not include sensitive technical details in that public issue.

Please include, when safe:

- affected version/commit;
- affected feature;
- reproduction steps;
- expected vs. observed behavior;
- security impact;
- whether exploitation requires local access, network access, a crafted archive/model/package, or elevated privileges.

## Security boundaries

DLSS NR Manager intentionally treats the following as security-sensitive operations:

- downloading executable/runtime/model assets;
- extracting ZIP/package contents;
- launching helper processes;
- modifying game installations;
- updating the application;
- installing or replacing manager-owned components;
- importing AI model files;
- consuming NVIDIA-signed native runtime files.

Security-sensitive changes must preserve these rules:

- use HTTPS for network downloads;
- prefer manager-owned GitHub Release assets for redistributable runtime components;
- verify SHA-256/digests before promotion or installation where a trusted digest is available;
- validate NVIDIA Authenticode signatures for NVIDIA runtime files where the existing runtime policy requires it;
- use bounded/safe archive extraction and reject path traversal;
- stage replacements before committing them to the final destination;
- do not silently fall back to arbitrary third-party mirrors;
- do not automatically redistribute restricted/gated model weights or license-restricted SDK material;
- keep the NVIDIA DLSS SDK under `third_party-local/` unless redistribution is separately cleared;
- keep AI Studio Python/PyTorch/CUDA userspace isolated from system Python/CUDA;
- require explicit user approval before manager-owned downloads whose complete package size exceeds 1 GiB;
- avoid hidden component downloads from feature pages: install/remove/redownload operations belong in the centralized Downloads surface.

## Supply-chain policy

Source dependencies are pinned in:

- `third_party/DEPENDENCIES.lock.json`
- `third_party/UPSTREAMS.json`
- `third_party/minecraft/RUNTIME.lock.json` where applicable
- feature-specific manifests under `manifests/`

Production runtime assets are expected to originate from DLSS NR Manager's own release channel unless explicitly documented otherwise.

A source being publicly downloadable does not imply that it may be redistributed. License-restricted material must remain local/manual when required.

## Secrets and credentials

Never commit:

- GitHub tokens;
- Hugging Face tokens;
- Microsoft credentials;
- private signing keys/certificates;
- API keys;
- restricted SDK credentials;
- private model-access cookies/tokens.

Use GitHub Actions secrets or local environment variables where credentials are required.

## Privacy

The application is not designed to send telemetry, analytics, prompts, media, generated images/videos, game files, or AI Studio jobs to DLSS NR Manager servers.

See `docs/NETWORK_OFFLINE_PRIVACY_POLICY.md` for the network/offline model and the intentionally external services used by specific features.

