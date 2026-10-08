# Privacy and Data Handling

## Current product behavior

DLSS NR Manager is a local Windows application. The current codebase does not implement first-party analytics, advertising telemetry, Application Insights, Sentry, or an automatic crash-report upload service.

Local features may read:

- installed game/application paths;
- GPU/driver and Windows update metadata;
- selected images/videos;
- Minecraft instance files;
- local caches/logs;
- hashes and metadata needed for integrity/provenance checks.

Media/AI processing is intended to run locally unless a feature explicitly performs a download/update operation.

## Network access

Normal network activity is limited to feature-specific update/download operations such as:

- DLSS NR Manager's own GitHub releases/runtime seed;
- GitHub API calls needed to discover manager-owned assets;
- WinGet / Windows Update / official driver links when the user uses update features;
- explicitly approved upstream/bootstrap operations used for development or vendoring.

Production runtime should not contact upstream project release feeds when an equivalent manager-owned asset is part of the self-contained architecture.

## Logs

Diagnostic logs are stored under:

`%LOCALAPPDATA%\DlssNrManager\logs\`

Logs may contain application version, OS/framework information, GPU/runtime detection, operation results, sanitized paths and exception details. Users should review logs/support bundles before sharing them publicly.

## AI Studio and media files

Selected media and AI Studio inputs/outputs remain local unless the user explicitly chooses a network-backed folder or initiates a download/import action. License acceptance for gated AI models is stored locally and does not itself transmit acceptance to model vendors.

## Secrets

The application must not write GitHub tokens, passwords, OAuth secrets, private SDK credentials, or access tokens into normal logs, support bundles, manifests, or release artifacts.

Any future telemetry or remote processing feature must be opt-in, documented here before release, and clearly separated from required update/download traffic.
