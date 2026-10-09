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
- required status checks: `build` and `Analyze C#` (CodeQL);
- linear history required;
- signed commits required;
- no bypass actors configured.

## Code owner review

`.github/CODEOWNERS` is present for sensitive paths.

The current ruleset does **not** require a CODEOWNER approval.

For a single-maintainer repository this is intentional unless another trusted reviewer is added; requiring code-owner approval can make self-owned pull requests impossible to satisfy.

## CodeQL

The repository has a pinned GitHub CodeQL C# workflow.

CodeQL is now confirmed stable on `main` with the manager-owned/offline restore architecture, including successful post-merge runs #31 and #34.

Verified repository settings (2026-10-09 via GitHub rulesets API):

- `Protect main` is active for the default branch;
- both `build` and `Analyze C#` are required with strict/up-to-date checks;
- branch deletion, non-fast-forward pushes and non-linear history are blocked;
- signed commits are required;
- pull requests and review-thread resolution are required, with zero mandatory approving reviews;
- no bypass actors are configured.

The connected GitHub integration can read rulesets but does not expose ruleset mutation. Other security feature toggles and Actions defaults must still be verified by the repository owner in GitHub Settings.

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
5. CodeQL `Analyze C#` must also pass on the final PR SHA.

## Ruleset change review

Any future change that:

- adds bypass actors;
- permits force-push;
- permits branch deletion;
- removes required PRs;
- removes strict required checks;
- removes Build/CodeQL checks;

should be treated as a security-sensitive repository change.

## Owner checklist — security settings (2026-10-09)

These are **manual GitHub Settings actions**; inclusion here does not mean they are enabled. Verify the live Settings UI.

1. Settings > Rules > Rulesets > Protect main: maintain the **verified** active protections (PR required, strict status checks for `build` and `Analyze C#`, signed commits, blocked deletion and non-fast-forward changes, linear history, no bypass). Avoid mandatory self-approval for a single-maintainer repo.
2. Settings > Security > Advanced Security: enable dependency graph, Dependabot alerts and security updates; enable private vulnerability reporting and secret scanning/push protection if available under the account plan. Keep CodeQL active. Consider grouped security updates only after assessing PR noise.
3. Settings > Actions > General: keep default `GITHUB_TOKEN` permissions read-only; allow write only in explicitly scoped publishing/updating workflows, and require review for untrusted fork workflows. Confirm third-party action policy does not break pinned CI actions.
4. Settings > Collaborators: remove unneeded write/admin access, check installed apps and fine-grained tokens, and retain least privilege.
5. Dependabot version-update configuration: manually add `.github/dependabot.yml` only after review. Limit it to `github-actions` initially (weekly; at most two open update PRs). **Do not configure NuGet auto-updates**: normal builds use manager-owned offline packages and immutable dependency locks. Verify any Actions pin updates against repository rules before merging.
6. Settings > General / Pull Requests: preserve squash merging, prevent unintended direct merges, and inspect automatic merge settings.

Do not grant an AI agent GitHub administration permission as a substitute for human configuration. `AGENTS.md` is agent guidance and not an access-control boundary.
