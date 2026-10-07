# Contributing to DLSS NR Manager

## Before opening a pull request

Run the normal regression/build checks and keep the project self-contained.

A contribution that adds or changes a dependency/runtime/model must answer:

- What is the upstream source?
- What exact immutable revision/version is used?
- What license applies to source and runtime/model weights?
- Is redistribution allowed?
- Is the component source-vendored, manager-owned, restricted-local, manual-license, or external-only?
- How is integrity verified?
- Does the change introduce a new runtime network dependency?
- Does it preserve offline behavior after installation?
- Does it add a duplicate download/install entry outside the centralized Downloads surface?

See:

- `SECURITY.md`
- `THIRD_PARTY_NOTICES.md`
- `docs/COMPONENT_LIFECYCLE_POLICY.md`
- `docs/NETWORK_OFFLINE_PRIVACY_POLICY.md`
- `third_party/DEPENDENCIES.lock.json`
- `third_party/UPSTREAMS.json`

## Dependency rules

Do not:

- commit credentials/tokens/private keys;
- commit raw restricted NVIDIA SDK material;
- add floating/unpinned source revisions;
- reintroduce direct production runtime fallbacks to arbitrary upstream mirrors;
- mirror gated/restricted AI model weights unless redistribution rights are explicitly cleared;
- add hidden component downloads from feature pages.

When adding a source dependency:

1. Pin the exact commit in `third_party/DEPENDENCIES.lock.json`.
2. Add/update `third_party/UPSTREAMS.json` if monitoring is appropriate.
3. Preserve the upstream license/notice.
4. Choose the promotion mode according to risk: automatic only where validation is strong; otherwise notify/manual.
5. Update `THIRD_PARTY_NOTICES.md` when the dependency is materially user-distributed.

## Downloads and runtime assets

Shared downloadable components are managed from **Downloads**.

New manager-owned packages should provide:

- stable package id/version;
- manager-owned release tag/asset naming;
- SHA-256/digest;
- provenance/source information;
- safe/atomic install behavior;
- remove/redownload behavior;
- explicit >1 GiB approval based on the complete package size;
- English and French UI strings.

## User-visible language

New user-visible text must support both:

- English;
- French.

Do not translate proper model/product names or legal license names.

## Tests

Add regression coverage for behavior that can be tested deterministically, especially:

- path containment/traversal;
- download/install state;
- component removal;
- hash/integrity checks;
- license-gating policy;
- localization behavior;
- download consent thresholds;
- model/task selection.

Warnings are treated as errors in CI.

## Pull-request scope

Keep functional work separate when it materially reduces merge risk. Repository-wide policy/governance changes should generally be isolated from feature implementation.

