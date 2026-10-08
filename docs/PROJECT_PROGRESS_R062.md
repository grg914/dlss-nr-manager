# R062 — Safely prune only five exact ancestors (2026-10-09)

PR #130 merged to main commit 968414ec8597384ac34b1c099e18c68ffadb487d after Build #37855886717 SUCCESS and CodeQL #37855886704 SUCCESS. The historical R058 all-branch audit and full root journal are in main. There were 46 branches and zero open PRs immediately after this merge, including retained merged-PR head refs.

The five branch references in BRANCH_CLEANUP_EXACT_ANCESTORS_R062.json were independently compared again with current main. Each has **0 commits ahead**, no differing files, the exact frozen SHA and no protection. All eleven current GitHub workflow files were scanned and had no direct reference to these branch names.

A separate one-shot GitHub Actions workflow performs read-only dry-run on pull requests and may delete only those five pinned refs after an authorized protected-main merge, after comparing them to live main, checking the current SHA, protection, open PR heads, and every active workflow reference again. A changed reference, a new consumer, or missing proof blocks deletion. Nine fail-closed regression tests cover manifest integrity and eligibility checks.

Do not report actual deletion until the post-merge workflow has SUCCESS and a fresh independent GitHub branch enumeration proves that each deleted ref is absent. Divergent branches, release branches, all other merged PR refs and branches needed by nuget-seed.yml are **excluded**.

The 4.0 release remains blocked by #79, #82, #95, #111, #123, and #124. Do not claim physical Windows/GPU acceptance or completed licensing review.
