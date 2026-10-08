# Contributing

DLSS NR Manager uses a strict reproducibility and supply-chain model. Contributions are welcome, but production code must not bypass it.

## Before changing code

Read:

- `README.md`
- `SECURITY.md`
- `third_party/README.md`
- `docs/UPSTREAM-SYNC.md`
- `docs/RELEASE_ARTIFACT_POLICY.md`
- `AI_PROJECT_PROGRESS.txt` when working on an active project phase.

## Branches and pull requests

Use focused branches and keep unrelated features in separate pull requests.

A PR should state:

- what changed;
- why it changed;
- affected runtime/dependencies;
- licensing implications;
- test evidence;
- rollback/compatibility implications where relevant.

Do not merge a feature solely because GitHub reports it as mergeable. Required CI checks must pass on the exact head being merged.

## Dependency rules

Do not add an unpinned production dependency.

For source dependencies:

- use immutable commit SHAs;
- record provenance in `third_party/DEPENDENCIES.lock.json`;
- define upstream update policy in `third_party/UPSTREAMS.json`;
- preserve upstream license/notice files.

For manager-owned runtime assets:

- publish through controlled GitHub Releases/runtime seed;
- record version/source provenance;
- verify SHA-256 before promotion/use;
- do not make normal production builds depend on third-party upstream availability.

For restricted material:

- keep it outside the public repository;
- use `third_party-local/` or another explicitly ignored local path;
- never publish raw restricted SDK material unless redistribution rights have been independently confirmed.

## Large files and models

Do not commit large runtime/model binaries into Git history.

Use GitHub Releases or the repository's manager-owned package/chunk mechanism.

AI model redistribution must follow the model's actual license. End-user acceptance does not grant the project additional redistribution rights.

## Downloads

A component must have one canonical install/remove/redownload surface.

Do not introduce hidden background downloads from unrelated feature pages when the component is already managed by the centralized download/update architecture.

Large downloads must follow the project's explicit confirmation policy.

## Localization

New user-facing application text must support both English and French unless it is:

- an official product/model name;
- a legal license name;
- a raw diagnostic/error supplied by an external runtime.

## Tests

Run the relevant tests and static checks before merge.

Production CI must continue to validate:

- immutable dependency refs;
- zero-upstream production policy;
- manager-owned offline NuGet restore;
- application tests;
- publish output;
- critical runtime integrity checks.

Never claim a test passed unless the actual run passed.

## Licensing

The root project license does not replace third-party licenses.

When adding or redistributing a component, update the appropriate notice/policy manifests and preserve required attribution/source obligations.

## Generated or AI-assisted changes

AI-assisted code is allowed, but the contributor remains responsible for:

- correctness;
- provenance;
- licensing;
- security;
- tests;
- avoiding copied incompatible code.
