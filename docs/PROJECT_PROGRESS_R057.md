# R057 — Process ownership acceptance and post-cleanup continuation (2026-10-09)

## Verified evidence

- The branch cleanup in PR #129 was squash-merged to `main` SHA `d5221deb14e774723e1cde408d182146597d1770`; the protected `main` and both development branches for PR #125 and draft PR #128 survived.
- Workflow run #37852360279 ultimately SUCCESS. A fresh API enumeration proved **84 safe deletions, 45 surviving branches**, and exactly one merged candidate still preserved due an active workflow reference.
- Draft PR #128 previous head `4b39c38e51ef8925b394a3d6eded5d3bf1aadac1`: Build #37849834512 **SUCCESS** including regression tests and win-x64 packaging, CodeQL #37849834471 **SUCCESS**. These checks precede reconciliation with the new `main`; they cannot substitute for fresh checks on the eventual head.
- The regression test `Stop_does_not_kill_untracked_helper_from_shared_manager_install_path` isolates the original failure: a separate untracked helper executable under the manager's shared LocalAppData path must remain alive when `ExternalProcessTracker.KillAll()` stops this instance's owned processes.
- `ExternalProcessTracker.Start` still has a Start→JobObject-assignment window; the assignment currently logs failure rather than fail-closing. Do not falsely claim the application has perfect crash ownership under all failure modes.

## PR #125 coordination

- PR #125 head `a60541ceeb4fbce6f0f8461718fcb7a455641e6d` was synchronized to the cleaned main. Its audit now warns that 125-branch counts are historical; docs checkpoint R056 lists actual post-cleanup state.
- Await current-head Build and CodeQL success before merging #125. Then reconcile #128 with the updated main while preserving this checkpoint, R051–R052, code and tests. Do not merge the draft before those checks succeed.

## Current unresolved gates

- Full real GPU/Windows shutdown, rollback, launch/exit and multi-instance testing (issue #95) still requires a physical machine.
- Release blockers #79 (OpenMP redistributable licensing), #82 (CodeQL ruleset), #111 (updates beyond media), #123 (atomic seed publication), and #124 (unmerged/divergent branch semantic audit) remain outside this PR's narrow scope.
- v4.0 release has not been published. No hardware acceptance or license clearance is claimed.
