# DLSS NR Manager v4.5 — Proposed roadmap (GPT C)

> Status: draft, not released. Ownership: GPT C. This plan is independent of v4.0 stabilization and Caustica experimentation.
> Baseline on 2026-10-09: main 05a8b4c637d1d4209d0e6d64d7ae3c2eb68c436f; refresh before integration.

## Verified product scope

- `docs/LOCAL_AI_STUDIO.md` explicitly lists Python/PyTorch private runtime packaging, actual ComfyUI API execution, Diffusers execution, generation progress/cancel/retry, preview gallery, VRAM-aware scheduling, workflow management and model/runtime assets as **not yet complete**.
- `docs/AI_RUNTIME_SECURITY.md` requires an isolated pinned Python/wheelhouse, no global mutation, no script interpolation, offline model loading, loopback-only ComfyUI APIs, restricted-model license acceptance and process-tree shutdown.
- `docs/VIDEO_BLUR_RESTORATION_EVALUATION.md` and issue #76 reserve temporal video restoration for further research, model/license validation and hardware tests, not an immediate production dependency.
- The existing AI Studio UI/catalogue/job persistence is not evidence of runnable inference. The v4.0 release gates remain with GPT B; Caustica/ReSTIR/LOD/shaders remain with GPT A.
- Confirmed exclusions: NVCleanstall integration and a full built-in benchmark are not reintroduced.

## Prioritized increments

| Order | Increment | Prerequisites | Complexity | Principal risk | Acceptance and tests |
| --- | --- | --- | --- | --- | --- |
| P0 (this draft) | Read-only job execution preflight; bilingual, per-job missing dependency diagnostics | Current job catalogue and queue; no runtime assets | Low | Mistaking file presence for verified execution | Unit tests for invalid catalogue/task/backend/paths, absent Python/model/backends, FR/EN messages; present files NEVER claim runnable or start code |
| P1 | Versioned and verified private Python + pinned PyTorch/CUDA wheelhouse packages through Downloads | Approved immutable seed assets, SHA-256/manifest, redistribution/license and disk approval | Very high | Supply chain, CUDA compatibility, massive artifacts | Offline install and hash tests, no global Python/PATH/pip mutation, rollback/recovery, fresh Windows RTX PC acceptance; do not publish unapproved packages |
| P2 | ComfyUI local execution adapter and bounded job-state machine | P1, pinned reviewed workflows/nodes, process ownership from v4 | High | Python execution, API exposure, stuck subprocess, prompt leakage | Loopback bind, explicit executable, job cancellation/timeout/recovery, model license checks, no hidden downloads, fault injection and Windows GPU tests |
| P3 | Diffusers direct executor for supported catalogued tasks | P1 and reviewed per-model pipelines | High | Pipeline incompatibility, VRAM exhaustion | Offline deterministic fixtures, task/backend matrix, no pickle execution by default, smoke test on real GPU |
| P4 | Generation progress/cancel/retry and local preview gallery | P2/P3 stable output contract | Medium | Partial files or deletion of user data | Atomic job status, interruption recovery, preserve existing outputs, localized UX and end-to-end cancellation tests |
| P5 | Optional VRAM-aware scheduling and workflow import/export | Executable/validated backends and real telemetry | High | Out-of-memory, untrusted nodes/scripts | Fail-closed capability matrix, queue fairness and per-profile Windows GPU acceptance; pinned allowlist for workflow nodes |
| Research gate | Temporal deblur (RealBasicVSR / BasicVSR++) under issue #76 | Reviewed checkpoint/weights/licenses and separate runtime budget | High | Flicker/hallucination, redistribution, GPU costs | Temporal quality and performance comparisons with provenance; no default x4 or automatic promotion |

## P0 implementation contract

- `AiStudioJobPreflight.Inspect` is **read-only**. It reports missing inputs/models/private Python/backends for existing saved jobs.
- Both FR and EN messages are part of the same diagnostic result; UI may display the result without creating downloads or launching processes.
- A directory or executable existing on disk only means 'found', not trusted. A returned `AwaitingVerifiedExecutor` explicitly blocks any inference claim until an approved hash/signer/license-bound executor exists.
- No changes to `QueueJob`, model imports, runtime update paths, signed artifacts, third-party dependencies or stable release.

## Global gates for later PRs

1. Use separate `feature/v4.5-*` branches and Draft PRs; refresh exact main, coordination, PR ownership and current SHA.
2. Do not cherry-pick or merge an unfinished v4.0/Caustica branch. Shared dependencies must be coordinated with GPT A/B first.
3. Required Build, Analyze C#, CodeQL and focused tests on the final candidate SHA; physical Windows/RTX and binary-license acceptance remain separate gates.
4. Preserve signed-commit/ruleset requirements; protected squash merge only after explicit authorization. Never publish v4.5 from a feature branch.
5. Do not edit any ScandiCraft repositories.

## Initial PR checkpoint

- Branch: `feature/v4.5-ai-studio-job-preflight-20261009` (based on main 05a8b4c).
- Planned files: a read-only preflight service, xUnit tests, non-executing Jobs UI diagnostic, this roadmap, and a minimal coordination update.
- Build and CodeQL: PENDING until exact-head CI finishes. No physical GPU/inference validation or signed release is claimed.
