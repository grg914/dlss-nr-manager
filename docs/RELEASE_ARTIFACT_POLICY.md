# Release Artifact Policy

## Purpose

DLSS NR Manager separates source code from large/runtime distribution artifacts.

Production releases should be reproducible, provenance-aware and independent of live third-party upstream availability.

## Repository vs GitHub Releases

Use Git history for:

- application/source code;
- scripts;
- workflows;
- manifests;
- small configuration;
- license and notice files;
- vendored source snapshots where appropriate.

Use project-owned GitHub Releases/runtime seed for:

- application executables/ZIPs;
- native runtimes;
- FFmpeg/media tools;
- JREs;
- VLC packages;
- AI runtimes;
- AI model weights when redistribution is permitted;
- other large binary assets.

Do not place large binary/model payloads into normal Git history.

## Production rule

Normal production Build/Release must consume only:

- repository content;
- manager-owned validated release assets;
- preinstalled trusted build tooling explicitly allowed by the workflow.

Third-party upstream network access belongs in controlled refresh/vendor workflows, not normal production promotion.

## Integrity

Manager-owned binary assets should provide SHA-256 through GitHub asset digest and/or project manifest.

Consumers must verify the expected digest before installation/use when the architecture supports it.

NVIDIA binaries that the project designates as signed inputs must also pass the expected Authenticode signer check.

## Provenance

For each promoted runtime/model asset, retain enough metadata to answer:

- what component is this;
- which exact source/version produced it;
- which repository/release/tag/commit is authoritative;
- what license applies;
- what SHA-256 identifies the promoted bytes;
- whether it was built from vendored source, copied from an official redistribution, or built with local-only restricted input.

## Restricted material

Never publish a raw restricted SDK merely to make the project "self-contained".

Self-contained production means end users receive permitted manager-owned runtime packages while restricted build inputs remain local/private.

## Large assets

Application package size is not artificially capped.

When the hosting platform imposes a per-asset limit, use deterministic chunks plus a manifest containing:

- total package size;
- ordered chunk names;
- chunk sizes;
- chunk SHA-256;
- reconstructed archive SHA-256.

The user must see the full reconstructed download size for approval/progress purposes.

## Downloads larger than 1 GiB

The application policy requires explicit user approval before starting a manager-owned download whose total package size exceeds 1 GiB.

Chunking must not bypass this requirement.

## Canonical install surface

Each managed component should have one canonical install/remove/redownload path.

Feature pages may show status and navigate to the canonical manager, but should not create duplicate hidden download paths.

## Release contents

A stable application release should include, as applicable:

- `DlssNrManager.exe`;
- application ZIP;
- `SHA256SUMS.txt`;
- `components-manifest.json`;
- release SBOM;
- release provenance metadata;
- required manager-owned runtimes/assets.

Not every large AI model must be attached to every application release; dedicated model/runtime releases are allowed.

## Immutability

Stable application releases should be treated as immutable.

If a published artifact must change, prefer a new release/version rather than silently replacing bytes under the same stable version.

Runtime-seed/prerelease channels may use controlled replacement only where the workflow explicitly validates the new digest and provenance.
