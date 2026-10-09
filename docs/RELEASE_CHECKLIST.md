# Release Checklist

Use this checklist before promoting a production release.

## Application

- [ ] `DlssNrManager.csproj` version is correct.
- [ ] README release/version text is current.
- [ ] Required Build and CodeQL checks are green.
- [ ] Regression tests pass.
- [ ] Windows x64 single-file publish and executable/icon validation pass.
- [ ] On-device supported RTX Windows installation, normal/forced-exit recovery, locked-file/junction protections, FR/EN UI and rollback acceptance are evidenced (#95); CI surrogates do not substitute for physical validation.

## Integrity and provenance

- [ ] Deterministic application packaging passes.
- [ ] `SHA256SUMS.txt` covers all production release assets.
- [ ] `components-manifest.json` matches production assets.
- [ ] `SOURCE-SBOM.spdx.json` is generated from the current source/dependency state.
- [ ] `release-provenance.json` records the exact release source SHA.
- [ ] Runtime seed downloads validate GitHub SHA-256 digests.
- [ ] Restricted/native signer requirements pass where applicable.
- [ ] Stable release immutability checks pass.
- [ ] OptiScaler native DLL and ZIP reproducibility is assessed against two clean pinned-toolchain builds, with per-file SHA-256 and canonical immutable seed comparison; conflicting asset names are never overwritten (#161).
- [ ] Approved video2dlssnr and FFmpeg release/asset IDs and SHA-256 are pinned throughout the update transaction and committed receipt, with no mixed-release install (#111).
- [ ] Automated release source still equals current `main` immediately before publication.

## Licensing

- [ ] DLSS NR Manager `LICENSE` is present where required.
- [ ] Required third-party notices and upstream license files are preserved.
- [ ] Copyleft source-availability obligations are satisfied.
- [ ] Restricted SDK/model material is absent from public assets unless redistribution is explicitly permitted.
- [ ] Any bundled Microsoft OpenMP runtime comes only from permitted signed Visual Studio REDIST inputs, with version/hash/source/license authorization independently documented; otherwise Real-ESRGAN OpenMP packaging remains disabled (#79).

## Runtime/offline architecture

- [ ] Production components resolve from bundled content or manager-owned releases.
- [ ] No retired direct-upstream production fallback has reappeared.
- [ ] Runtime refresh rebuilt only components affected by the validated change.
- [ ] Unchanged content-addressed runtime assets are not replaced unnecessarily.

## UX and localization

- [ ] New user-visible strings have English and French mappings where the current UI requires localization.
- [ ] Error/status/confirmation paths were checked in both languages.
- [ ] Download/progress behavior is correct for changed downloadable components.

## Final publication

- [ ] Release target commit is the exact validated source SHA.
- [ ] Release assets/digests were re-read after publication where the workflow requires it.
- [ ] Release notes do not claim signing, licensing, or feature guarantees that were not actually validated.
