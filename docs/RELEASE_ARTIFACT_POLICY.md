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
- deterministic application ZIP containing the executable plus `README.md`, the project `LICENSE`, third-party notices and the data/privacy notice;
- `SHA256SUMS.txt`;
- `components-manifest.json`;
- release SBOM;
- release provenance metadata;
- required manager-owned runtimes/assets.

Not every large AI model must be attached to every application release; dedicated model/runtime releases are allowed.

## Reproducibility

Release metadata and packaging must be reproducible for the same source commit.

The release workflow therefore uses the source commit timestamp for:

- application ZIP entry timestamps;
- `components-manifest.json`;
- the SPDX SBOM creation timestamp;
- release provenance metadata.

The application ZIP is generated through `tools/create-application-package.ps1`. Build CI creates it twice from the same published executable and requires the two SHA-256 values to match.

The release SBOM must ingest `components-manifest.json` so it describes the manager-owned runtime assets actually promoted, not only source dependencies.

## Immutability

Stable application releases are immutable.

Rules:

- a stable tag/version belongs to one exact source commit;
- a manual/tag release refuses to reuse that version from another commit;
- an existing release asset with identical SHA-256 is skipped;
- an existing release asset with different bytes causes the release to fail;
- changing stable bytes requires a new application version.

`runtime-seed-v1` remains a mutable prerelease/staging channel because it is not the stable application release.

Runtime Seed Refresh may refresh that seed independently. Before automatically dispatching the stable Release workflow, it resolves the current application version and stable tag:

- tag absent: release dispatch is allowed;
- tag already points to the same source commit: an idempotent rerun is allowed;
- tag points to another commit: stable release dispatch is skipped until the application version is bumped.

This prevents a runtime/tooling refresh from silently rewriting an already published stable version.

## Exact release source pinning

Automated runtime-seed promotion must not rely on a floating `main` revision after it has validated a specific commit.

Policy:

- Runtime Seed Refresh passes the exact validated 40-character `source_sha` into the Release workflow.
- The dispatch helper re-reads `main` immediately before dispatch and skips a stale source if `main` has advanced.
- A manually dispatched Release with no explicit `source_sha` uses the event's exact `GITHUB_SHA`.
- Release checks out the resolved exact SHA, not a floating branch ref.
- For `workflow_dispatch`, the resolved SHA must still equal the current `main` HEAD when the release starts.
- Release rechecks `main` immediately before stable publication and aborts if the branch advanced while the build was running.
- Application package timestamps, SBOM source commit, release provenance and the GitHub release target all use the same resolved SHA.

The release process is therefore fail-closed: a superseded commit may be rebuilt for diagnostics, but it must not be promoted as a stable release after `main` has moved.
