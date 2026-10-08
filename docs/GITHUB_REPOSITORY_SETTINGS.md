# GitHub Repository Settings Baseline

This file documents repository settings that are enforced by GitHub rather than by files in the repository.

It is an audit baseline, not an API export.

## Main branch ruleset

Observed ruleset:

- name: `Protect main`
- target: default branch
- enforcement: active

Observed rules:

- branch deletion blocked;
- non-fast-forward updates blocked;
- pull request required;
- review threads must be resolved;
- allowed merge methods: squash / rebase;
- required status checks use strict branch-up-to-date policy;
- required status check: `build`;
- linear history required;
- no bypass actors configured.

## Code owner review

`.github/CODEOWNERS` is present for sensitive paths.

The current ruleset does **not** require a CODEOWNER approval.

For a single-maintainer repository this is intentional unless another trusted reviewer is added; requiring code-owner approval can make self-owned pull requests impossible to satisfy.

## CodeQL

The repository hardening branch adds a pinned GitHub CodeQL C# workflow.

Target policy after the workflow is confirmed stable on `main`:

- add the CodeQL analysis check to required status checks;
- keep `build` required;
- keep strict/up-to-date required checks;
- do not remove the normal Build check in favor of CodeQL.

Do not make CodeQL required before its workflow is known to run successfully on the repository's manager-owned/offline restore architecture.

## Security features not verifiable through current integration

The connected GitHub integration used during this audit could not query:

- secret-scanning alerts;
- Dependabot alerts;
- code-scanning alert collections through their dedicated alert endpoints;
- classic branch-protection details requiring administration access.

Therefore this document must not claim "zero alerts" for those products.

Repository owners should periodically check GitHub's Security tab directly.

## Merge policy

Before merging a security/runtime/dependency change:

1. required Build must pass on the exact head;
2. branch must be current with `main`;
3. review threads must be resolved;
4. dependency/license/provenance changes must be reflected in locks/manifests/policies;
5. after CodeQL becomes required, CodeQL must also pass.

## Ruleset change review

Any future change that:

- adds bypass actors;
- permits force-push;
- permits branch deletion;
- removes required PRs;
- removes strict required checks;
- removes Build/CodeQL checks;

should be treated as a security-sensitive repository change.
