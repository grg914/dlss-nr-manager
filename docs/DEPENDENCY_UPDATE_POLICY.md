# Dependency Update Policy

DLSS NR Manager does not blindly follow `latest` at production runtime.

## Source dependencies

Public vendored sources are recorded in `third_party/DEPENDENCIES.lock.json` using immutable 40-character commit SHAs.

Updates should:

1. be detected by the upstream-monitor workflow or manual review;
2. update the lock/provenance;
3. rebuild affected manager-owned assets;
4. run regression/integrity/license checks;
5. promote only validated assets to the runtime seed/final release.

## Runtime assets

Normal production code should consume local/bundled content or manager-owned DLSS NR Manager releases. Direct runtime downloads from unrelated upstream projects are not the default architecture.

## Restricted dependencies

License-restricted SDKs such as NVIDIA/DLSS use notify-only update tracking. Detection of a new version does not authorize downloading, committing, or redistributing restricted SDK/runtime material.

## Automated update tools

Generic automated dependency updaters must not bypass:

- immutable source pinning;
- manager-owned runtime promotion;
- license review;
- binary signature/hash validation;
- offline NuGet seed policy.

The repository therefore uses project-specific upstream/runtime workflows as the authoritative update path for high-risk/native/runtime dependencies.

## Review priority

Security fixes and correctness fixes take priority over feature-version freshness. Major native/model/runtime upgrades require explicit compatibility testing instead of automatic merge based only on version number.
