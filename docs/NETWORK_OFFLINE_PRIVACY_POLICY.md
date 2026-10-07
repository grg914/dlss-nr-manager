# Network, Offline and Privacy Policy

## Principles

DLSS NR Manager is designed around a manager-owned runtime channel for redistributable components, while allowing intentionally external network access for features whose purpose is to discover updates, artwork, official vendor information, or manually licensed content.

The application is not designed to upload user media, prompts, AI Studio jobs, game files, generated output, or usage telemetry to a DLSS NR Manager backend.

## Manager-owned network traffic

The following feature families should resolve redistributable runtime assets from `grg914/dlss-nr-manager` GitHub Releases:

- application self-update;
- Media Neural / video2dlssnr;
- FFmpeg media runtime;
- Real-ESRGAN engine and manager-owned models;
- AI-origin ONNX models;
- OptiScaler manager-owned runtime;
- ReShade manager-owned runtime;
- NVIDIA Streamline / validated redistributable runtime bundles;
- Minecraft manager-owned runtime bundles;
- Temurin runtime when packaged by the project;
- AI Studio packages that are legally redistributable;
- VLC runtime/source packages when the VSR-HDR feature is shipped.

Runtime downloads should use HTTPS and integrity verification according to the component policy.

## Intentionally external network traffic

Some features intentionally contact external services or open external vendor pages.

### PC Update Center

PC Update may use or open official/vendor resources such as:

- Microsoft / WinGet;
- NVIDIA;
- AMD;
- Intel;
- Realtek;
- OEM support pages;
- application vendor download pages.

These are update/discovery/navigation functions, not manager-owned runtime mirrors.

### Game artwork / metadata

Game artwork and catalog metadata may contact Steam/Epic endpoints and CDNs to resolve game imagery or catalog information.

### Minecraft prerequisites

When Minecraft itself is missing, the application may direct the user to the official Minecraft download page. Manager-owned Minecraft integration/runtime assets remain separate from the Minecraft product itself.

### Manual/gated AI models

Restricted/gated models are not silently mirrored into manager-owned releases. The user must accept the applicable license and obtain the model through the official source when the policy requires manual acquisition.

### CI/upstream monitoring

GitHub Actions used for source maintenance may contact upstream GitHub projects and other source hosts defined in `third_party/UPSTREAMS.json`. Those refresh paths are development/maintenance paths, not production runtime fallbacks.

## Offline behavior

"Offline-ready" means a previously installed component can run without fetching it again.

It does not mean every feature can discover new updates/artwork while offline.

Examples:

- already-installed Media Neural/Real-ESRGAN/AI models should run offline;
- previously installed manager-owned VLC should run offline;
- AI Studio jobs should run offline when the isolated runtime and selected model are fully cached;
- PC Update discovery and remote artwork refresh naturally require network access.

## Local data

Local state may include:

- application preferences;
- game discovery/preferences/history;
- logs;
- managed-install manifests and hashes;
- downloaded runtime components;
- AI Studio models, workflows, jobs and generated outputs;
- locally recorded model-license acceptance;
- cached artwork/catalog metadata.

Support bundles are user-initiated. Existing support-bundle logic sanitizes known user-profile paths before export; creating a support bundle does not itself upload the bundle anywhere.

## No hidden component downloads

Feature pages should not silently install missing manager-owned components.

Install, remove, redownload and repair operations for shared downloadable components belong in the centralized **Downloads** surface.

If a feature requires a missing component, it should direct the user to Downloads instead of starting an implicit dependency download.

## Large downloads

When a manager-owned package exceeds 1 GiB in complete/reconstructed size, the application must request explicit user approval before the first payload byte is transferred.

Transport chunking must not be used to bypass that consent threshold.

## Deletion

Deleting a manager-owned downloadable component removes its local managed copy, not the source GitHub Release asset. The user may reinstall it later through Downloads.

User-owned media and model-source folders selected for import must not be deleted as part of removing the managed copy.

