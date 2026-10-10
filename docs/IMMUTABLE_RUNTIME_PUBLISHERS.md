# Append-only runtime seed publication and promotion

Status: **partial issue #123 migration**. This document describes safe publisher behavior; it is NOT a certificate that all application consumers have migrated to immutable metadata or that hardware/licensing acceptance is complete.

## Producer / consumer contract

| Component | Canonical asset | Publisher collision policy | Promotion |
|---|---|---|---|
| NuGet offline feed | `nuget-offline.zip` | SHA-256-named revisions (already integrated; pinned by `manifests/runtime-seed-assets.json`) | Reviewed Git manifest change |
| FFmpeg | `ffmpeg-dlssnr-win-x64.zip` | Stage `*.sha256-<hash>.zip` (already integrated) | Consumer pin migration pending |
| OptiScaler / ReShade | Versioned ZIP names | Default reject on same-version changes; Native runtime CI may stage changed OptiScaler or ReShade rebuilds under a SHA-256-suffixed immutable name without replacing canonical | Explicit consumer pin review and rollback; both require reviewed promotion; other callers retain default reject |
| Streamline | `streamline-runtime-v*-win-x64.zip` | Deterministic build-twice check and SHA-addressed append-only staging (already integrated in PR #157) | Explicitly reviewed consumer pin; canonical unchanged |
| VLC runtime and source | `vlc-<version>-win64.zip`, `vlc-<version>.tar.xz` and matching provenance JSON | Reject same-version changed content | All three assets must be verified together |
| Temurin JRE | `temurin-25-jre-win-x64.zip` plus `temurin-25-jre.json` | Stage content-addressed ZIP and JSON while preserving canonical assets | Review both matching asset names, digests, Java version and manifest |
| Real-ESRGAN | `realesrgan-ncnn-vulkan-windows-x64.zip` | Stage content-addressed ZIP while preserving canonical | **OpenMP-free only; requires Windows Vulkan/RTX acceptance, native import audit and explicit runtime approval before immutable staging** |
| Manual Video2DLSSNR | `video2dlssnr_release.zip` | Separate staged append-only publisher (PR #142) | Pin and verify consumer manually |

## Universal safeguards

The shared `tools/publish-append-only-runtime-seed.ps1` checks reviewed filenames, local file structure (ZIP, tar.xz or provenance JSON), positive size and SHA-256, exact GitHub release asset matching, and **digest plus size after publication**. Missing/unverifiable metadata is an error. No release asset is deleted or replaced; concurrent writers trying the same filename must fail. Existing readers continue to select canonical assets until a separately reviewed update.

Releases may contain both old and staged assets. Their coexistence **does not** by itself constitute an atomic activation. Only promote a new version through an explicit, validated source-controlled consumer pin or version selection, ideally preserving a rollback path and recording source provenance. Do not select `latest` or a broad wildcard that might accidentally choose a staged content-addressed file.

## OptiScaler and ReShade same-version native rebuilds

The OptiScaler v0.7.7-pre0 rebuild in workflow run 37890352922 produced a package whose size or SHA-256 differed from the already published canonical ZIP. Source version equality does not establish reproducible native output. With `-OnChanged StageImmutable`, the native CI may upload an additional `OptiScaler-NR-<version>-vendored-win-x64.sha256-<sha256>.zip` asset **only** after verifying the canonical metadata; the existing asset is never replaced. The new artifact is **not promoted, installed, or selected by version wildcards**. The publisher logs the generated size and digest; a separate reproducibility investigation (#161) must compare compiled DLLs, archive metadata, and toolchains before closing that issue. The default publisher behavior remains fail-closed for all callers. The native runtime workflow explicitly opts in for both OptiScaler and ReShade packages.

## Legal and validation gates

The Real-ESRGAN producer is now configured without OpenMP or a `vcomp140.dll` redistribution path. Staging a replacement runtime is gated by `DLSSNR_REALESRGAN_RUNTIME_APPROVED == '1'`, to be set only after successful x2/x4 Windows Vulkan/RTX hardware acceptance, PE import audit, licensed-source notices and exact SHA-256 evidence. **Do not enable this flag automatically**. Old canonical assets remain immutable; consumer promotion is separately reviewed.

Required acceptance before closing #123: repeat publish with identical bytes; changed bytes with an occupied canonical name; missing digest; wrong digest; network/upload failure; races; rollback to previous pinned revision; verified Windows runtime install and recovery; and real target GPU tests where applicable. Separate issues #79 and #95 must remain open until their external evidence exists.

This work protects the publishing infrastructure; it does not automatically authorize new source mirrors, models or proprietary NVIDIA redistribution.
