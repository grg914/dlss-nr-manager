# Security Policy

## Supported versions

Security fixes are applied to the current `main` branch and the latest published DLSS NR Manager release. Older releases may be superseded rather than patched in place.

## Reporting a vulnerability

Do **not** open a public issue for a vulnerability that could expose users, enable arbitrary code execution, bypass integrity checks, escape managed paths, weaken signature/hash validation, or disclose private data.

Use GitHub's private vulnerability reporting / Security Advisory flow for this repository when available. Include:

- affected version or commit;
- reproduction steps;
- expected vs actual behavior;
- affected file/component;
- whether exploitation requires local access, network access, a crafted archive/model, or elevated privileges;
- logs or proof-of-concept material that does not contain secrets.

If private reporting is temporarily unavailable, avoid publishing exploit details until a private contact path is available.

## Security boundaries

DLSS NR Manager treats the following as security-sensitive:

- self-update and manager-owned release downloads;
- SHA-256 and GitHub release-digest validation;
- Authenticode validation of NVIDIA runtime binaries;
- ZIP/archive extraction and path traversal prevention;
- executable/DLL staging into games;
- Minecraft backup/restore ownership;
- AI model/runtime installation;
- local-only NVIDIA SDK material;
- subprocess execution (FFmpeg, Real-ESRGAN, Java/Fabric, WinGet, ComfyUI/Python where enabled);
- update and cleanup actions that mutate the local machine.

Restricted NVIDIA SDK/runtime material must not be committed to the public source tree unless its redistribution terms have been reviewed and explicitly allow it.

## Integrity expectations

Production downloads must use HTTPS and, where the asset is manager-owned, must be validated with a pinned hash, GitHub release digest, signed manifest, vendor signature, or an equivalent explicit integrity check before activation.

Downloaded archives must be extracted through bounded, traversal-safe extraction logic. A successful download is not considered trusted until integrity and path-safety checks complete.

## Scope exclusions

Reports about unsupported third-party software behavior, game bans caused by intentionally unsupported injection/modification, or issues that require disabling the project's integrity checks are not security vulnerabilities in DLSS NR Manager itself unless the manager creates the unsafe condition.
