# Binary Signing and Authenticity Policy

## Current trust model

DLSS NR Manager release integrity is anchored by:

- project-owned GitHub Releases;
- SHA-256 asset digests;
- `SHA256SUMS.txt`;
- component manifests;
- release provenance metadata.

The application executable is not to be described as Authenticode-signed unless the release workflow actually performs and verifies that signature.

## Third-party binaries

Where an upstream/vendor binary is expected to be signed, the project should verify the signature before promotion/use.

The existing NVIDIA runtime path requires valid Authenticode signatures and an NVIDIA signer for protected NVIDIA DLLs.

Do not reduce an existing signature check to a hash-only check without a documented reason.

## Project executable signing

If DLSS NR Manager gains Authenticode signing:

- the private key/certificate must never be committed;
- signing credentials must be stored in a protected signing service or GitHub secret/environment;
- signing should happen only after tests and before final release hashing;
- the final signed EXE/ZIP hashes must be the hashes published in the release;
- CI must verify the resulting signature and expected publisher identity;
- timestamping should use a trusted timestamp service.

A certificate rotation must not silently change publisher expectations in update verification.

## Unsigned builds

Development/PR builds may remain unsigned.

They must not be presented as signed production releases.

## Hashes and signatures

Signatures and hashes serve different purposes:

- signature: publisher/authenticity;
- SHA-256: byte identity/integrity.

Use both when available.

## Stable release immutability

A stable tag must not have an existing asset replaced with different bytes.

If a correction changes a stable artifact, publish a new application version.

## Restricted/vendor material

Authenticode validity does not itself grant redistribution rights.

Licensing and signature validation are independent requirements.
