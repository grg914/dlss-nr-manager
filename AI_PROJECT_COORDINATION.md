# AI project coordination — DLSS NR Manager

> **Purpose:** small, shared handoff between three independent GPT conversations. GitHub is the source of truth; this document is a snapshot, **not a lock or a live synchronization service**.
>
> **Repository:** `grg914/dlss-nr-manager`. Related experimental renderer: `grg914/Caustica-RTX`. **Never touch ScandiCraft repositories.**
>
> **GPT A status refresh (2026-10-09):** Coordination snapshot rechecked against protected main `dddd9ac76a9108a7607331966f9148245a23e686`. [PR #324](https://github.com/grg914/dlss-nr-manager/pull/324) offline update guard **merged**, Build/CodeQL green; source v3.2.0 still not v4.0. ReSTIR stack draft, no RTX physical acceptance. Older snapshot sections below are historical.

> **Snapshot:** 2026-10-09, GPT B source-audited handoff. Observed `main` SHA: `f8fc1d65448f150c27736f31a07131200dca6e63` (**refresh before every change; this is not a live lock**).

### GPT A — v4 dependency audit (2026-10-09)

Owner preference: **finish v4.0 before testing ReSTIR/producing another experimental Manager EXE**. The test-binary CI proposal is paused; #311/#316/#319 remain draft/unmerged. Verified 27 locked public source IDs match the upstream policy IDs; the NVIDIA DLSS SDK remains local-only. No new mandatory fork/source dependency identified. Scoped metadata fix and inventory: `docs/V4_DEPENDENCY_GAP_AUDIT.md`; Caustica source license is LGPL-3.0-or-later (distinct NVIDIA binary obligations). v4 gates remain #79 OpenMP REDIST licensed provenance, #95 real Windows process/rollback acceptance, GPT B offline-update completion and release checklist. Do **not** add v4.5 Python/CUDA/ComfyUI models or experimental ReSTIR assets to stable v4.0. This checkpoint is on a separate review-only PR; GitHub main remains the authority.

### GPT A — Live three-journal reconciliation (2026-10-09)

**Owner workflow:** read **all three** live files before the next intervention; record changes using blob-SHA-guarded, append-only checkpoints on a reviewable branch. This is not a real-time shared lock. **Live main** checked at `b37d92ba801a9a30854e6f38552e475702742f6c`; the older GPT B snapshot above is historical.

- **Scope:** finish Manager **v4.0** first; no experimental Windows Manager package, Caustica ReSTIR release, AI Studio v4.5 runtime or extra dependency fork. GPT B's [#324](https://github.com/grg914/dlss-nr-manager/pull/324) is a separate draft offline-update fix.
- **GPT A PRs:** [#311](https://github.com/grg914/dlss-nr-manager/pull/311) (draft; currently GitHub `mergeable=false` against an older base) → [#316](https://github.com/grg914/dlss-nr-manager/pull/316) → [#319](https://github.com/grg914/dlss-nr-manager/pull/319) remain stacked drafts, unmerged and excluded from stable v4. #319 exact-head Build [37953997832](https://github.com/grg914/dlss-nr-manager/actions/runs/37953997832) **SUCCESS, 198/198 tests**; not RTX hardware validated.
- **Competing document drafts:** [#320](https://github.com/grg914/dlss-nr-manager/pull/320) exact-head Build [37955471632](https://github.com/grg914/dlss-nr-manager/actions/runs/37955471632) and CodeQL [37955471642](https://github.com/grg914/dlss-nr-manager/actions/runs/37955471642) **SUCCESS**, but the branch now **diverges from main** and overlaps the three shared documents. [#325](https://github.com/grg914/dlss-nr-manager/pull/325) is the current GPT A source-aligned dependency-audit/checkpoint draft; reconcile unique #320 history before closing it, never merge both blindly.
- **v4 gates:** issues [#79](https://github.com/grg914/dlss-nr-manager/issues/79) (licensed OpenMP REDIST) and [#95](https://github.com/grg914/dlss-nr-manager/issues/95) (physical Windows rollback) remain **OPEN**. GPT B owns stable release readiness and serialized protected merges. **No change to main** in this checkpoint.

### GPT A — v4.0 release-gate reconciliation (2026-10-09)

Live GitHub audit: `main=b37d92ba801a9a30854e6f38552e475702742f6c`; current app/version and latest stable release **v3.2.0**, not v4.0. PR **#318 closed without merge**; successor offline-update **#324 open draft**, head `d283308ad6e5492d5a0566c55795cf7b4c55bf63`, Build `37956420271` **SUCCESS**, CodeQL `37956420167` **IN PROGRESS** at this checkpoint. GPT B owns completion and reviewed/signed protected merges. Hard v4 gates remain issue **#95** Windows interruption/recovery/rollback acceptance, issue **#79** Microsoft OpenMP licensed REDIST provenance or fail-closed omission, Windows 11/RTX operator acceptance, exact-final-source Build/CodeQL/signature/reviews, package SHA-256+manifest/SBOM/provenance, FR/EN and final immutable `v4.0.0` release. The Caustica experimental draft stack #311→#316→#319 and AI Studio 4.5 are out of stable v4 scope. PR #320 docs diverges from live main; PR #325 source is aligned with latest checked main, pending exact-head CI and independent review. No extra mandatory dependency/fork verified.

### GPT A / GPT B v4 completion handoff — 2026-10-09 (latest verified)

- **Live main** `dddd9ac76a9108a7607331966f9148245a23e686`: GPT B offline background-check PR [#324](https://github.com/grg914/dlss-nr-manager/pull/324) **protected-merged**, exact Build/CodeQL **SUCCESS**, **194/194 tests**. Superseded #318 closed unmerged. Avoid duplicate offline-source work.
- **GPT A completed preparation:** 27 public source lock ↔ upstream policy mapping audited; correction to Caustica LGPL-3.0-or-later lockfile metadata and dependency inventory in [#325](https://github.com/grg914/dlss-nr-manager/pull/325); Windows RC acceptance handoff at `docs/V4_RC_ACCEPTANCE_HANDOFF.md` (PR #325). All staged as **Draft**, not merged, and no SDK/binary distributed.
- **GPT B remains accountable** for stable v4.0 source/reviews/releases. **Still blocked**: [#95](https://github.com/grg914/dlss-nr-manager/issues/95) physical Windows/RTX crashes, download interruption, locked-file/rollback and helper ownership; [#79](https://github.com/grg914/dlss-nr-manager/issues/79) operator Microsoft Visual Studio REDIST entitlement or fail-closed OpenMP omission; final WPF, FR/EN, SHA/signature/manifest/SBOM, RC→immutable v4.0.0.
- **Coordination rule:** owner asked GPT A to reread/reconcile all three journals at every intervention. GitHub comments/PR are the shared handoff, **not** real-time locking. Serialize `main` merges with GPT B and verify signed ancestry. Experimental ReSTIR #311/#316/#319 and GPT C v4.5 must remain out of stable v4.0.

## Latest verified GPT A handoff (2026-10-09 / GPT A / verified live main f8fc1d65448f150c27736f31a07131200dca6e63)

The original tables below are a historical snapshot. Live GitHub state takes precedence. Manager draft stack: [#311](https://github.com/grg914/dlss-nr-manager/pull/311) → [#316](https://github.com/grg914/dlss-nr-manager/pull/316) → [#319](https://github.com/grg914/dlss-nr-manager/pull/319); all open/unmerged. Exact-head Build SUCCESS for all three, CodeQL SUCCESS confirmed for #311 only; hardware RTX/Vulkan, owner review, signature and remaining gates **not approved**. Caustica experimental [#28](https://github.com/grg914/Caustica-RTX/pull/28), [#29](https://github.com/grg914/Caustica-RTX/pull/29), [#30](https://github.com/grg914/Caustica-RTX/pull/30) also open/draft, CI successful, not production-validated. The #30 JAR is a pinned offline personal-test artifact, **not** approved for stable distribution. GPT A will audit recovery and verification before any merge. Never mix GPT B/C lanes or issue parallel main merges.

## Owner-required GPT A pre-intervention protocol (2026-10-09)

Before **each new GPT A intervention/work session**, re-read **all three** `AI_PROJECT_COORDINATION.md`, `AI_PROJECT_PROGRESS.txt`, and `AI_PROJECT_PROGRESS2.txt` from live GitHub, verify the current main/PR/CI state, and update all three with minimal fact-checked checkpoints using a dedicated conflict-safe branch/PR. Preserve every existing GPT B/C entry; never clobber shared files or write directly to protected main. This is an owner request, not authorization to bypass review, signature, CI or serialized merge rules.

### 2026-10-09 GPT A CI addendum

Exact-head Manager [#319](https://github.com/grg914/dlss-nr-manager/pull/319) `78a24c9e5bcdfe7d99091b1d5fcb5ec52006cbb5`: Windows Build [37953997832](https://github.com/grg914/dlss-nr-manager/actions/runs/37953997832) **SUCCESS (198/198 xUnit)**, deterministic packaging, zero-upstream policy. Still **DRAFT/UNMERGED**, no independent review/hardware RTX acceptance and no separate CodeQL confirmed for #319. Coordination Draft [#320](https://github.com/grg914/dlss-nr-manager/pull/320) uses append-only/checkpoint edits to all three documents; exact-head Build/CodeQL **PENDING** at checkpoint. Serialize any protected main merge with GPT B/C.


## GPT A — consolidated four-point implementation handoff (2026-10-09)

**Canonical GPT A review candidate:** branch `docs/gpt-a-consolidated-v4-six-phase-audit-20261009`, built on **live main `dddd9ac76a9108a7607331966f9148245a23e686`** after GPT B's #324 merge. It combines the unique history of old drafts #320/#325, Caustica LGPL metadata, dependency inventory, Windows RC acceptance and source-based NVIDIA / six-phase audit without removing GPT B/C records. Older draft URLs below are historical, not alternative merge targets. All three journals should be re-read and updated before intervention.

- **GPT A Point 1 (PR consolidation):** unique #320 ReSTIR/CI checkpoints preserved alongside #325 v4 handoff, no old main overwrite. Close old draft PRs **only after** checking consolidated source blobs, branch ancestry and GitHub review evidence; protected squash only after signed head Build/CodeQL.
- **GPT A Point 2 (Caustica import):** experimental Manager #319 updated to reject malformed/forged import receipts when deciding whether automatic restore is safe; new xUnit coverage, fail closed and retain backup. Exact newest build tests/CodeQL **PENDING** at checkpoint; no binaries published or RTX acceptance asserted.
- **GPT A Point 3 (native integration):** `docs/GPT_A_NVIDIA_NGX_RESTIR_AUDIT_2026-10-09.md` verifies Vulkan/Caustica direct NGX (no OptiScaler proxy), RR/NR/FG capability checks and native hash-gated Streamline pair. Remaining GPU runtime, authorized SDK, Reflex latency and ReSTIR variance gates are explicit.
- **GPT A Point 4 (six phases):** `docs/GPT_A_SIX_PHASES_STATUS_2026-10-09.md` marks implemented source, experimental and hardware/legal blockers separately. GPT B still owns v4.0 RC/release; GPT C owns separate v4.5. No stable release advancement claimed.

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
