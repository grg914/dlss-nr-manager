# AI Studio v4.6 — Offline Job lifecycle groundwork

Experimental GPT C implementation on an isolated v4.6 feature branch stacked
on reviewed experimental v4.5 AI Studio model-presence work. **This does not
install, run, activate, download or approve Python, ComfyUI, models or GPU
inference.** Protected `main`, stable Manager v4.0 and Caustica remain untouched.

## What this stage adds

- `AiStudioJobLifecycleService`: explicit persistent statuses
  `Queued -> Starting -> Running -> Completed`, plus
  `Cancelling / Cancelled / Failed / Interrupted`, with an allowed-transition
  table. A completed job requires a real, nonempty output file under its
  already selected output folder. No green status from HTTP 200 alone.
- Manual `MarkOrphanedJobsInterrupted` reads saved job manifests and changes
  previously active jobs to `Interrupted`. It **never restarts an engine**,
  retries a failed job or silently treats an orphan as completed.
- Per-instance locked mutations and existing `AtomicFile.WriteAllText` avoid
  partially written files during normal in-process transitions.
- Two optional fields `UpdatedAt` and `ResultFile` on the existing
  `AiStudioJob` record preserve deserialization of old job JSON.
- New queues are also written with the existing atomic file helper.
- Output paths and the job directory reject reparse points; only common
  image/video result extensions are admitted.
- Dedicated synthetic regression tests cover valid and invalid transitions,
  result-file proof, path escape, terminal state locking, corrupt manifests,
  offline recovery and backwards-compatible JSON.

## Deliberate limitations

The service is not wired to a runnable GPU process, so **image generation is
still unavailable** and current queued jobs cannot be started by the user.
This is the safe execution contract to reuse once issue #285 is unblocked:
approved ComfyUI runtime provenance/licences, independently trusted file
closure, owned Python process, local API/version verification, actual workflow
nodes, result validation, tests on Windows RTX 5060 Ti and real disconnected PC.
Do not add a Generate/Run action that claims success without those gates.

The service currently serializes operations only within one instance; before
wiring a multi-process runner, explicitly choose single-manager ownership or
an OS-wide lock. Do not create a second job coordinator without evidence.

## Checks required before any merge

Windows CI and xUnit on exact final head, CodeQL, independent review and
change-scope audit. Merge only into an authorized **experimental v4.6**
integration branch, never stable main. CI-only mirrors to main must never merge.
