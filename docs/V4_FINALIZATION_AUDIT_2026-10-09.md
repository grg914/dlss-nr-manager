# DLSS NR Manager — v4.0 finalization audit (source review, 2026-10-09)

**Baseline at review:** protected `main=730d7aea7c32afb82936810da1dc56cb101a176e`, stable published/source `v3.2.0`. **Disposition: NOT READY to publish v4.0.** The record is a source/metadata audit and does not substitute for tests on the target Windows RTX PC. Preserve every earlier checkpoint in `AI_PROJECT_PROGRESS.txt`, `AI_PROJECT_PROGRESS2.txt` and `AI_PROJECT_COORDINATION.md`.

## Audit matrix

| Area | Verified observations | Status / next acceptance |
| --- | --- | --- |
| Stable fixes | Signed protected #375 B01 manager updater SHA-256/policy; #377 B02 VLC interrupted backup recovery; #380 journaling. Exact B02 Build 234/234 xUnit and CodeQL SUCCESS; postmerge 3/3 blob equality. | Integrated; physical Windows interrupted-install/rollback test still #95 |
| FR/EN UI | `UiLocalizationService` has bilingual mapping and translation change handler; source audit found untranslated French-first official-source panel/HDR strings plus cached rows not rebound. | #382 **draft, not merged**; its own exact-head Build+CodeQL pending, then signed staging and visual FR/EN dialogs/status acceptance |
| Downloads | `DownloadCenterService` lists MediaEngine/VLC/Real-ESRGAN/AI Studio model/AI-origin. Single Redownload button becomes Update only for validated newer media or eligible manager-owned model package. | #111 reopened; missing per-component trusted receipts and optional update eligibility for remaining supported components. Never auto-install raw upstream releases |
| Official source availability | All 25 public GitHub repository commit references from `third_party/DEPENDENCIES.lock.json` resolved to their **exact** SHA by GitHub API (including two GitHub sources disabled in updater policy). Public source IDs are one-to-one with `UPSTREAMS.json`; restricted NVIDIA DLSS SDK local-only. | Confirmed source revision reachability **only**. Version-update detection is read-only, promotion follows reviewed license/manifest/asset SHA policy |
| Models | `manifests/ai-studio-models.json` contains ten named model sources. Restricted FLUX dev/Qwen/LTX paths are manual-license imports; experimental ComfyUI/Python AI Studio work stays in v4.5/v4.6 lanes. | Source pages identified; model weights' actual download/hash, licenses per permitted distribution, CUDA/VRAM execution not validated. No v4.5 runtime merger |
| Latest release assets | GitHub `v3.2.0` (2026-10-07) has 29 nonzero asset sizes and 29 `sha256:` digest fields; `runtime-seed-v1` has 27 nonzero digest-bearing assets. | Metadata present, actual bytes/extraction/runtime not tested. `SBOM.spdx.json` and `release-provenance.json` absent from v3.2.0 release asset list; current `.github/workflows/release.yml` produces them for future release |
| Packaging/tests | WPF `.NET 8` win-x64 self-contained single-file source; `tests/DlssNrManager.Tests/` xUnit .csproj references main app and `tests/CrashProbe`. Tests are not shipped in executable by design. Repository contains `docs/RELEASE_CHECKLIST.md`, provenance/licensing policies and CI jobs. | Confirm final v4 publish archive, SBOM, SHA256SUMS, provenance, source integrity, FR/EN docs and rollback on an actual produced release; do not bump/tag prematurely |
| START/RUN/STOP/CLEANUP | `ExternalProcessTracker` starts app-owned helpers in Windows Job Object with kill-on-close and reject-late-start gate; tracked media/AI/Java processes. `MainWindow.Closed` cancels active CTS; `App.OnExit` calls shutdown and emits log. `ProcessLaunchPolicyTests`, `NormalManagerShutdownTests`, `ForcedManagerTerminationTests` exist. | CI and source ≠ full Windows PC proof. Run `tools/verify-v4-windows-process-cleanup.ps1`, plus active Download Center interruption, VLC, GPU VRAM/RAM, ports, file locks/junctions, disk-full, orphan descendants; #95 OPEN |
| Licensing/security | Existing SHA, lock, manifest and ruleset guard. Microsoft OpenMP `vcomp140.dll` REDIST provenance and legal right remain unresolved #79, with fail-closed omission required. | #79 OPEN; never copy arbitrary System32 DLL or claim redistributable approval |
| Roadmap lanes | GPT A adaptive GPU #353 and NVIDIA Driver Health #381 are isolated v4.5 drafts; GPT C AI Studio and ComfyUI inference are experimental v4.5/4.6 drafts. | Do not merge experiments to stable v4 without semantic review, fresh checks, signed promotion and hardware validation |

## Physical Windows checklist (issue #95)

