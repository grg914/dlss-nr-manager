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

## In-app official upstream visibility (partial issue #111)

The Download Center offers an **on-demand** read-only official GitHub source
check, using the enabled, reviewed entries of \`third_party/UPSTREAMS.json\`
and pinned revisions/tags in \`third_party/DEPENDENCIES.lock.json\`.
Both small metadata files are bundled as embedded resources; the SDK and
vendored source trees are NOT bundled with the Windows application.

- GitHub release and tag checks compare strict numeric version tuples.
  Opaque or prerelease identifiers are reviewed manually, never automatically
  installed.
- A changed upstream branch-head SHA is flagged as a **different source
  revision**, not proof of a newer compatible release or linear ancestry.
- The user may open only an allowlisted official GitHub repository link.
  No unvalidated binaries are downloaded or installed from the official feed.
- Explicit manual checks are cached for fifteen minutes to limit GitHub API
  traffic. Unavailable/limited APIs are reported as unavailable.
- The existing manager-owned "Update" action still applies only to validated
  media and eligible AI Studio model package receipts. More per-component
  update receipts and rollback tests remain required (#111). The central
  upstream source overview must not be misrepresented as a ready installer.
