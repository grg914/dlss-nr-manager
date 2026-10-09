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

The repository has a pinned GitHub CodeQL C# workflow.

CodeQL is now confirmed stable on `main` with the manager-owned/offline restore architecture, including successful post-merge runs #31 and #34.

Current repository-setting gap:

- the active `Protect main` ruleset still requires only `build`;
- CodeQL should now be added as an additional required status check;
- keep `build` required;
- keep strict/up-to-date required checks;
- do not remove the normal Build check in favor of CodeQL.

The connected GitHub integration used for this hardening work can read the ruleset but does not expose ruleset mutation, so this remains a GitHub repository-settings action rather than a repository-file change.

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

## Owner checklist — security settings (2026-10-09)

These are **manual GitHub Settings actions**; inclusion here does not mean they are enabled. Verify the live Settings UI.

1. Settings > Rules > Rulesets > Protect main: verify it remains active, enforces PRs, blocks branch deletion and force-pushes, keeps strict/up-to-date status checks and `build`, and add the CodeQL job `Analyze C#` as an additional required check once the exact check name is confirmed in a successful PR run. Avoid mandatory self-approval for a single-maintainer repo.
2. Settings > Security > Advanced Security: enable dependency graph, Dependabot alerts and security updates; enable private vulnerability reporting and secret scanning/push protection if available under the account plan. Keep CodeQL active. Consider grouped security updates only after assessing PR noise.
3. Settings > Actions > General: keep default `GITHUB_TOKEN` permissions read-only; allow write only in explicitly scoped publishing/updating workflows, and require review for untrusted fork workflows. Confirm third-party action policy does not break pinned CI actions.
4. Settings > Collaborators: remove unneeded write/admin access, check installed apps and fine-grained tokens, and retain least privilege.
5. Dependabot version-update configuration: manually add `.github/dependabot.yml` only after review. Limit it to `github-actions` initially (weekly; at most two open update PRs). **Do not configure NuGet auto-updates**: normal builds use manager-owned offline packages and immutable dependency locks. Verify any Actions pin updates against repository rules before merging.
6. Settings > General / Pull Requests: preserve squash merging, prevent unintended direct merges, and inspect automatic merge settings.

Do not grant an AI agent GitHub administration permission as a substitute for human configuration. `AGENTS.md` is agent guidance and not an access-control boundary.
