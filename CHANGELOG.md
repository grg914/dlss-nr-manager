# Changelog

All notable release changes should be recorded here. This file distinguishes **merged code** from **unreleased plans**. Source version and latest published GitHub release are separate facts.

## [Unreleased] — 4.0 readiness (not shipped)

### Merged and CI-validated changes (2026-10-08)
- PR #99: restrict NVIDIA-SMI execution to trusted installed locations and track helper processes.
- PR #100: unify media update rollback and conservative recovery of legacy media backups.
- PR #101: confine managed AI Studio installs to the selected model directory and add backup recovery allowlists.
- PR #102: protect pre-existing manually imported/licensed model data when initial backup move fails.
- PR #105: clean temporary AI Studio extraction staging on failure, and clarify FR/EN redownload rollback prompts.

### Outstanding release gates
- Complete real-device Windows crash/forced-kill/restart tests for component rollback, model backup recovery, subprocess shutdown, GPU/VRAM and disk cleanup.
- Audit junction/symlink parent paths, ambiguous historical backups and concurrency/file-lock cases.
- Validate all requirements in the user-provided prompt.txt: hardware profiles, driver-change refresh, RTX 20/30/40/50 feature gating, FR/EN text and tooltips, optional updates in Download Center, dependency availability/licensing and complete regression coverage.
- Verify actual packaged assets, reproducible Windows x64 build, installer/runtime and release workflow results.
- Resolve open release blockers and record final evidence in AI_PROJECT_PROGRESS.txt before tagging 4.0.

> **No 4.0 release is declared by this changelog.** Passing CI alone is not an end-to-end Windows hardware acceptance test.

## Source version 3.2.0

The version declared in `DlssNrManager.csproj` at the time this changelog was introduced is `3.2.0`. Consult GitHub Releases independently for the most recently published package; this section does not claim that a corresponding binary was published.

## Earlier releases

Historical release notes were not reconstructed here because individual past release contents have not been fully audited. See GitHub Releases and the existing project journal for evidence.
