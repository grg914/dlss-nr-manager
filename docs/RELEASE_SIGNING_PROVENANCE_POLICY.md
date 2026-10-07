# Release Signing and Provenance Policy

## Current state

DLSS NR Manager production releases publish:

- `DlssNrManager.exe`;
- the Windows x64 application ZIP;
- `SHA256SUMS.txt`;
- `components-manifest.json`;
- `SOURCE-SBOM.spdx.json`;
- `release-provenance.json`;
- manager-owned runtime/source assets.

At the time this policy was added, the project release workflow does **not** Authenticode-sign `DlssNrManager.exe` with a project code-signing certificate.

This is separate from NVIDIA runtime validation: NVIDIA DLLs may be required to carry a valid NVIDIA Authenticode signature even though the DLSS NR Manager executable itself is not project-signed.

## Verification without project code signing

Until project Authenticode signing is configured, users and maintainers should verify:

1. the asset comes from the official `grg914/dlss-nr-manager` GitHub Release;
2. the asset SHA-256 matches `SHA256SUMS.txt`;
3. `release-provenance.json` identifies the expected version/commit;
4. `SOURCE-SBOM.spdx.json` and the dependency lock identify the expected source dependency set;
5. component-specific signature/hash policies pass.

## SBOM scope

`SOURCE-SBOM.spdx.json` is a source/dependency SBOM generated from the repository dependency lock plus the application package identity.

It does not assert that every dependency uses a simple SPDX license expression; where licensing is complex or separately governed, the SBOM records `NOASSERTION` and the repository policy/notice files remain authoritative.

## Release provenance

`release-provenance.json` records:

- repository;
- version;
- commit SHA;
- generation time;
- dependency-lock SHA-256;
- component-manifest SHA-256;
- source-SBOM SHA-256;
- current project-signing state.

It is itself included in `SHA256SUMS.txt`.

## Future Authenticode signing

When a project code-signing certificate is introduced:

- never commit the certificate private key/PFX/password;
- store signing material in an appropriate protected secret/environment or external signing service;
- restrict signing to trusted release jobs/environments;
- sign the final executable before creating the application ZIP and before generating final SHA-256 sums;
- timestamp the signature using a trusted timestamp service;
- verify the signature after signing;
- document the expected publisher/subject in this policy;
- rotate/revoke credentials if compromise is suspected.

A certificate change should be treated as a security-sensitive release event.

## Artifact attestations

GitHub artifact attestations or equivalent signed provenance may be added later as an additional layer. They should complement, not replace, SHA-256, source locks, component manifests and license/source obligations.

