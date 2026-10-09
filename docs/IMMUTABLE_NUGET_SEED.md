# Immutable NuGet runtime seed staging and promotion

Issue: #123. Runtime release: `runtime-seed-v1`.

## Policy

The existing `nuget-offline.zip` asset remains available to existing builds. The two NuGet producers share the `runtime-seed-v1-writers` concurrency group and use `tools/publish-immutable-nuget-seed.ps1` rather than `gh release upload --clobber`. New content is uploaded once as `nuget-offline.sha256-<64-character-sha256>.zip`, and GitHub's reported size and SHA-256 digest are verified after upload. Conflicting content or invalid ZIP structure is rejected.

The **source-controlled promotion pointer** is `manifests/runtime-seed-assets.json`. `tools/prepare-offline-dotnet.ps1` reads the selected name, release tag, size and SHA-256 from that manifest, invokes the existing GitHub release download integrity validation, verifies the pinned file again and only then extracts the package feed. This prevents a publisher from silently changing which NuGet packages a Build uses.

## Promoting a new immutable revision

1. Run the seed producer with the common non-destructive publisher and record its `nuget-offline.sha256-<hash>.zip` name.
2. Inspect the release asset metadata through the authenticated GitHub API. Require exactly one asset with the expected name, non-zero size and `sha256:<64 hex>` digest. Verify downloaded ZIP content and inner manifest/package digests on a clean Windows runner.
3. Open a PR changing only the `nuget_offline.asset_name`, `size` and `sha256` in the Git-tracked pin manifest. For a content-addressed name, the name suffix MUST equal the `sha256` field.
4. Require the complete Build, CodeQL and Runtime Seed Writer Safety Tests on the **final PR head**. Merge only after successful CI and review. The Git commit becomes the atomic pointer promotion; no live release asset is deleted.
5. Retain previous revisions for rollback and for builds referencing an older commit. Roll back by reverting the pinned manifest commit.

**Do not edit the mutable canonical asset in place or delete any immutable revision while consumers reference it.** Do not promote unapproved restricted-license payloads automatically.

## Scope / remaining work

This initial migration applies only to the **NuGet offline seed**. Real-ESRGAN, FFmpeg and other runtime publisher paths must be migrated separately while preserving their downstream manifest/asset contracts. The PR and CI simulation tests cannot establish actual Windows/NVIDIA performance, power-failure recovery, or Microsoft OpenMP redistribution rights. Issue #123 and v4 release gates remain open until end-to-end validation is complete.
