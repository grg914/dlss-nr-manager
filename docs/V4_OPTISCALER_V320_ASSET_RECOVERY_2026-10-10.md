# V4 legacy OptiScaler recovery — 2026-10-10

This V4 candidate is isolated from protected main and the V4.5/V4.6 streams.

## Original and immutable asset

Source release: https://github.com/grg914/dlss-nr-manager/releases/tag/v3.2.0

Original asset URL: https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip

Release ID 406116106; asset ID 620010799; 131421020 bytes; SHA-256 14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b.

The manifest manifests/optiscaler-v4-legacy-candidate.json pins all these identifiers, and tools/verify-optiscaler-legacy-asset.ps1 compares both the GitHub release metadata and actual downloaded bytes before inspecting ZIP entries and Win64 PE headers. No DLL is executed by this verification.

A Windows 2025 GitHub Actions workflow retrieves and verifies the original ZIP and keeps a seven-day artifact named verified-optiscaler-v3-2-0-original. From a checked-out branch, the same command can be run in Windows PowerShell:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-optiscaler-legacy-asset.ps1

No published asset is deleted, overwritten or republished. The Stable channel offers the precisely pinned original asset as a manually confirmed TRIAL only; the application never auto-installs it. Physical RTX/game compatibility and rollback testing are required before V4 publication.

## Acceptance before V4 use

Check the exact matching Win64 DLLs and license notices, then use a real Windows RTX system to test a supported non-Minecraft game, graphics/runtime loading, DLSS/FG, shutdown/rollback and resource cleanup. Confirm compatibility with the approved component policy and preserve the already-pinned fallback for rollback. Promotion requires separate reviewed/signed source changes and exact-head CI.

Keep native rebuild reproducibility issue #161 open: the current new-build OptiScaler.dll can differ between two MSVC runs. The original binary avoids *rebuilding* for this particular fallback but cannot solve that defect.

## V4 stable-candidate integration (trial mode only)

The updated `Services/OptiScalerLegacyReleasePolicy.cs` requires the exact original v3.2.0 GitHub release ID **406116106**, asset ID **620010799**, filename, URL, published length **131421020** and SHA-256 **14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b**. The ordinary stable release selector now offers only that metadata-verified original. It must not select a different archive merely because it shares the `v0.7.7-pre0` label. Other opt-in preview packages remain available through the preview channel.

Before installing, the UI presents the SHA-256 and warns that the original legacy binary has NOT yet been validated on the selected game's RTX device; the operator must confirm. The existing `InstallerService` transaction first saves the previous installation, downloads to staging, verifies SHA-256 and additionally checks the original ZIP's exact byte length, extracts safely, and commits or rolls back according to its existing transaction journal. `InstallManifest.SourceArchiveUrl` and `SourceArchiveSha256` persist the exact selected provenance; previously saved manifests deserialize with these optional fields absent. The game history includes source SHA-256 for diagnosis.

The new `OptiScalerLegacyReleasePolicyTests` cover acceptance of the exact original and rejection of altered release/asset identity, source tag, filename, URL, digest, byte length, prerelease/draft status and malformed metadata. **This is a source-level trial route only:** GitHub CI cannot certify Vulkan/DLSS/FG behavior on the user's RTX card. Do not close #95/#161, publish V4 or merge unsigned commits until hardware tests, code signing and protected checks are satisfied.

### Verified source ZIP receipt

CI Windows run [38037938511](https://github.com/grg914/dlss-nr-manager/actions/runs/38037938511) downloaded and verified the original archive: 24 ZIP entries, two valid Win64 DLL PE headers, exact size/SHA. The recovered [temporary artifact #11664800784](https://github.com/grg914/dlss-nr-manager/actions/runs/38037938511/artifacts/11664800784) expires 2026-10-17 08:28 UTC; the permanent original remains [v3.2.0 release](https://github.com/grg914/dlss-nr-manager/releases/tag/v3.2.0).

