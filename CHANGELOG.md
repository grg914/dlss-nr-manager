# Changelog

All notable release changes should be recorded here. This file distinguishes **merged code** from **unreleased plans**. Source version and latest published GitHub release are separate facts.

### 2026-10-10 — V4 source audit and integration gates
- Integrated through protected signed PR #422: optional self-contained V4 **local developer test** builder for a PC without an installed Manager; not a distributable release artifact.
- Integrated through protected signed PR #424: read-only local Microsoft VC++ x64 detection with FR/EN UI, no third-party installer/binary distribution; old #340/#341 superseded.
- Protected signed [#426](https://github.com/grg914/dlss-nr-manager/pull/426) **MERGED** into `83952a65d6bdbaa38951b23c8dba7e6df5ccb195`: central log levels TRACE/DEBUG/INFO/WARNING/ERROR/CRITICAL, common credential redaction, missing FR/EN labels, structured fatal dispatcher diagnostics and xUnit checks. Corrected initial CS0103 error and reran exact-head Build/CodeQL (303/303 xUnit PASS). Hardware/RTX and release gates remain open.
- Checked 25 pinned upstream GitHub source commits and 10 model source pages; binary/LFS availability, model hashes, hardware GPU and licensing remain unverified. Five old Real-ESRGAN .param mismatch #411 and optional update expansion #111 remain explicit blockers.
- New audit and cleanup candidate inventory under docs/, plus retrospective AI_PROJECT_PROGRESS3.txt; no destructive branch/file cleanup and no v4 release.

### 2026-10-10 — V4 general re-audit follow-up (not yet published)
- Re-verified 25/25 pinned public upstream GitHub commit SHAs and reviewed FR/EN, startup/shutdown ownership, downloads, runtime package exclusions and safety gates.
- Identified support ZIP export risk: untrusted OptiScaler.log/INI/JSON may contain sensitive credentials despite user-path masking. Separate **candidate** fixes central ZIP-entry redaction and expands xUnit external-log secrecy tests; final merge status is tracked in GitHub.
- Preserved V4.5 experimental separation and the existing closed/active PR ledger; no mandatory update, no release tag, no license/RTX acceptance claim.

### 2026-10-10 — Real-ESRGAN #411 exact-byte repair candidate
- Verified 5/5 historic CRLF `.param` SHA-256 digests match a byte-exact LF→CRLF transform of the pinned official source, with no added model options or architecture changes.
- Candidate pins all 12 model download URLs to immutable `v3.2.0` and their published SHA-256 and byte sizes, restores LF exclusively for these five recognized source models, and retains canonical Git blob checks and destination rollback on error.
- Added direct xUnit repair, source/release integrity and tampering regression tests plus a reproducible provenance report; **not a published V4 release** and on-device Vulkan/RTX tests pending.

## [Unreleased] — 4.0 readiness (not shipped)



### Latest six-stage completion checkpoint (2026-10-09)
- Stage 1 completed: protected signed #386 merged `ed77e0e2fbde9b833c8fd6ec82e36b0caffe4794`; superseded source drafts #382/#383 closed with append-only history preserved.
- Stage 2 incomplete #111: optional version receipts and validated immutable newer packages for VLC, Real-ESRGAN, AI-origin not yet supported; media and permitted model updates already work, no raw upstream install.
- Stage 3 source hardening staged #389 signed `03a256c8fa1d34c9e79f70f4b1dc9d739be8a03d` (NOT MERGED; CI pending): cancel explicit update/official source version lookups at exit; physical Windows / GPU / file locks #95 still needed.
- Stage 4 licensed OpenMP gate exists in `runtime-refresh.yml` but operator redistribution rights and actual Real-ESRGAN acceptance #79 unresolved; source reachability not binary verification.
- Stage 5 device-level GPU/RAM/VRAM/performance and shutdown acceptance pending; stage 6 v4 packaging not yet authorized. Project/release remain 3.2.0; latest v3.2.0 29 digest-bearing release assets lack `SBOM.spdx.json` and `release-provenance.json`, which current release workflow generates for future releases.

### 2026-10-09 protected stable source and remaining audit gates

- Protected [#375](https://github.com/grg914/dlss-nr-manager/pull/375) integrated mandatory full SHA-256 and canonical asset allowlisting for executable manager updates; [#377](https://github.com/grg914/dlss-nr-manager/pull/377) integrated crash-recoverable VLC GUID/legacy backups. Both underwent signed exact-head Build/CodeQL and protected squash; #377 Build had 234/234 xUnit. [#380](https://github.com/grg914/dlss-nr-manager/pull/380) preserved the signed B01/B02 evidence in shared coordination journals.
- Reopened [#111](https://github.com/grg914/dlss-nr-manager/issues/111): only selected media/redistributable models offer validated opt-in Update; other managed components require authoritative version receipts, allowlisted promoted assets, and rollback tests. Read-only upstream discoveries must not silently install anything.
- Confirmed 25/25 locked public GitHub commit references currently exist and published `v3.2.0` has 29 non-empty assets with GitHub SHA-256 digest metadata. Binary bytes, real Windows runtime, model downloads and GPU performance remain **unverified**.
- Published `v3.2.0` lacks standalone `SBOM.spdx.json` and `release-provenance.json` assets despite the current release workflow generating them for future publications; these are explicit v4 packaging checks, not grounds to mutate an immutable older release.
- FR/EN static and dynamic Download Center labels were integrated in **signed protected [#386](https://github.com/grg914/dlss-nr-manager/pull/386)**, exact signed-head Windows Build **244/244 xUnit** and CodeQL SUCCESS; 11/11 blob identity and deterministic package validation. Physical Windows/RTX shutdown and recovery [#95](https://github.com/grg914/dlss-nr-manager/issues/95), Microsoft OpenMP legal provenance or omission [#79](https://github.com/grg914/dlss-nr-manager/issues/79), end-to-end source/model validation, FR/EN visual acceptance and final release controls remain outstanding. No v4.0 tag or assets published.



### Merged and CI-validated changes (2026-10-08)
- PR #99: restrict NVIDIA-SMI execution to trusted installed locations and track helper processes.
- PR #100: unify media update rollback and conservative recovery of legacy media backups.
- PR #101: confine managed AI Studio installs to the selected model directory and add backup recovery allowlists.
- PR #102: protect pre-existing manually imported/licensed model data when initial backup move fails.
- PR #105: clean temporary AI Studio extraction staging on failure, and clarify FR/EN redownload rollback prompts.

### Outstanding release gates
- Complete real-device Windows crash/forced-kill/restart tests for component rollback, model backup recovery, subprocess shutdown, GPU/VRAM and disk cleanup.
- Audit junction/symlink parent paths, ambiguous historical backups and concurrency/file-lock cases.
- Validate all requirements in the user-provided prompt.txt: hardware profiles, driver-change refresh, RTX 20/30/40/50 feature gating, FR/EN text and tooltips, optional updates in Download Center, dependency availability/licensing and complete regression coverage.
- Verify actual packaged assets, reproducible Windows x64 build, installer/runtime and release workflow results.
- Resolve open release blockers and record final evidence in AI_PROJECT_PROGRESS.txt before tagging 4.0.

> **No 4.0 release is declared by this changelog.** Passing CI alone is not an end-to-end Windows hardware acceptance test.

## Source version 3.2.0

The version declared in `DlssNrManager.csproj` at the time this changelog was introduced is `3.2.0`. Consult GitHub Releases independently for the most recently published package; this section does not claim that a corresponding binary was published.

## Historical feature reconstruction from pre-journal README

These are **README-documented code capabilities**, not newly implemented changes and not independent proof of every shipped archive. The source baseline is `0f8eb7c`, immediately before `AI_PROJECT_PROGRESS.txt` was introduced on 2026-10-07; full version-by-version source links and outstanding release evidence are recorded in [AI_PROJECT_PROGRESS2.txt](AI_PROJECT_PROGRESS2.txt).

### v3.1.0 code documented before the journal
Manager-owned verified runtime release channel for OptiScaler, FFmpeg, Real-ESRGAN, ReShade, Streamline/video2dlssnr, AI models and Minecraft 26.2; offline pinned/vendor source workflow; deterministic Caustica RTX / Fabric / performance assets; NVIDIA signing/provenance checks and local-only SDK restrictions.

### v3.0.0
Integrity manifest for manager-owned files, bounded retry and better diagnostics; Quick/Balanced/Thorough ONNX AI-origin analysis, conservative confidence/provenance interpretation, cancellable sampling and JSON export.

### v2.1.0
Per-game transactional install recovery and bounded history, renderer detection and DXVK/vkd3d, configurable launcher/game roots, executable selection, game launch and restore controls, software WPF rendering fallback and more regression tests.

### v2.0.0
Independent AI-origin UI, Minecraft managed-install detection and restore, support-bundle export, hardened launcher scanning and ZIP extraction, atomic game artwork cache and release SHA256 manifest.

### v1.5.1 and v1.5.0
NVIDIA multi-GPU detection and UI race fixes; safe anti-cheat and reparse scans; stronger model/download/installation verification. Production `DlssNrManager Menu` with side navigation and original WPF controls retained.

### v1.4.2, v1.4.1, v1.4.0 and v1.3.0
Windows Job Object process lifecycle and single-instance guard, safe ZIP/file handling and runtime trust; independent ONNX AI-origin detection; RTX generation capability gating; official NVIDIA Neural Rendering source-discovery checks with non-fabrication of proprietary DLLs.

### Earlier foundation
Merged GitHub PRs #1–#12 and the pre-journal README provide evidence of foundational application work (OptiScaler installer, game detection, PC update, AI upscaling, Minecraft RTX, Scandi assets, AI-origin detection). The precise original release archive contents and a first-version tag have NOT been independently reconstructed here. Do not invent an earlier numbered version or a release date.

### v4.0 status (still unreleased)
Hardware & Profiles, central Downloads, optional media/model updates, VLC VSR-HDR, AI Studio model catalog/jobs and multiple runtime-seed hardening PRs are present in main or PRs. Main source is currently 3.2.0. Source presence is not full E2E validation. The remaining issues and manual Windows/NVIDIA/license checks are specified in `AI_PROJECT_PROGRESS2.txt`. NVCleanstall and an integrated full benchmark were removed from scope on 2026-10-09 (PR #143).

