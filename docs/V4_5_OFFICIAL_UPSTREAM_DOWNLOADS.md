# v4.5 — Optional official GitHub asset download (GPT C)

## Decision: no forks required

AI Studio source monitoring already exists in the **Téléchargements → Versions officielles des outils** section. ComfyUI, Diffusers, FLUX.2 and Wan2.2 remain pinned in the dependency lock and are compared to official GitHub revisions **on explicit user request** only. A changed HEAD commit means "source changed; review required", not an installable, compatible, or newer runtime. No background network traffic and no automatic promotion.

## Optional ComfyUI portable acquisition

The ComfyUI entry gains an opt-in **Télécharger ComfyUI officiel** button after an explicit release metadata lookup. Only the official `Comfy-Org/ComfyUI` stable GitHub release asset named `ComfyUI_windows_portable_nvidia.7z` may be downloaded. The UI displays exact version and total size before download. Files over 1 GiB require an additional Yes/No approval via the existing `LargeDownloadApprovalHub`. No other bundled source or custom node is downloaded.

The downloader:
- gets release metadata from the official GitHub REST API; requires a stable `vX.Y.Z` tag, exact release-asset HTTPS URL, positive size below GitHub's per-asset limit, and `sha256:` digest;
- restricts redirects to GitHub's known HTTPS release asset/CDN domains;
- uses the existing global `DownloadProgressHub`, cancellation and bounded streaming;
- checks exact download length and SHA-256 before atomic, non-overwriting move into `%LOCALAPPDATA%\DlssNrManager\ai-studio\downloads\official-comfyui\<tag>\`;
- refuses reparse-point redirects, unexpected existing files, wrong digests, missing metadata and partial downloads;
- does **not** extract or execute Python, modify global Python/CUDA, install new custom nodes or replace the currently trusted runtime.

**Critical distinction:** official-source identity and matching bytes are not proof of redistribution rights, reviewed dependencies or model/GPU compatibility. The staged file stays **unapproved** until the separate manifest, licenses, full closure review, private runtime verifier and Windows RTX acceptance required by issue #285. Draft staging releases are not public updater feeds. The existing **manager-owned package installer** remains the only pathway for installing approved AI Studio model packages and reversible runtime upgrades. A remotely discovered source SHA must never bypass that boundary.

## Other AI projects

- `huggingface/diffusers`: code source, not a standalone Windows NVIDIA application. No direct installation button.
- `black-forest-labs/flux2`: reference implementation, not a complete model weight package. Models must follow approved Hugging Face weight/manifests and license policy.
- `Wan-Video/Wan2.2`: source plus separately hosted weights, GPU-specific workflows and performance gates. Source revisions can be reviewed from the existing GitHub link; no unverified package install.

## Required before enabling AI inference or an offline bundle

1. Approve immutable provenance, package dependencies, redistribution licenses and weight SHA-256.
2. Build/publish a versioned manager-owned approved runtime and complete model pack with transactional rollback.
3. Wire selected jobs to a verified manager-owned process with strict loopback and workflow policy.
4. Validate disconnected Windows RTX hardware; a previously installed NVIDIA driver is a host prerequisite.
5. Keep an optional **offline full-bundle installer** for users with no Internet at installation time; optional Downloads must never be the only way to install AI Studio.

This change affects only GPT C experimental v4.5; do not merge to protected `main` or the GPT B stable v4.0 lane without explicit approval.
