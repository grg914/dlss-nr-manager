# Dependency Update Policy

DLSS NR Manager does not blindly follow mutable `latest` dependencies in production.

## Source dependencies

Public vendored sources are recorded in `third_party/DEPENDENCIES.lock.json` using immutable revisions.

Updates should:

1. be detected by the upstream-monitor workflow or manual review;
2. update lock/provenance data;
3. rebuild only affected manager-owned assets;
4. run regression, integrity, license, and security checks;
5. promote validated assets through the manager-owned runtime seed and production release path.

## Runtime assets

Production code should consume bundled/local content or manager-owned DLSS NR Manager release assets. Direct runtime downloads from unrelated upstream release feeds are not the default architecture.

## Restricted dependencies

License-restricted SDKs use notify-only update tracking unless their license explicitly permits automated acquisition and redistribution.

Detection of a new version is not authorization to download, commit, or redistribute restricted material.

## Automated update tools

Generic dependency automation must not bypass:

- immutable source pinning;
- the manager-owned runtime promotion path;
- license review;
- hash/signature validation;
- the offline NuGet seed policy;
- exact-source release provenance.

Project-specific upstream/runtime workflows remain authoritative for native, model, runtime, and other high-risk dependencies.

## Review priority

Security and correctness fixes take priority over version freshness. Major native/model/runtime upgrades require explicit compatibility testing rather than automatic merge based only on version number.
