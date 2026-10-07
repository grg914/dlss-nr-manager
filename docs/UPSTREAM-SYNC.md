# Trusted upstream synchronization and zero-upstream production

DLSS NR Manager uses a strict two-circuit dependency model.

## Production circuit

Normal `Build` and `Release` workflows are designed to operate without contacting third-party dependency repositories or package feeds.

Allowed production inputs are:

- the current `grg914/dlss-nr-manager` repository;
- Git LFS objects owned by this repository;
- manager-owned assets from the `runtime-seed-v1` prerelease;
- manager-owned assets from previous stable DLSS NR Manager releases when a restricted component cannot be rebuilt publicly;
- toolchains already provisioned on the GitHub runner.

`build.yml` and `release.yml` intentionally do not use `actions/checkout`, `actions/setup-dotnet`, artifact actions, APT, WinGet installation, NuGet.org, Modrinth, Fabric Maven/Meta, NVIDIA upstream releases, Hugging Face, ReShade upstream, OptiScaler upstream, or other dependency hosts.

The normal .NET restore uses `nuget-offline.zip` from `runtime-seed-v1` and a generated NuGet configuration containing only the extracted local feed.

## Refresh circuit

External dependency access is isolated to refresh workflows.

`.github/workflows/upstream-monitor.yml` runs daily and may also be started manually. Policies are defined in `third_party/UPSTREAMS.json`.

For an automatically promotable dependency the monitor:

1. resolves a stable upstream release/tag/branch according to policy;
2. resolves it to an immutable commit and tree SHA;
3. updates `third_party/DEPENDENCIES.lock.json`;
4. vendors the exact source snapshot;
5. updates linked managed package versions when configured, such as Microsoft.ML.OnnxRuntime;
6. refreshes Minecraft runtime locks when applicable;
7. increments the application patch version;
8. opens an automation PR;
9. validates Windows/Linux builds and compatibility gates;
10. merges only after validation;
11. dispatches the runtime seed refresh;
12. dispatches production release only after manager-owned seed promotion succeeds.

GitHub tag discovery is paginated so repositories with more than 100 recent tags, including FFmpeg, do not lose stable tags outside the first API page.

## Restricted and quality-gated components

Some upstream changes are detection-only.

- NVIDIA DLSS SDK: license-restricted input.
- video2dlssnr: a usable native rebuild needs local NVIDIA SDK/Neural Rendering runtime inputs.
- Caustica RTX: its native/runtime bundle also requires restricted NVIDIA SDK inputs.
- Real-ESRGAN model-source repositories: runtime weights remain fingerprint-pinned until quality/regression validation approves a change.
- Hugging Face AI-origin models remain manually promoted immutable snapshots.

The monitor creates or maintains a GitHub issue for `notify-only` updates rather than silently publishing unusable or unvalidated binaries.

## Runtime seed

`runtime-seed-v1` is a prerelease and is the manager-owned supply cache. It must never become the latest application release.

Expected manager-owned assets include:

- `video2dlssnr_release.zip`;
- `streamline-runtime-v*-win-x64.zip`;
- `minecraft-runtime-26.2.zip`;
- `ffmpeg-dlssnr-win-x64.zip`;
- `realesrgan-ncnn-vulkan-windows-x64.zip`;
- `OptiScaler-NR-*-vendored-win-x64.zip`;
- `ReShade-Setup-*-vendored.zip`;
- `nuget-offline.zip`;
- `temurin-25-jre-win-x64.zip`;
- `temurin-25-jre.json`.

The refresh circuit may use upstream GitHub repositories, NuGet.org, APT, WinGet or other required original sources to recreate these assets. The production circuit may not.

Temurin 25 is checked daily against the official `adoptium/temurin25-binaries` GitHub repository. The Windows x64 HotSpot JRE archive is verified against GitHub's SHA-256 digest and executed with `java -version` before promotion.

## Application runtime behavior

Application binaries use manager-owned stable release assets.

`components-manifest.json` records each component's version, asset filename, SHA-256 and source provenance where available.

Media tooling downloads FFmpeg and video2dlssnr only from the manager release.

Minecraft setup downloads the frozen Minecraft runtime only from the manager release. If no suitable Java 25 is installed locally, it downloads the verified manager-owned Temurin 25 archive from the manager release, caches it under the application's local data directory and verifies Java major version 25 before use. Minecraft setup no longer installs Java through WinGet.

## Anti-regression guard

`tools/verify-self-contained.ps1 -Strict` audits both source provenance and production workflow policy.

For `build.yml` and `release.yml`, it rejects:

- `uses:` steps;
- setup/download/upload actions;
- APT or WinGet installation;
- public NuGet endpoints;
- `dotnet restore` commands that do not explicitly use the manager-owned offline NuGet config;
- direct dependency-upstream URLs already covered by the dependency audit.

## Continuity for future work

When work resumes in a new ChatGPT conversation or after context loss, read these files first:

1. `docs/UPSTREAM-SYNC.md`;
2. `docs/PROJECT-CONTINUITY.md`;
3. `third_party/UPSTREAMS.json`;
4. `third_party/DEPENDENCIES.lock.json`;
5. `third_party/minecraft/RUNTIME.lock.json`;
6. `.github/workflows/upstream-monitor.yml`;
7. `.github/workflows/runtime-refresh.yml`;
8. `.github/workflows/build.yml`;
9. `.github/workflows/release.yml`.

Architectural invariant:

```text
upstream
  -> refresh / validation circuit
  -> immutable vendored source + manager-owned runtime seed
  -> offline production build/release
  -> manager-owned stable release
  -> DLSS NR Manager application
```

Do not restore direct third-party dependency downloads to normal build/release or runtime code.