Use [V4_WINDOWS_PROCESS_ACCEPTANCE.md](V4_WINDOWS_PROCESS_ACCEPTANCE.md) on the target machine. For each actual app-owned helper run: START, active RUN, user STOP or app close, CLEANUP verification. Capture identity/PID/tree, managed temporary files, downloads, output consistency, post-shutdown ports, RAM and NVIDIA GPU VRAM, and final diagnostic logs. Repeat with normal close, cancellation, app forced termination, network disconnection, extraction failure, insufficient disk, locked files and blocked junctions. Intentionally detached user-initiated apps (Explorer/browser/game/installer/elevated repair/self-update handoff) are **documented exceptions**, not arbitrary orphaned children to kill by process name.

## Release gate

No v4 tag, user release or authorization follows from this document. Require: (a) #111 verified optional per-component download/update/rollback behavior, (b) #95 target hardware acceptance, (c) #79 approved signed REDIST or safe omission, (d) all FR/EN paths/UI acceptance, (e) exact-main signed Build/CodeQL+xUnit plus end-to-end packaging with SBOM/provenance/digests, and (f) owner release approval. Historical branches and experimental PRs remain until independently reconciled and safe to retire.

**Evidence boundary:** GitHub file contents, live issue/PR states, GitHub release metadata, matching locked commit endpoints and prior exact-head CI are the verified facts. No physical test, actual binary/model retrieval, benchmark or release creation has been performed by this audit.


## Operational handoff — 2026-10-09 (prioritized, no v4.0 release claim)

The items below distinguish **verified source/CI facts** from **acceptance pending**. Do not turn draft PRs or GitHub upstream notifications into advertised shipped features.

