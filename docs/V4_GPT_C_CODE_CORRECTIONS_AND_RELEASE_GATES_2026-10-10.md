# V4 — GPT C: code corrections, CI evidence, release gates
Date: 2026-10-10 (Europe/Zurich)
Scope: **V4 only**, source correction, native reproducibility, Download Center policy, and release readiness. This file must never be interpreted as approval of an unreleased binary or an NVIDIA/RTX physical test. GPT A's V4.5 web-games/driver/profile branches and GPT C's V4.5/4.6 AI Studio experiments remain separate.

## Authority and immutable reference points

- Owner repository: `grg914/dlss-nr-manager`. Protected `main` at audit: `4c6d8f1b6202fd57db5dae22b5bf9bc5b03ace1a`.
- GitHub ruleset `24538398`: signed commits, linear history, protected PR review, required `build` and `Analyze C#`. Never force-push `main`, bypass rules or merge unsigned staging commits.
- Released `v3.2.0` and `runtime-seed-v1` are historical immutable reference assets. No clobber/overwrite/silent consumer redirect.
- V4 combined source: [PR #440](https://github.com/grg914/dlss-nr-manager/pull/440), head `2bf12c1880af397ba9d84c3a591eb64688cfffda` at this audit, **not merged**.
- OptiScaler source/diagnostic: [PR #441](https://github.com/grg914/dlss-nr-manager/pull/441); model-version fix [PR #442](https://github.com/grg914/dlss-nr-manager/pull/442) is represented **byte-identically** in combined PR #440, so do not double-merge #442.

## PHASE: integrated application / Real-ESRGAN / Download Center

STATUS: candidate source is CI-green, NOT the released V4.

MODIFICATIONS: PR #440 consolidates #435 (OpenMP removal only from Real-ESRGAN/ncnn, Vulkan kept; no `vcomp140.dll` packaging), #436 (immutable AI-origin ONNX URLs), #437 (Minecraft 26.2 C2ME lock/FR-EN consistency), #439 (safe WPF Close/cancellation source ownership, child process shutdown), and an additional Windows Real-ESRGAN `swprintf`/wrong-wide-pointer fix with regression test. It also contains the equivalent stable-version normalization from #442: `v1.0`, `1.0.0` and `1.0.0.0` no longer cause spurious optional AI Studio model updates.

TESTS: exact-head #440 Build [38033347267](https://github.com/grg914/dlss-nr-manager/actions/runs/38033347267) SUCCESS, xUnit **331 passed / 0 failed / 0 skipped**, deterministic application ZIP A/B SHA-256 `3b160af34d944c05941079571d4e60832e9ac5e6675fbbcc3928eeed87372686`. CodeQL [38033347301](https://github.com/grg914/dlss-nr-manager/actions/runs/38033347301), Real-ESRGAN OpenMP-free [38033347271](https://github.com/grg914/dlss-nr-manager/actions/runs/38033347271), FFmpeg runtime-seed safety [38033347300](https://github.com/grg914/dlss-nr-manager/actions/runs/38033347300), runtime-seed writer [38033347274](https://github.com/grg914/dlss-nr-manager/actions/runs/38033347274): SUCCESS. #442 Build [38032344090](https://github.com/grg914/dlss-nr-manager/actions/runs/38032344090) and CodeQL [38032344089](https://github.com/grg914/dlss-nr-manager/actions/runs/38032344089): SUCCESS.

PROBLEMS REMAINING: staging PR #440 HEAD is not GitHub Verified-signed. Protected merge needs one owner-signed commit with the exact tree from its [updated signing comment](https://github.com/grg914/dlss-nr-manager/pull/440#issuecomment-6094508820), followed by exact signed-head Build/CodeQL and review. No private key should be copied to agents or issues.

## PHASE: OptiScaler #161 — reproducibility, publisher immutability, compiler ABI

STATUS: package reproducibility proven within two local CI rebuilds; equality with previously published native seed **not** proven.

PROBLEM FOUND: original runtime refresh detected a same-version OptiScaler ZIP conflict and correctly rejected it; that is not license to overwrite an asset. The previous `Compress-Archive` hypothesis became stale because main already uses `create-deterministic-flat-zip.ps1`. On PR #441, an earlier native CI job printed `PASS: OptiScaler native inputs and final ZIP are identical across two clean rebuilds` but ended with exit status 1, consistent with stale PowerShell `$LASTEXITCODE=1` after successful `robocopy` (exit codes 0–7 can indicate success).

FIXES: source-receipt-based deterministic build date, `/Brepro` for native DLLs, runtime ZIP excludes non-runtime forwarder PDB while preserving debugging artifacts in the builder workspace, per-file SHA-256 and COFF time stamps on two fresh builds, fixed ZIP timestamp/entry ordering, explicit `$global:LASTEXITCODE = 0` only after all comparisons succeed, and native CI triggers for vendored OptiScaler and package producer source changes. See `tools/build-optiscaler.ps1`, `tools/verify-optiscaler-native-repro.ps1`, `.github/workflows/optiscaler-native-repro.yml`.

TESTS: PR #441 head `9158d84cf34bd139c955a36bf1b59bdede228d33` had four SUCCESS workflows: Build [38035089815](https://github.com/grg914/dlss-nr-manager/actions/runs/38035089815), CodeQL [38035089862](https://github.com/grg914/dlss-nr-manager/actions/runs/38035089862), deterministic package safety [38035089820](https://github.com/grg914/dlss-nr-manager/actions/runs/38035089820), native reproducibility [38035089841](https://github.com/grg914/dlss-nr-manager/actions/runs/38035089841). Both rebuilds in this native job generated the same OptiScaler package SHA-256 `094cac907b60aa8b4d19cbfbde999ab23767ae313f96fb6c809c65be7263d0d2` (130,614,073 bytes) and OptiScaler.dll SHA-256 `38945c0a527684f004bb740d2d762fa2459cfec7251e4e0ca715b104f3b8fac5`. The previously published canonical archive had a DIFFERENT size/hash, and remains immutable.

ABI RISK: linker emits MSVC `C4744` for `InputCommon::currently_active_tech` (`std::atomic<std::shared_ptr<LowLatencyTech>>`) with 16-byte alignment metadata in one translation unit but not another. Microsoft says C4744 means an externally linked variable has inconsistent types between /GL-compiled sources. Current exploratory `alignas(16)` patch to `third_party/OptiScaler/OptiScaler/low_latency/input/input_common.h` is **NOT an accepted fix until** the exact patched Windows native compile log contains ZERO C4744, two rebuilt DLL/package hashes agree and CodeQL/Build pass. If the warning persists, revert that experiment and investigate headers/packing rather than suppressing C4744 or claiming correctness. The issue must stay open if ABI safety is unproven.

PROBLEMS REMAINING: independently reviewed native licensing/provenance, PE/ABI and runtime behavior on Windows RTX, content-addressed NEW package identity plus reviewed SHA-256/size consumer metadata promotion and rollback. A successful reproducibility test is NOT a published asset approval.

## PHASE: branches, issues and physical acceptance

BRANCH/PR STATUS: earlier census found 199 branches and 29 open PRs *before* #441/#442; branch inventory changes over time and must be refreshed before deletion. Historic unique diverged refs (FFmpeg/NGX and old docs) must NOT be deleted or bulk-merged without semantic diff and consumer audit (issue #124).

RELEASE BLOCKERS (remain OPEN unless hard evidence resolves them):

- [#79](https://github.com/grg914/dlss-nr-manager/issues/79): real RTX Vulkan Real-ESRGAN x2/x4 output and complete transitive native dependencies; OpenMP PE direct-import CI is not enough.
- [#95](https://github.com/grg914/dlss-nr-manager/issues/95): physical Windows install/redownload/kill/restart/rollback, user output preservation, process descendants, GPU VRAM/CPU/RAM/network cleanup, FR/EN dialogs.
- [#111](https://github.com/grg914/dlss-nr-manager/issues/111): optional Update only for newly approved immutable version+hash+receipt (no unsupported manual/restricted model updates); offline/cancellation/rollback matrix.
- [#161](https://github.com/grg914/dlss-nr-manager/issues/161): old and new OptiScaler runtime seed SHA mismatch, remaining C4744 ABI investigation, no silent overwritten release.
- [#411](https://github.com/grg914/dlss-nr-manager/issues/411): verify full bytes of 12 Real-ESRGAN model assets, legitimate v3.2.0 LF/CRLF repair, RTX real model load, model quality and cache/repair validation.
- [#408](https://github.com/grg914/dlss-nr-manager/issues/408): .NET 8 end of support 2026-11-10; decide/qualify .NET 10 LTS or explicit limited support plan with offline .NET feed and native ONNX/WPF acceptance.
- [#124](https://github.com/grg914/dlss-nr-manager/issues/124): unique-history branch cleanup only after audit, no automatic deletion.

ACCEPTANCE METHOD: use existing `docs/V4_FUNCTIONAL_ACCEPTANCE_MATRIX_2026-10-10.md` and `docs/V4_BLOCKERS_95_79_111_OPERATOR_ACCEPTANCE_2026-10-10.md` on the actual Windows RTX host. Preserve signed logs, package SHA receipts and observed rollback state. Until that happens, hardware scenarios remain NOT RUN, and V4 publication remains BLOCKED.

## Change and review discipline

After EACH functional code change: record before/after SHA and modified files; run exact-head Windows build/xUnit, CodeQL, and relevant native/seed safety workflow; inspect warning/error logs (not only workflow `success`); compare provenance locks and historical assets; update the relevant open issue and this document. Never infer device-level performance from GitHub-hosted CI. This document describes traceable evidence, not a promise of a clean release.
