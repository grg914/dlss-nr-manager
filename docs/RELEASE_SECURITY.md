# Release Security and Provenance

A production release is trusted only when source revision, dependency provenance, tests, runtime assets and final file hashes are all consistent.

## Required release controls

- release revision must match the intended tag/project version;
- dependency locks use immutable refs;
- regression tests run with warnings treated as errors;
- production restore uses the manager-owned offline NuGet feed;
- manager-owned runtime seed assets are checked by GitHub digest and/or project-specific validation;
- NVIDIA proprietary runtimes require expected architecture/signature validation where applicable;
- Caustica/SPBR/Minecraft assets are revalidated before upload;
- `components-manifest.json` describes trusted manager-owned components;
- `SHA256SUMS.txt` covers published release files;
- `release-provenance.json` records repository, source commit, version, and final asset hashes.

## Build inputs that remain external

A zero-external-toolchain build is not claimed. Hosted runner OS/toolchains, Windows SDK/.NET, MinGW/NASM, Java/MSVC/Vulkan tooling, GPU drivers and license-restricted SDK inputs may remain external build prerequisites.

The goal is that normal end users consume validated manager-owned release assets rather than live upstream build/download feeds.

## Release mutation

Stable release assets should be treated as immutable after publication except for an explicit repair procedure. If an asset must be replaced, regenerate checksums/provenance and document the reason.

Runtime seed releases are staging channels and may be updated by controlled promotion workflows; final application releases remain the audit boundary users should verify.
