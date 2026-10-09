# AI project coordination — DLSS NR Manager

> **Purpose:** small, shared handoff between three independent GPT conversations. GitHub is the source of truth; this document is a snapshot, **not a lock or a live synchronization service**.
>
> **Repository:** `grg914/dlss-nr-manager`. Related experimental renderer: `grg914/Caustica-RTX`. **Never touch ScandiCraft repositories.**
>
> **Snapshot:** 2026-10-09, GPT B source-audited handoff. Observed `main` SHA: `f8fc1d65448f150c27736f31a07131200dca6e63` (**refresh before every change; this is not a live lock**).

## Latest GPT B verified handoff — 2026-10-09, after protected offline merge

- **Production `main` verified** at `dddd9ac76a9108a7607331966f9148245a23e686` (signed). [#324](https://github.com/grg914/dlss-nr-manager/pull/324) **MERGED by protected squash**, original verified-signed HEAD `d283308ad6e5492d5a0566c55795cf7b4c55bf63`, Build `37956420271` and CodeQL `37956420167` both SUCCESS, **194/194 xUnit**. Post-merge SHA ancestry is `identical` and all four affected Git blobs equal the tested source. Old #318 CLOSED/UNMERGED; its older status below is a historical snapshot.
- **Remaining v4 work:** physical Windows 11 RTX **after-connected-install, network-disconnected** startup/media/game/cache and rollback/crash tests (#95); approved OpenMP REDIST license+device validation (#79); release checklist/packaging and explicit user authorization. #111 CLOSED; #161 native recompilation investigation is **non-blocking when reusing immutable approved OptiScaler DLLs**. Latest published app release still v3.2.0; **no v4.0 publication or hardware evidence**.
- **Concurrent lanes:** GPT A owns #325 dependency/license docs and #320 historical shared-doc divergence; neither was edited or merged by GPT B. GPT C v4.5 AI Studio remains separate. **Check live main and three journals again** before promoting any concurrent documentation branch; a saved checkpoint is not an exclusive lock. The on-device network-off acceptance instructions are appended to `docs/V4_WINDOWS_PROCESS_ACCEPTANCE.md` in this documentation-only candidate.

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
| **C — DLSS NR Manager v4.5** | New v4.5-only features, primarily AI Studio execution foundations, read-only job preflight, private runtime integrity, future local inference and user-proposed feature assessments. Active draft PRs: [#199](https://github.com/grg914/dlss-nr-manager/pull/199) (P0) and [#197](https://github.com/grg914/dlss-nr-manager/pull/197) (P1). | Do not modify GPT B's v4.0 stabilization/supply-chain lane or GPT A's Caustica/ReSTIR/LOD/shader lane. New private-roadmap ideas require proposal and owner approval; no v4.5 release/main feature merge authorized. |

These are planning defaults, not claims of exclusive ownership. Always inspect current PR activity; if a lane changes, update this table via a separate PR.

## Snapshot of active work — verify before acting

| Lane / area | Verified status at 2026-10-09 checkpoint | Next safe action |
| --- | --- | --- |
| GPT B — v4.0 offline startup | [#318](https://github.com/grg914/dlss-nr-manager/pull/318), draft signed HEAD `73e5b98475a5b8fe50bbb16bf69a657c7f2cc915`, base current `main` `f8fc1d65448f150c27736f31a07131200dca6e63`; Build run `37950726788` and CodeQL `37950726807` SUCCESS; **194/194 xUnit**. Not merged at checkpoint. | Verify fresh SHA, signature, review threads, exact checks and main, then protected squash and post-merge ancestry/blobs. On-device no-network validation still pending. |
| GPT B — completed media/integrity | Signed merges [#282](https://github.com/grg914/dlss-nr-manager/pull/282), [#290](https://github.com/grg914/dlss-nr-manager/pull/290), [#304](https://github.com/grg914/dlss-nr-manager/pull/304), [#309](https://github.com/grg914/dlss-nr-manager/pull/309), [#313](https://github.com/grg914/dlss-nr-manager/pull/313) integrated with required CI; installed OptiScaler binaries reused, no native rebuild. Issue [#111](https://github.com/grg914/dlss-nr-manager/issues/111) CLOSED. | Keep real Windows/RTX offline, rollback, FR/EN and component acceptance separate from CI; do not reproduce resolved feature work. |
| GPT B — remaining v4 gates | [#95](https://github.com/grg914/dlss-nr-manager/issues/95) physical crash/recovery OPEN; [#79](https://github.com/grg914/dlss-nr-manager/issues/79) OpenMP REDIST legal/physical validation OPEN; [#124](https://github.com/grg914/dlss-nr-manager/issues/124) divergent-branch audit OPEN. [#161](https://github.com/grg914/dlss-nr-manager/issues/161) native reproducibility is future publisher research, **not a v4 gate when reusing existing pinned DLLs**. Latest public release remains v3.2.0 and csproj version 3.2.0. | Test on supported PC; do not declare rights, RTX behavior or publish v4 without evidence/owner approval. |
| GPT A — Minecraft RTX / Caustica | Active manager PRs [#319](https://github.com/grg914/dlss-nr-manager/pull/319), [#316](https://github.com/grg914/dlss-nr-manager/pull/316), [#311](https://github.com/grg914/dlss-nr-manager/pull/311); checkpoint drafts #220/#191/#178/#169. Experimental JARs separate from production. | Lane A only; do not edit or promote experimental changes in GPT B. |
| GPT C — v4.5 AI Studio | Active isolated PRs [#310](https://github.com/grg914/dlss-nr-manager/pull/310), [#314](https://github.com/grg914/dlss-nr-manager/pull/314), #315, #301, #300 and coordination #271. Python/CUDA/ComfyUI runtime still not production-validated. | Lane C only; no GPT B merge/promotion of experimental v4.5 into v4.0. |

**Owner instruction (2026-10-09):** Before each GPT B intervention read all three files `AI_PROJECT_COORDINATION.md`, `AI_PROJECT_PROGRESS.txt`, `AI_PROJECT_PROGRESS2.txt` from live GitHub, plus current main/PR/CI/issues; refresh their verified checkpoint information through protected, focused append-only documentation changes without overwriting other lanes. Documentation is a dated snapshot, not a required meaningless re-commit if no facts changed. One signed protected merge at a time.

## Write and merge protocol for all three conversations

1. **Before writing:** fetch fresh main SHA, the target file blob SHA, PR HEAD and active branches. Use a **separate topic branch** per task; do not push to another conversation's branch.
2. **Before updating shared docs:** reread their latest contents; preserve every other conversation's entries. Prefer a minimal, focused change. Use the expected file SHA / branch-head lease so a concurrent edit fails rather than silently overwriting.
3. **Record validated outcomes only:** repository, PR/issue number, exact HEAD/merged SHA, test results (or clearly `PENDING`), remaining blockers and owner/lane. Never infer completion from a green unrelated run.
4. **Merge via protected PR:** squash merge only after the **required checks on the current merge candidate** pass, any review threads are resolved, signature/ruleset rules are satisfied, and no conflicting parallel change has landed. Never bypass protections or force-push `main`.
5. **After merge:** verify GitHub reports merged, re-read `main`, update coordination/journals through a focused subsequent PR when appropriate. Do not close an issue unless its acceptance criteria are satisfied.
6. **Branch cleanup:** delete only branches whose changes are explicitly superseded, or whose exact tree/ancestry is proven safe; squash merge alone does **not** prove branch semantic equivalence.
7. If CI, permissions or human/device validation blocks a step, leave it **pending** with the precise blocker. Never claim a release, binary provenance or real RTX benchmark without evidence.
8. **Serialize protected main merges across A/B/C:** only one conversation may issue a squash merge at a time. Immediately before merging, verify the live `main` SHA, candidate base, signed HEAD, required exact-head checks, and review threads. Other conversations must not merge to `main` concurrently.
9. **Verify ancestry after every merge:** re-fetch live `main` and compare the returned merge SHA as `base` to `main` as `head`. The merge SHA must be identical to, or an ancestor of, the live main commit; also verify relevant file blobs/content. A PR being marked `merged` is **not** sufficient. If the history diverges, halt further merges and reconcile the missing changes through a new signed, protected PR. Never reset or force-push `main`. See [#233](https://github.com/grg914/dlss-nr-manager/issues/233).


## Next handoff

- **Lane A:** continue source-verified Caustica/Minecraft experimental development on its own PRs, do not auto-promote its JAR to stable v4.
- **Lane B:** after this shared docs checkpoint is protected-merged, reconcile #318 on the live main, ensure fresh signed exact-head Build/CodeQL before protected squash; then prepare **on-device** no-Internet, Windows process cleanup, rollback and Real-ESRGAN acceptance. No unnecessary native OptiScaler compilation.
- **Lane C:** proceed with independently reviewed, isolated v4.5 AI Studio Python/ComfyUI work; require on-device offline inference proof, no automatic v4.0 integration.
- **All three:** recheck real main and all three documents before next intervention and after each signed squash. Never rely on this snapshot as a synchronization lock.


## GPT B / GPT A reconciled source-audit handoff — 2026-10-09 (review candidate)

- GPT B protected-merged [#329](https://github.com/grg914/dlss-nr-manager/pull/329) as verified main `ddd57336d7f2784d7699a1f2ab32fe49c4684fc9` (Build/CodeQL SUCCESS; all four file Git blobs checked), preserving the offline-startup source merge [#324](https://github.com/grg914/dlss-nr-manager/pull/324). Physical offline Windows verification remains pending.
- GPT A source audit is selectively reconciled from experimental review-only [#331](https://github.com/grg914/dlss-nr-manager/pull/331), source `77ae43b39a6061d2982ab01bb8e5a6919cf42ed9`: four source-identical audit/RC documents, source-verified Caustica LGPL-3.0-or-later license metadata and six-phase historical audit corrections. This is **documentation and source-lock metadata only**; no native, installer, experimental ReSTIR or AI Studio v4.5 feature is promoted.
- Stable v4 gates remain Windows/RTX acceptance and rollback [#95](https://github.com/grg914/dlss-nr-manager/issues/95), legal Microsoft REDIST provenance or fail-closed omission [#79](https://github.com/grg914/dlss-nr-manager/issues/79), final FR/EN/release package/hash/SBOM/signature checks. No new mandatory third-party fork or download identified. The above integration is **proposed on a separate review branch** until signed exact-head Build/CodeQL and protected merge succeed. All GPT A/C experimental PRs remain drafts.


## GPT B — B07 isolated v4 stable fix (2026-10-09; DRAFT)
- Verified source main `a2c26427505e7b1740768de07075a42a619be5b6` and GPT A audit. Enumerates all VLC locations, prefers the newest version with proven >=3.0.24 support, and never treats unknown version as compatible. Candidate preference unit tests added. These are source-only, unmerged changes requiring exact-head Windows Build/CodeQL, review and GitHub-signed protected merge. GPT A #339 and GPT C v4.5 untouched; any later shared-journal promotion must reconcile the then-current main and concurrent B01/B02 PRs.
