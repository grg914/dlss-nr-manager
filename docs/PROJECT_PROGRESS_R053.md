# Checkpoint R053 — Conservative branch cleanup (2026-10-08)

## User instruction

Delete **only** branches whose changes have already been correctly merged. Do not remove divergent work, branches with closed-but-unmerged PRs, open work or release refs.

## Pre-cleanup GitHub inventory

- Default protected branch: `main` SHA `58761014f8e4962a5c271e9c0801677935e399e2`.
- 128 remote branch refs and 116 GitHub PRs inspected across paginated APIs.
- 85 branch refs selected by **all** of these frozen conditions: GitHub PR explicitly merged into `main`; source repository is `grg914/dlss-nr-manager`; current branch tip SHA matches the merged PR head SHA; no open PR uses the branch; not `main` or a `release/*` ref.
- 43 refs excluded, including `main`, release branches, open PR #125/#128, orphan/divergent branches, superseded PRs that were never directly merged.
- Exact candidates, last-head SHAs and PR numbers are frozen in `docs/BRANCH_CLEANUP_CANDIDATES_2026-10-08.json`. This is an audit record, **not proof that they have been deleted**.

## Implementation and further runtime safeguards

- `tools/prune-merged-pr-branches.py` defaults to a **non-destructive dry run**. `--execute` only works during a `push` to `main` in the correct original repository. It re-fetches each PR and source ref before deletion and checks the exact pre-approved SHA, real `merged_at` and merge commit, target `main`, absence of open PRs, unprotected status, no explicit reference in active repository workflows, and unchanged tip immediately before DELETE.
- Temporary workflow `.github/workflows/verified-merged-branch-cleanup.yml` is triggered only when itself or the frozen manifest first arrives on `main`. Uses GitHub's restricted Actions token and repository-local code pinned to the triggering commit. No external action or external token is used.
- Every attempted deletion is confirmed by an API GET after the DELETE. The job writes a machine-readable record and per-branch decision in its Actions summary and fails if a deletion cannot be verified. A failed check never authorizes deletion.
- Race caveat: GitHub's DELETE ref API has no atomic expected-SHA compare-and-delete; the script checks SHA immediately before deletion, but concurrent ref mutation in the tiny TOCTOU window cannot be fully ruled out. The conservative head check and low-concurrency scheduled run mitigate but do not erase this limitation.
- The PR must pass required CI before merge. **No branch is reported deleted until the cleanup workflow actually completes and the remote branch inventory is rechecked.**

## Follow-up

1. Review PR's source, manifest and CI; merge only after checks succeed.
2. Inspect `Verified Merged Branch Cleanup` action run on `main` and verify actual DELETE/404 evidence.
3. Re-read GitHub branch inventory and record counts/results against issue #124.
4. Leave excluded refs untouched. Their later semantic audits must not be replaced by automatic pruning.
5. Continue separate v4 release work; this cleanup does not validate GPU hardware or constitute a v4 release.
