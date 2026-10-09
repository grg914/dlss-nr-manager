# AI project coordination — DLSS NR Manager

> **Purpose:** small, shared handoff between two independent GPT conversations. GitHub is the source of truth; this document is a snapshot, **not a lock or a live synchronization service**.
>
> **Repository:** `grg914/dlss-nr-manager`. Related experimental renderer: `grg914/Caustica-RTX`. **Never touch ScandiCraft repositories.**
>
> **Snapshot:** 2026-10-09. Observed `main` SHA: `9421c71a6b995ad53d7b0134d64f2f6eab0ed313` (**refresh before every change**).

## Shared sources and precedence

1. Read the latest `AI_PROJECT_COORDINATION.md`, `AI_PROJECT_PROGRESS.txt`, and `AI_PROJECT_PROGRESS2.txt` on GitHub.
2. Check **live** main SHA, PRs (open/closed/merged), issues, release assets and workflow checks. Live GitHub state overrides older journal/checkpoint text.
3. Keep historical progress journals intact and append only verified checkpoints. Do not replace either journal with a stale branch copy.
4. The coordination file tracks only active ownership, status, conflict risks and next actions. It does not duplicate entire journal histories.

## Parallel work lanes (coordination, not exclusive repository locks)

| Conversation | Primary work lane | Avoid without cross-checking |
| --- | --- | --- |
| **A — Avancement des six phases** | Six-phase roadmap; Minecraft RTX/Caustica and SPBRScandi provenance; ReSTIR/LOD/sky/weather/NRD+FSR research; real implementation and CI checkpoints. | Main-repo PR cleanup and governance PRs currently handled by B. Do not promote experimental Caustica JARs into the stable manager release without validation. |
| **B — Audit PR / issues / cleanup** | Audit manager PRs, closed duplicates, issues, protected squash merges, CI/rulesets, cleanup of demonstrably obsolete PRs and verified branches. | Caustica experimental branches/PRs and six-phase implementation in A; never delete a diverged branch without semantic audit. |

These are planning defaults, not claims of exclusive ownership. Always inspect current PR activity; if a lane changes, update this table via a separate PR.

## Snapshot of active work — verify before acting

| Area | Current reference / state at snapshot | Next safe action |
| --- | --- | --- |
| Manager supply-chain owner review | Manager PR [#158](https://github.com/grg914/dlss-nr-manager/pull/158) reconciled on branch at `4109d95c300a636d728e244bea77deab617e7f3e`; issue [#156](https://github.com/grg914/dlss-nr-manager/issues/156). Dedicated safety test green; required Build/Analyze C# pending at prior check. | Re-read exact HEAD and strict required checks, then squash merge only if permitted; verify merged SHA before closing #156. |
| Manager PR cleanup | Manager PR [#143](https://github.com/grg914/dlss-nr-manager/pull/143) closed **unmerged** as obsolete after merged #163 removed public planning files. Native `delete_branch_on_merge` enabled. | Preserve obsolete branch history until independently audited. Do not resurrect private planning files. |
| Manager documentation/six-phase audit | PR [#162](https://github.com/grg914/dlss-nr-manager/pull/162) draft; feature/component policy reconciliation; new `docs/V4_SIX_PHASES_AUDIT_STATUS.md`. | Coordinate with lane A, recheck diff against current main and promote only after review and CI. |
| Manager Download Center | PR [#165](https://github.com/grg914/dlss-nr-manager/pull/165) draft with partial read-only official upstream version checks; issue [#111](https://github.com/grg914/dlss-nr-manager/issues/111). | Resolve conflicts; do not equate upstream updates with approved installations. |
| Manager process cleanup | PR [#144](https://github.com/grg914/dlss-nr-manager/pull/144) draft; issue [#95](https://github.com/grg914/dlss-nr-manager/issues/95) requires Windows RTX acceptance. | Reconcile against main and test managed-child ownership, normal/forced exit on Windows. |
| Dependency bots | PRs [#152](https://github.com/grg914/dlss-nr-manager/pull/152) and [#153](https://github.com/grg914/dlss-nr-manager/pull/153). | Review actual immutable action SHAs and workflows, update PR branches, require all mandated CI. |
| Six-phase / Caustica work | Lane A: Caustica [PR #12](https://github.com/grg914/Caustica-RTX/pull/12) squash-merged into **experimental** [PR #10](https://github.com/grg914/Caustica-RTX/pull/10) at `c81b4af1f7349cadcbf9b21a6b95a65c692d2fbb` (first opaque receiver after dielectric interface). #12 exact-head [CI #37910256769](https://github.com/grg914/Caustica-RTX/actions/runs/37910256769) green; #10 exact merged-head [CI #37910812817](https://github.com/grg914/Caustica-RTX/actions/runs/37910812817) **SUCCESS (Windows/Linux NGX, JAR/Slang/Java)**. Manager [progress PR #169](https://github.com/grg914/dlss-nr-manager/pull/169) remains under lane-B review. | Lane A: review temporal ReSTIR weighting/MIS, implement spatial reuse and complete in-game RTX tests; passing CI is not renderer acceptance. **Do not** merge #10 into Caustica `main` or promote a JAR until acceptance. Lane B owns Manager PR #169 merge/cleanup. |
| Remaining manager issue groups | [#161](https://github.com/grg914/dlss-nr-manager/issues/161) OptiScaler reproducibility; [#124](https://github.com/grg914/dlss-nr-manager/issues/124) divergent branches; [#79](https://github.com/grg914/dlss-nr-manager/issues/79) OpenMP licensing; plus #51, #76, #149. | Keep separate until the actual acceptance criterion is met; do not bulk-close. |

## Write and merge protocol for both conversations

1. **Before writing:** fetch fresh main SHA, the target file blob SHA, PR HEAD and active branches. Use a **separate topic branch** per task; do not push to another conversation's branch.
2. **Before updating shared docs:** reread their latest contents; preserve every other conversation's entries. Prefer a minimal, focused change. Use the expected file SHA / branch-head lease so a concurrent edit fails rather than silently overwriting.
3. **Record validated outcomes only:** repository, PR/issue number, exact HEAD/merged SHA, test results (or clearly `PENDING`), remaining blockers and owner/lane. Never infer completion from a green unrelated run.
4. **Merge via protected PR:** squash merge only after the **required checks on the current merge candidate** pass, any review threads are resolved, signature/ruleset rules are satisfied, and no conflicting parallel change has landed. Never bypass protections or force-push `main`.
5. **After merge:** verify GitHub reports merged, re-read `main`, update coordination/journals through a focused subsequent PR when appropriate. Do not close an issue unless its acceptance criteria are satisfied.
6. **Branch cleanup:** delete only branches whose changes are explicitly superseded, or whose exact tree/ancestry is proven safe; squash merge alone does **not** prove branch semantic equivalence.
7. If CI, permissions or human/device validation blocks a step, leave it **pending** with the precise blocker. Never claim a release, binary provenance or real RTX benchmark without evidence.

## Next handoff

- **Lane A:** continue the six-phase work and register new experimental Caustica PRs/checkpoints with their precise acceptance status; keep production integration separate.
- **Lane B:** finish CI-gated review of manager PR #158, then #162 and dependency PRs #152/#153; reconcile #144/#165 before merge; continue issue #124 branch audit.
- **Both:** refresh this snapshot from GitHub before editing. Coordinate by distinct PRs; this file does not automatically synchronize ChatGPT conversations.
