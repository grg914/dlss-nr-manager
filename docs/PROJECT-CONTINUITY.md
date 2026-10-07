# Live recovery checkpoint

A persistent GitHub checkpoint is maintained in issue #51: `[continuity] DLSS NR Manager pipeline checkpoint`.

After PR #50 is merged, `.github/workflows/continuity-checkpoint.yml` updates that issue whenever Build, Upstream Dependency Sync, Runtime Seed Refresh or Release completes. It records the current `main` SHA, recent workflow runs and open pull requests.

If a ChatGPT/browser connection is interrupted:
1. read issue #51;
2. read `AI_PROJECT_PROGRESS.txt`;
3. verify the latest GitHub workflow/job logs directly;
4. resume from the latest observed failing step;
5. do not reopen phases that already have successful CI evidence.

Repository protection is intentional: `main` requires a pull request and the required `build` check. Upstream automation must never bypass that ruleset with a direct push.

---

# DLSS NR Manager project continuity

This file is the durable handoff for dependency-independence work.

## Current target

The repository is being hardened so that normal CI, application releases and application runtime dependency downloads rely on manager-owned inputs only. Third-party networks are isolated to refresh workflows.

## Core architecture

- Public/open-source source snapshots live under `third_party/` and are pinned by immutable SHA in `third_party/DEPENDENCIES.lock.json`.
- Upstream tracking policy lives in `third_party/UPSTREAMS.json`.
- `upstream-monitor.yml` detects new upstream versions and validates them before promotion.
- `runtime-refresh.yml` is the sole automatic supply-cache promotion gate.
- `runtime-seed-v1` holds validated redistributable runtime/toolchain assets used by production.
- `build.yml` and `release.yml` must remain zero-upstream.
- `components-manifest.json` is generated into stable releases so the application can identify manager-owned component versions/hashes.
- Restricted NVIDIA SDK/runtime inputs remain local or notify-only.
- Java 25 fallback is manager-owned Temurin, not WinGet.

## Important safety decisions

Do not automatically promote Caustica or video2dlssnr source changes when a native rebuild requires restricted NVIDIA inputs.

Do not automatically promote Real-ESRGAN model weight source changes without model-quality validation.

Do not allow production workflows to silently fall back to NuGet.org, APT, WinGet or external GitHub Actions.

## Verification before declaring dependency work complete

- Build workflow green.
- Upstream monitor green and FFmpeg tag pagination verified.
- Runtime seed refresh green.
- Stable release generated from the manager-owned seed.
- Stable release contains `components-manifest.json`, Java 25 runtime, FFmpeg, video2dlssnr, Minecraft runtime, Streamline, Real-ESRGAN, OptiScaler and ReShade.
- Application/test restore succeeds from `nuget-offline.zip` with package sources cleared.
- `verify-self-contained.ps1 -Strict` is green.

See `docs/UPSTREAM-SYNC.md` for the detailed design and invariants.
