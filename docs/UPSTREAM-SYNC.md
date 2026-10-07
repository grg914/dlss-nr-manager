# Phase 3 — trusted upstream synchronization

This repository uses a two-circuit dependency model.

## Production circuit

Normal builds and releases consume only source snapshots committed in this repository, manager-owned assets from `grg914/dlss-nr-manager`, the manager-owned `runtime-seed-v1` prerelease, and an already provisioned build toolchain.

Production code must not download directly from Modrinth, Fabric, NVIDIA upstream releases, NuGet.org, Hugging Face, BtbN, ReShade upstream, OptiScaler upstream, or other third-party repositories.

## Refresh circuit

`.github/workflows/upstream-monitor.yml` is the automatic path that follows supported public upstreams. It runs daily and can also be started manually. Policies live in `third_party/UPSTREAMS.json`.

For every promotable dependency the refresh circuit resolves an immutable upstream commit, updates the lock, vendors the exact source tree, refreshes Minecraft runtime metadata when needed, bumps the manager patch version, opens an automation PR, runs build/regression gates, merges only after validation, refreshes manager-owned runtime assets, and dispatches a production release.

The NVIDIA DLSS SDK remains `notify-only`. Its license boundary must not be bypassed by automation.

## Runtime seed

`runtime-seed-v1` is a prerelease used as the manager-owned supply cache. It must remain a prerelease so it cannot replace the latest application release.

Expected assets include `video2dlssnr_release.zip`, `streamline-runtime-v*-win-x64.zip`, `minecraft-runtime-26.2.zip`, `ffmpeg-dlssnr-win-x64.zip`, and `nuget-offline.zip`.

Only refresh workflows may contact original upstreams needed to rebuild these assets.

## Component manifest

Each application release publishes `components-manifest.json`, recording component id, version, manager-owned asset, SHA-256, source id and immutable source ref.

The application uses manager-owned release assets as its binary source.

## Continuity

When work resumes in a new conversation, read this file plus `third_party/UPSTREAMS.json`, `third_party/DEPENDENCIES.lock.json`, `third_party/minecraft/RUNTIME.lock.json`, `.github/workflows/upstream-monitor.yml`, and `.github/workflows/runtime-refresh.yml`.

Architectural invariant:

`upstream -> refresh/validation -> manager-owned source/assets -> production/app`

Do not restore direct third-party runtime downloads to normal CI/release or application code.
