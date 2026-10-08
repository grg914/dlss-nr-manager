# Contributing

DLSS NR Manager is reliability-first. Changes that make the project easier to build but weaken provenance, licensing, offline reproducibility, or safety checks are not accepted.

## Before opening a pull request

- Branch from the current `main`.
- Keep the change focused; do not mix unrelated feature work and repository-governance changes.
- Run the regression tests and repository-policy checks.
- Update documentation/manifests when behavior, downloads, runtime assets, licenses, or user-visible controls change.
- User-visible UI added to the production WPF application must support both **English and French**.
- Do not add a second install/download button for a component already managed by the central Downloads surface.
- Do not silently add runtime network calls to upstream projects.

## Dependency rules

Public source dependencies belong in `third_party/DEPENDENCIES.lock.json` and must be pinned to immutable 40-character commit SHAs.

Normal production runtime must prefer:

1. local/bundled content;
2. manager-owned assets published from `grg914/dlss-nr-manager`;
3. explicit user-provided local files for license-gated/restricted components.

Direct production dependencies on unrelated upstream release feeds must not be reintroduced without an explicit architecture review.

Large redistributable binaries/models belong in GitHub Releases, not Git history. License-restricted NVIDIA SDK inputs belong under ignored `third_party-local/` unless redistribution is separately cleared.

## Licensing

Preserve upstream license, notice, model-card, and source-offer obligations. The root MIT license applies to DLSS NR Manager's own code; it does not relicense vendored or separately distributed third-party components.

Before adding a new model/runtime, document:

- source/repository;
- exact version/commit;
- license;
- whether manager-owned redistribution is allowed;
- required notices/source offer;
- expected installed/download size;
- integrity mechanism.

## Pull request checklist

The PR template is authoritative. At minimum confirm:

- tests/build pass;
- warnings-as-errors pass;
- new downloads are integrity checked;
- archives are path-safe;
- FR/EN strings are covered;
- license/provenance metadata is updated;
- no secret, token, private SDK, or local path was committed.
