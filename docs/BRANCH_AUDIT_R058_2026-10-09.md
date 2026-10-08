# R058 — Live audit of every surviving branch (2026-10-09)

**Repository:** grg914/dlss-nr-manager. **Comparison baseline:** main at d5221deb14e774723e1cde408d182146597d1770, immediately after cleanup PR #129 and before journal PR #125 was merged.

Source: GitHub paginated /branches and /pulls?state=all responses, plus independent compare main...branch for each of the 44 non-main branch refs. Branches are preserved unless expressly eligible for a later separate guarded cleanup. A GitHub squash merge does not make the PR head an ancestor of main. Ahead commits do NOT demonstrate missing functionality, while a closed PR is NOT automatically an integrated PR.

Since this snapshot, PR #125 was confirmed merged to main at 51ee9cd42461b09500b370592c8663380862d8be. Its old head and ahead/behind values in this table are historical. Main advances will likewise change relative commit counts; run a new comparison before any destructive action.

## Summary

- 45 refs total, 44 non-main refs individually compared.
- Five exact ancestor refs have zero unique commits. They are cleanup candidates only after confirming no active workflow/external consumer.
- Two PR branches (#125 and #128) were open in the inventory; PR #125 has since been merged.
- Four release refs are retained; the already-merged #33 ref is actively consumed by nuget-seed.yml.
- Divergent histories, including source-vendoring branches with GitHub's 300-file comparison truncation, must not be auto-merged.
- A superseded/closed-unmerged PR is only historical context; exact files and tests must be checked semantically before declaring it fully incorporated.

## All 45 branch references

| Branch | Ahead / behind main | Status at snapshot | Action or concern |
|---|---:|---|---|
| `audit/caustica-scandi-assets` | 6 / 42 | Closed-unmerged #60 | HEAD changed after #60; inspect Caustica asset audit |
| `automation/upstream-sync-37588055056` | 1 / 51 | No PR | FFmpeg vendor/lock divergence; review provenance |
| `automation/upstream-sync-37589917835` | 1 / 50 | No PR | FFmpeg vendor/lock divergence; review provenance |
| `chore/prune-verified-merged-branches-20261008` | 6 / 1 | Merged #129 | Squash merged; check consumers before optional ref cleanup |
| `chore/repository-governance-20261008` | 44 / 31 | Closed-unmerged #78 | Governance/config overlaps with subsequent PR #81 |
| `chore/repository-governance-hardening` | 46 / 31 | Closed-unmerged #80 | Governance/config overlaps with subsequent PR #81 |
| `ci/protected-release-request` | 0 / 47 | Exact ancestor | No unique commits; verify workflow references before cleanup |
| `docs/fix-readme-version-and-v4-checkpoint` | 2 / 10 | Closed-unmerged #107 | Docs only; compare before salvage |
| `docs/live-ai-project-progress` | 199 / 31 | No PR | High divergence; preservation required |
| `docs/recovery-checkpoint-20261008-pr102-103` | 1 / 12 | Closed-unmerged #104 | Historical progress overlaps later checkpoint |
| `docs/v4-branch-audit-20261008` | 8 / 0 | PR #125 at snapshot | Merged after snapshot, commit 51ee9cd; branch ref may remain |
| `docs/v4-release-changelog-baseline` | 1 / 9 | Closed-unmerged #109 | CHANGELOG overlaps PR #112; semantic review |
| `feature/v4-download-center-media-update-detection` | 9 / 8 | Closed-unmerged #113 | Superseded by merged #114; compare behavior |
| `fix/finalize-continuity-checkpoint` | 2 / 46 | Closed-unmerged #55 | Checkpoint workflow divergence |
| `fix/finalize-v311-continuity` | 2 / 45 | Closed-unmerged #57 | Checkpoint workflow divergence |
| `fix/full-audit-v1.5.1` | 24 / 73 | Closed-unmerged #18 | Superseded by merged #19/#20, but not ancestry-proof |
| `fix/minecraft-fabric-profile-determinism` | 6 / 57 | Closed-unmerged #40 | Minecraft bootstrap/CI changes; selective review |
| `fix/minecraft-nvidia-runtime-ux` | 2 / 70 | Closed-unmerged #22 | WPF UX changes; selective review |
| `fix/phase3-bootstrap` | 10 / 62 | No PR | CI/bootstrap + vendor workflow divergence |
| `fix/reproducible-model-and-release` | 0 / 78 | Exact ancestor | No unique commits; verify consumers before cleanup |
| `fix/seed-consistency-and-continuity` | 3 / 48 | No PR | Runtime-seed and workflow changes; selective review |
| `fix/v3.2.0-i18n-complete` | 1 / 36 | Closed-unmerged #69 | UiLocalizationService difference; audit French/English text |
| `fix/v4-ai-studio-staging-cleanup` | 2 / 12 | Closed-unmerged #103 | Superseded by merged #105; compare rollback logic |
| `fix/v4-owned-helper-isolation` | 5 / 2 | PR #128 at snapshot | Reconciled on main after #125; CI pending |
| `hardening/minecraft-native-ngx-policy` | 0 / 39 | Exact ancestor | Identical SHA to sibling; verify consumers |
| `hardening-minecraft-native-ngx-policy` | 0 / 39 | Exact ancestor | Identical SHA to sibling; verify consumers |
| `hardening-minecraft-native-ngx-policy-final` | 1 / 39 | Closed-unmerged #64 | Superseded by merged #66, verify |
| `hardening-minecraft-native-ngx-policy-v2` | 2 / 39 | Closed-unmerged #65 | Superseded by merged #66, verify |
| `main` | 0 / 0 | Protected main | Never delete |
| `phase4-upstream-fix` | 2 / 61 | Closed-unmerged #35 | Temurin/upstream scripts; selective audit |
| `refactor/ffmpeg-self-contained` | 108 / 64 | Merged into non-main #30 | Shares HEAD with video2dlssnr branch; 300-file compare cap |
| `refactor/media-runtime-cutover` | 107 / 64 | Closed-unmerged #31 | 300-file compare cap; preserve |
| `refactor/monorepo-caustica` | 0 / 64 | Exact ancestor | No unique commits; verify consumers before cleanup |
| `refactor/phase4-zero-upstream-production` | 42 / 62 | Merged #33, workflow consumer | Referenced by .github/workflows/nuget-seed.yml: KEEP |
| `refactor/video2dlssnr-dynamic-ngx` | 108 / 64 | No PR | Shares SHA with ffmpeg-self-contained; 300-file compare cap |
| `release/v2.0.0-audit` | 30 / 71 | Release / merged #21 | KEEP release tag/context |
| `release/v2.0.1-spbrscandi` | 2 / 69 | Release / merged #24 | KEEP release tag/context |
| `release/v3.0.0-full-audit` | 47 / 65 | Release / merged #28 | KEEP release tag/context |
| `release/v3.2.0` | 6 / 37 | Release / merged #68 | KEEP release tag/context |
| `research/v4-realesrgan-openmp-free-build` | 4 / 6 | Closed-unmerged #118 | Superseded by merged #119; OpenMP license issue #79 remains |
| `security/v4-pc-clean-parent-reparse` | 3 / 5 | Closed-unmerged #121 | Superseded by merged #122; safety compare required |
| `security/v4-recovery-parent-reparse-guard` | 8 / 9 | Closed-unmerged #110 | Rollback/reparse-point safety, selective test comparison |
| `security/v4-safe-component-removal` | 8 / 7 | Closed-unmerged #116 | Superseded by merged #117; selective review |
| `test/v4-windows-acceptance-audit` | 3 / 5 | Closed-unmerged #120 | Superseded by merged #122; hardware acceptance outstanding |
| `test/v4-windows-jobobject-crash` | 6 / 7 | Closed-unmerged #115 | Superseded by merged #117; crash acceptance outstanding |

## No-delete rules and next actions

1. Never delete main, active PR branches, release branches or referenced branch refactor/phase4-zero-upstream-production.
2. Treat five exact-ancestor refs as non-destructive cleanup candidates, not authorization for deletion: workflow consumers, release pipelines, documentation links and external uses must be reviewed.
3. Review the closed-but-unmerged PR families on their *current* production files before cherry-picking: translations (#69), Minecraft user interface (#22), runtime seed and bootstrap (orphan refs), rollback parent-symlink guards (#110) and Download Center (#113→#114).
4. Do not integrate FFmpeg/Caustica vendored trees directly from stale divergent branches. Evaluate upstream lock and licensing changes separately.
5. PR #125 (R046–R056 historic continuity) is now merged, PR #128 (per-instance process ownership) requires fresh Windows Build/CodeQL before moving out of draft and merging.
6. Release 4.0 remains blocked by issues #79, #82, #95, #111, #123 and unresolved semantic review in #124. Build/CodeQL green is not proof of physical GPU acceptance.

**Decision:** NO additional branch deletion or automatic merge from this audit. Historical evaluation and status recorded at https://github.com/grg914/dlss-nr-manager/issues/124 .
