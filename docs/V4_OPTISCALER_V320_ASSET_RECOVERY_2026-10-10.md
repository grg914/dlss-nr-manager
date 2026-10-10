# V4 legacy OptiScaler recovery — 2026-10-10

This V4 candidate is isolated from protected main and the V4.5/V4.6 streams.

## Original and immutable asset

Source release: https://github.com/grg914/dlss-nr-manager/releases/tag/v3.2.0

Original asset URL: https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip

Release ID 406116106; asset ID 620010799; 131421020 bytes; SHA-256 14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b.

The manifest manifests/optiscaler-v4-legacy-candidate.json pins all these identifiers, and tools/verify-optiscaler-legacy-asset.ps1 compares both the GitHub release metadata and actual downloaded bytes before inspecting ZIP entries and Win64 PE headers. No DLL is executed by this verification.

A Windows 2025 GitHub Actions workflow retrieves and verifies the original ZIP and keeps a seven-day artifact named verified-optiscaler-v3-2-0-original. From a checked-out branch, the same command can be run in Windows PowerShell:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-optiscaler-legacy-asset.ps1

No published asset is deleted, overwritten or republished. Candidate selection is NOT promoted to the application automatically; compatibility testing is required first. A prior release being available does not prove it works in a new V4 runtime.

## Acceptance before V4 use

Check the exact matching Win64 DLLs and license notices, then use a real Windows RTX system to test a supported non-Minecraft game, graphics/runtime loading, DLSS/FG, shutdown/rollback and resource cleanup. Confirm compatibility with the approved component policy and preserve the already-pinned fallback for rollback. Promotion requires separate reviewed/signed source changes and exact-head CI.

Keep native rebuild reproducibility issue #161 open: the current new-build OptiScaler.dll can differ between two MSVC runs. The original binary avoids *rebuilding* for this particular fallback but cannot solve that defect.
