# v4.5 AI Studio — private runtime integrity gate (experimental)

Owner: **GPT C**, separate from GPT B v4.0 and GPT A Caustica/Minecraft.

## Scope

This P1 prerequisite is **offline and read-only**. It validates an externally pinned manifest and its enumerated runtime files. It does not download or install Python/PyTorch/CUDA/ComfyUI/Diffusers, launch any process, mutate global tools, or alter existing model installers.

## Trust boundary

The proposed local manifest lives under the existing private AI Studio runtime root at `%LOCALAPPDATA%\DlssNrManager\ai-studio\runtime\runtime-integrity.json`. The expected **manifest SHA-256 must be supplied externally by a reviewed manager-owned release/component manifest**, never from the candidate runtime itself. Nothing in this draft publishes or approves that external package.

Example **synthetic fixture only**, not an approved or published package:

```json
{"schema":1,"package_id":"ai-studio-runtime-win-x64","version":"0.0.0-fixture","files":[{"path":"python/python.exe","size":42,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
```

The verifier rejects absent/untrusted pins, wrong manifest hashes, malformed or oversized manifests, unknown package/schema, duplicate paths, absolute/traversal/drive paths, symlinks/junctions, missing files, size mismatch and per-file SHA-256 mismatch. Cancellation propagates.

**ManifestFilesVerifiedOnly** means only the listed file bytes matched a separately pinned manifest. It does **not** mean the whole directory is clean, the binary is signed/licensed, the runtime is safe to execute, or GPU/model inference is compatible. Unexpected extra files are not enumerated here; a future process executor must use a strict allowlist and separate provenance/runtime checks.

## Further approval gates (not implemented)

1. Review exact Python/PyTorch/CUDA/ComfyUI/Diffusers versions, licenses, sources, wheelhouse hashes, GPU/driver compatibility and redistributability.
2. Build non-overwriting, manager-owned immutable archive/provenance assets with authorized producer workflows; do not replace existing seed/release files or upload restricted inputs.
3. Feed the trusted SHA-256 from signed/approved manager-owned release metadata into this verifier. Route any installations through existing central Downloads and transactional rollback.
4. Enforce isolated private Python and package search paths, `PYTHONNOUSERSITE=1`, no surprise PyPI/global Python/CUDA modifications, loopback-only API, and audited ComfyUI nodes/workflows.
5. Require explicit license acceptance for restricted models, trusted executable path, job cancellation and process-tree ownership before starting inference.
6. Pass exact-head Build, Analyze C#, CodeQL, offline unit tests, Windows RTX/VRAM acceptance, hash/signature/license review, and signed protected PR checks before any release.

## Test contract

Synthetic fixtures cover missing trusted pin, missing or tampered manifest, modified file, malicious paths, duplicate path, wrong package ID and cancellation. No actual Python or NVIDIA runtime is run.

**Status: P1 groundwork only.** Does not alter v4.0 stabilization or Caustica experimental source.
