# V4 CLEANUP CANDIDATES — inventory before any deletion (2026-10-10)

**Scope:** V4 stable. No deletion, force-push, rebase or release modification performed by this document. Every candidate must be re-evaluated against current main and owner-approved V4.5 archives.

| Candidate | Reason | Dependencies / reference | Risk if deleted | Proposed action |
| --- | --- | --- | --- | --- |
| Historical non-merged V4.5 / V4.6 PR branches | Stable V4 must remain separate | Private `V4.5/transfer-manifest.json`, existing 53-file provenance | Loss of experimental functionality/history | Keep until owner and GPT C verify exact private mirror, then close only redundant public PRs without deleting unique source |
| Old journal-only PRs #169 #178 #191 #220 #271 #339 #345 | Duplicate/redundant control plane | Three shared progress files, GPT A/C handoff | **Unique text not verbatim on main**; losing audit evidence | Preserve, analyze facts and append an archive before any closure |
| `docs/live-ai-project-progress` | Very stale divergence, 199 commits ahead / 96 behind at inspection | Shared progress journals | Overwrites recent valid main logs | Retain reference; semantic diff and approved archival only |
| `refactor/video2dlssnr-dynamic-ngx` | Extensive source/native divergence | Restricted NVIDIA SDK, video pipeline and CI policy | Binary provenance loss and runtime regression | Retain until dependency/security review, never blanket-merge |
| `automation/upstream-sync-37588055056` and `-37589917835` | Stale vendored FFmpeg source branches | Dependency locks, source refresh | Could silently repin huge vendor trees | Retain, audit exact refs then mark obsolete if newer locks already dominate |
| `fix/phase3-bootstrap` | Old CI/runtime handling | Offline producer workflows and seed policies | Reintroduction of upstream/download bypass | Compare targeted 6 paths and guardrails; no direct merge |
| `fix/seed-consistency-and-continuity` | Historical workflow patch | `download-runtime-seed.ps1`, monitoring, continuity | Regression to integrity/TOCTOU behavior | Verify current fixes before any cleanup |
| Content-addressed OptiScaler ZIP candidates | Different builds of same functional tag | Canonical locked runtime seed; issue #161 | Deletes reproducibility evidence or shifts consumer | Keep; compare native PE/PDB bytes and approve one immutable revision |
| Historical `v3.2.0` Real-ESRGAN .param assets | Five LF→CRLF transformed files | Pinned application sizes/Git blob SHA1; issue #411 | Destructive release overwrite; breaks audit provenance | **Never delete/overwrite old release**; publish corrected vetted bytes only in a future new immutable release |
| WPF markup mockups and tests | Not necessary in installed app | Code/test/documentation references | Breaks CI/visual provenance if removed from repo | Leave in repo, exclude from production zip/compilation as already done |
| Temporary app-managed caches | Can rebuild when no task uses them | Active downloads, transaction backups, model refs, outputs | User data loss or damaged rollback | Only through existing safe targeted cleanup with explicit selection, never repo-wide automatic deletion |
| Existing process tracker | Already owns Windows Job Object lifetime | App lifecycle; normal/force shutdown tests | Duplicate managers can deadlock or orphan helpers | **Keep existing implementation**, augment source/physical tests rather than duplicate |

No candidate is authorized for deletion by this checklist alone. Preserve user projects, outputs, manual licenses, system drivers, native runtime cache not clearly owned by this app, and all stable published release assets.
