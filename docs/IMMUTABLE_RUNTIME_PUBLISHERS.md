# Append-only runtime seed publication and promotion

Status: **partial issue #123 migration**. This document describes safe publisher behavior; it is NOT a certificate that all application consumers have migrated to immutable metadata or that hardware/licensing acceptance is complete.

## Producer / consumer contract

| Component | Canonical asset | Publisher collision policy | Promotion |
|---|---|---|---|
| NuGet offline feed | `nuget-offline.zip` | SHA-256-named revisions (already integrated; pinned by `manifests/runtime-seed-assets.json`) | Reviewed Git manifest change |
| FFmpeg | `ffmpeg-dlssnr-win-x64.zip` | Stage `*.sha256-<hash>.zip` (already integrated) | Consumer pin migration pending |
| OptiScaler / ReShade | Versioned ZIP names | Reject same-version changed content (already integrated) | New reviewed versioned filename |
| Streamline | `streamline-runtime-v*-win-x64.zip` | Deterministic build-twice check and SHA-addressed append-only staging (already integrated in PR #157) | Explicitly reviewed consumer pin; canonical unchanged |
| VLC runtime and source | `vlc-<version>-win64.zip`, `vlc-<version>.tar.xz` and matching provenance JSON | Reject same-version changed content | All three assets must be verified together |
| Temurin JRE | `temurin-25-jre-win-x64.zip` plus `temurin-25-jre.json` | Stage content-addressed ZIP and JSON while preserving canonical assets | Review both matching asset names, digests, Java version and manifest |
| Real-ESRGAN | `realesrgan-ncnn-vulkan-windows-x64.zip` | Stage content-addressed ZIP while preserving canonical | **Only when #79 Visual Studio OpenMP redistribution rights and Authenticode provenance are explicitly approved** |
| Manual Video2DLSSNR | `video2dlssnr_release.zip` | Separate staged append-only publisher (PR #142) | Pin and verify consumer manually |

## Universal safeguards

The shared `tools/publish-append-only-runtime-seed.ps1` checks reviewed filenames, local file structure (ZIP, tar.xz or provenance JSON), positive size and SHA-256, exact GitHub release asset matching, and **digest plus size after publication**. Missing/unverifiable metadata is an error. No release asset is deleted or replaced; concurrent writers trying the same filename must fail. Existing readers continue to select canonical assets until a separately reviewed update.

Releases may contain both old and staged assets. Their coexistence **does not** by itself constitute an atomic activation. Only promote a new version through an explicit, validated source-controlled consumer pin or version selection, ideally preserving a rollback path and recording source provenance. Do not select `latest` or a broad wildcard that might accidentally choose a staged content-addressed file.

## Legal and validation gates

The Real-ESRGAN refresh job remains gated by `DLSSNR_OPENMP_REDIST_APPROVED == '1'`. The operator must establish lawful Microsoft Visual Studio REDIST rights and a verified source DLL. **Do not enable this flag automatically**. A no-OpenMP experimental build cannot be substituted without independent runtime/PE import and quality testing.

Required acceptance before closing #123: repeat publish with identical bytes; changed bytes with an occupied canonical name; missing digest; wrong digest; network/upload failure; races; rollback to previous pinned revision; verified Windows runtime install and recovery; and real target GPU tests where applicable. Separate issues #79 and #95 must remain open until their external evidence exists.

This work protects the publishing infrastructure; it does not automatically authorize new source mirrors, models or proprietary NVIDIA redistribution.