| Priority | Required work | Evidence that closes it | Owner/scope |
| --- | --- | --- | --- |
| P0 | #382 bilingual static labels, local-only dynamic list rebinding, tests | Exact current HEAD Windows Build/xUnit and CodeQL success, code review, GitHub-signed source-equivalent stage, protected serial squash and FR/EN Windows UI walkthrough | GPT B v4; prior source HEAD failed xUnit2031; fixed HEAD `5a006ccb150d7be5e238c9259645562f8cac5461` not yet validated |
| P0 | #383 README/CHANGELOG/audit/checklist and all three shared journals | Review no stale claims, preserve all previous entries and reconcile #382/#383 overlapping `AI_PROJECT_PROGRESS.txt` appends; signed latest-main staging, Build+CodeQL, protected merge | GPT B docs-only draft; no merge |
| P0 | #111 optional version-aware updates | Immutable approved release/asset ID+SHA/version receipts for remaining **eligible** components; one action, user confirmation, offline/no-new-version safe state, transactional rollback and tests. No install from arbitrary GitHub upstream notice | v4 Download Center |
| P0 | #95 Windows runtime/lifecycle acceptance | Real Windows 11 + supported RTX: start→run→stop→cleanup for all installed manager-owned helpers; normal/forced exit, cancellation, background download stop, ports, handles, RAM/VRAM, file locks/junctions, disk full, temp/output preservation, restart recovery and final log evidence | User PC; CodeQL/xUnit insufficient |
| P0 | #79 Microsoft OpenMP redistribution | Licensed, signed REDIST version/hash/source and legal rights or exclude OpenMP runtime while preserving fail-closed behavior, plus Real-ESRGAN x64 device regression | Binary licensing / owner |
| P1 | Whole-app safety, compatibility and performance | FR/EN UI and error/confirmation audit, CPU/RAM/VRAM/latency under workloads, all supported RTX feature gating, offline PC-new-install runtime audit, no unnecessary network checks, actual download/asset-hash validation | v4 release candidate |
| P1 | Final production release | Exact-source Build+CodeQL+xUnit, deterministic win-x64 self-contained ZIP/EXE, `SHA256SUMS.txt`, `components-manifest.json`, `SBOM.spdx.json`, `release-provenance.json`, required licenses/notices/signatures, checked published assets then user release approval | Stable main only |
| Excluded | AI Studio Python/CUDA/ComfyUI runtime and GPU profiles / driver-health experiments | Separate v4.5+ validation (GPT A #353/#381, GPT C #285 + experimental PRs), never silently backported | Future lane |

**CI finding from live run:** #382 head `b2da747830078b4f7225baf00f6134a31c926e9c` failed the Windows Build step before tests executed because xUnit analyzer `xUnit2031` rejected `Assert.Single(query.Where(predicate))` in `LocalizedOptionTooltipTests.cs`. New branch head `5a006ccb150d7be5e238c9259645562f8cac5461` replaces it with `Assert.Single(query, predicate)`; the new head requires fresh CI. Treat the earlier fail as a fixed source candidate, not as passing regression evidence.

**Test placement:** `tests/DlssNrManager.Tests` is the xUnit project and `tests/CrashProbe` is its separate process-termination surrogate; the release single-file application must **not** embed these test source files. Test **execution and success** must be evidenced from exact candidate workflow logs; existence in GitHub does not suffice.

**Release metadata naming:** `.github/workflows/release.yml` writes `release-assets/SBOM.spdx.json` and `release-assets/release-provenance.json`. The previous release checklist named an absent `SOURCE-SBOM.spdx.json`, now corrected on this draft branch; do not assume a v4 asset was uploaded. `v3.2.0` does not contain standalone SBOM or provenance assets; preserve immutable old release.

**Checkpoint scope:** all statuses are as of the live source, issue, PR and workflow observations on 2026-10-09. No test was executed on the user's GPU, no byte-level model/binary installation was verified, and neither v4 RC nor release has been published.


### Latest FR/EN correction — source candidate only
The first #382 language patch introduced two aliases that conflicted with **existing** `UiLocalizationService` dictionary keys. The current source HEAD `ce5e9a24882b86aad0cc75b8cd6f8fa06728a82b` removes the redundant HDR/download rules pairs, standardizes the HDR XAML literal and fixes both a theory parameter inversion and xUnit2031 compile failure. The **current** additive translation count is **7 unique pairs**. Static 7/7 case lookup verification succeeded, but source-only checks do not count as Windows GUI or CI acceptance; Build [37988726456](https://github.com/grg914/dlss-nr-manager/actions/runs/37988726456) and CodeQL [37988726457](https://github.com/grg914/dlss-nr-manager/actions/runs/37988726457) must complete for that exact HEAD. The earlier nine-pair snapshot is historical and superseded.

## Six-stage execution checkpoint — 2026-10-09 (post #386, #389 source draft pending)

**1 — Secure merge: COMPLETE.** #386 signed protected merged `ed77e0e2fbde9b833c8fd6ec82e36b0caffe4794`, 244/244 xUnit, Build+CodeQL success, deterministic executable ZIP, 11/11 file blob parity and exact ancestry; #382/#383 old drafts closed without additional merge. No new dependency or experimental feature landed.

**2 — Download Center: PARTIAL / BLOCKED #111.** Existing approved optional Update action already covers media and permitted manager-owned AI Studio models. VLC/Real-ESRGAN/AI-origin lack safe immutable installed-version/approved-new-release receipts. Do not add a generic upstream install button or mislabel Redistributable/model licenses.

**3 — Lifecycle: SOURCE FIX PENDING #389 / DEVICE #95.** Signed source HEAD `03a256c8fa1d34c9e79f70f4b1dc9d739be8a03d` cancels otherwise untracked on-demand HTTP version checks when window closes; tests added; exact signed Build+CodeQL required before merge. Physical Windows processes, RAM, VRAM, ports, active downloads, disk/full locks, backup rollback and final logs must still be tested.

**4 — Security/dependencies: GUARDED / LEGAL GATE #79.** Existing release workflow already refuses unauthorized OpenMP publication; candidate must come from licensed Visual Studio x64 VC/Redist, valid Microsoft Authenticode and SHA provenance. Must establish operator rights separately, never assume System32 is redistributable; no new binary or model licensed/approved by this audit.

**5 — Regressions/performance: CI PARTIAL / HARDWARE PENDING #95.** #386 signed CI tested 244/244 and reproducible ZIP; #389 checks pending, full FR/EN visual passes, target NVIDIA VRAM/RAM/leak/CPU throughput, offline failure modes not tested on user's PC.

**6 — Release preparation: SOURCE AUDITED / PUBLICATION BLOCKED.** `DlssNrManager.csproj` and published release remain 3.2.0; GitHub v3.2.0 29 assets and runtime-seed-v1 27 assets show SHA metadata but no byte-level installed runtime proof. Current release.yml generates `SBOM.spdx.json`, `release-provenance.json`, `SHA256SUMS.txt`, component manifest. Do not bump/tag/publish v4 until gates #111/#95/#79 + precise package validation and explicit operator approval.

**Audit method:** current live GitHub source/CI/issue/release metadata and official Microsoft redistribution guidance, not real Windows RTX execution. Source PR #389 and this documentation branch BOTH append AI_PROJECT_PROGRESS.txt: preserve both unique histories in later source/signed stage.
## GPT B reconciliation checkpoint — 2026-10-10
Stable protected main `b727d5f35e5e9212cb5a854b531e6071a65ab72f` includes signed #386 and #389, with current `build` and `Analyze C#` SUCCESS. This documentation and six-stage plan was restored onto fresh main from the separate stale #390 branch, preserving **both** old #389 and independent #390 append-only progress histories. Source PR does not imply hardware validation, completed #111/#95/#79, or release authorization. The prior #389-pending references in historical snapshots reflect their authored time; the latest source fact is #389 MERGED. GPT A/C experiments remain separate.
