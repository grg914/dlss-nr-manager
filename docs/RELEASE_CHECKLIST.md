# Release Checklist

Use this checklist before publishing or promoting a production release.

## Application

- [ ] Version in `DlssNrManager.csproj` is correct.
- [ ] README stable-version text is current.
- [ ] Regression tests pass with warnings as errors.
- [ ] Windows x64 publish succeeds.
- [ ] Application self-update path points only to manager-owned release assets.

## Integrity

- [ ] `SHA256SUMS.txt` covers every production release asset.
- [ ] `components-manifest.json` matches release assets.
- [ ] `SOURCE-SBOM.spdx.json` was generated from the current dependency lock.
- [ ] `release-provenance.json` references the current version/commit and hashes the dependency lock, component manifest and SBOM.
- [ ] Manager-owned runtime assets have GitHub SHA-256 digests.
- [ ] NVIDIA native runtime files pass the required signer checks.
- [ ] Application Authenticode signing state matches `docs/RELEASE_SIGNING_PROVENANCE_POLICY.md`; if unsigned, no release note or UI implies otherwise.
- [ ] Archive extraction uses safe traversal/size protections.

## Licensing / notices

- [ ] Root `LICENSE` is included in the downloadable ZIP.
- [ ] `THIRD_PARTY_NOTICES.md` is included in the downloadable ZIP/release.
- [ ] Copyleft/runtime source-offer obligations are satisfied.
- [ ] Restricted NVIDIA SDK material is absent from public assets.
- [ ] Restricted/gated AI model weights are not mirrored unless redistribution is explicitly allowed.
- [ ] VLC corresponding source is present whenever VLC runtime is distributed.

## Runtime/offline

- [ ] Manager-owned components resolve from DLSS NR Manager releases.
- [ ] No retired third-party runtime fallback has reappeared.
- [ ] Installed components can run offline where documented.
- [ ] Shared downloads are manageable through the central Downloads surface.
- [ ] >1 GiB downloads require explicit user approval before transfer.

## UX/localization

- [ ] New user-visible strings are present in English and French.
- [ ] Error/status/confirmation paths were checked in both languages.
- [ ] Download progress/ETA works for new downloadable components.

## Feature-specific

- [ ] Games & DLSS / OptiScaler restore path validated.
- [ ] Minecraft RTX/Caustica/runtime bundle validated.
- [ ] Media Neural/FFmpeg/video2dlssnr validated.
- [ ] Real-ESRGAN models/engine validated.
- [ ] AI-origin detector model hashes validated.
- [ ] Streamline/NVIDIA runtime audit passes.
- [ ] VSR-HDR/VLC runtime + corresponding source validated if included.
- [ ] AI Studio runtime/model package manifests validated if included.

