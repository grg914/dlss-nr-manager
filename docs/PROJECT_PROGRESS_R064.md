# R064 — Runtime seed producer serialization (2026-10-09)

## Problem / evidence
Two workflows, `.github/workflows/runtime-refresh.yml` and `.github/workflows/nuget-seed.yml`, were writing the same mutable `runtime-seed-v1/nuget-offline.zip` asset with `--clobber` but had different concurrency groups. The refresh workflow also used `cancel-in-progress: true`, potentially interrupting a publish.

## Proposed code
- Place both publishers in the same repository-wide concurrency group `runtime-seed-v1-writers` and disable in-progress cancellation.
- On the secondary NuGet publisher, calculate local SHA-256 and length; read GitHub release metadata; skip `--clobber` if the published asset has the identical digest and size.
- Reject a pre-existing asset without a GitHub digest rather than silently replacing unverified bytes.
- On a genuinely changed asset, upload the currently required legacy name only after verification, then verify the new GitHub digest and length; no untrusted substitute.
- Add 3 source-level unit tests for group policy, safety gates and publishing order.

## Remaining #123 blocker
These measures eliminate one known parallel writer/cancellation hazard and avoid identical uploads. **They do not make a changed-name asset replacement atomic.** A complete solution still needs immutable versioned assets and manifest-pointer promotion with all consumers migrated, plus real release failure-injection tests. Keep issue #123 open and do not claim v4 readiness.

## Coordination
Do not merge before Build and CodeQL success on exact PR head. Root AI_PROJECT_PROGRESS.txt checkpoint R064 is prepended without overwriting prior entries.
