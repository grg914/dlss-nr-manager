# AI Agent Instructions

Scope: the entire repository. Follow `SECURITY.md`, `CONTRIBUTING.md`, and `docs/GITHUB_REPOSITORY_SETTINGS.md`.

## Workflow
- Work on a dedicated branch and open a focused pull request. Do not push directly to `main`, bypass rulesets, force-push shared branches, or publish releases unless explicitly authorized.
- Before editing, inspect the relevant current implementation, tests, `README.md`, and latest entries in `AI_PROJECT_PROGRESS.txt`. Preserve changes from other active branches; do not overwrite unrelated work.
- Summarize changed files, rationale, verification and failures, dependency/license implications, and rollback considerations. Never claim a test passed unless its exact run succeeded.
- Update `AI_PROJECT_PROGRESS.txt` with a concise, factual checkpoint for substantive work; keep existing history intact. Do not invent completed milestones.
- Keep changes minimal; avoid duplicate controls, download entry points, or legacy compatibility layers without an explicit requirement.

## Security and dependency boundaries
- Do not commit secrets, vendor-restricted SDK contents, private signing materials, or machine-specific data.
- Respect the manager-owned/offline, zero-upstream production dependency policy; do not run online package upgrades or change locked inputs as a side effect of dependency alerts.
- New or updated third-party inputs require immutable references, source/provenance and license review, lock/manifest consistency, and existing integrity verification.
- Treat changes to `.github/workflows/`, `tools/`, runtime/update/download services, installers, archives, and releases as security-sensitive. Do not weaken hash/signature checks, overwrite immutable release assets, or add hidden downloads.
- Use the existing Build and CodeQL CI; obtain passing required checks on the final PR SHA before proposing merge. Do not bypass failed checks.

## Application quality
- Preserve the centralized component download/update experience and the FR/EN localization requirements.
- Validate the actual Windows/.NET WPF build when relevant; never equate static inspection with physical RTX/NVIDIA acceptance.
- External PRs and AI suggestions are untrusted until reviewed and validated.

Repository settings such as branch rulesets, Dependabot alerts, private vulnerability reporting, and secret protection must be configured and verified by the owner in GitHub Settings; this file does not enforce those permissions.
