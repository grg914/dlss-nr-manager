# V4 functional acceptance matrix — 2026-10-10

Status: SOURCE-ONLY AUDIT. No physical Windows RTX or complete UI exercise has been performed. No V4 release approval.
Stable reference: main 01fff7c288a56f1378b5e9cb1020dbf51e5a6178, signed. Source/release version 3.2.0.

## Coverage and evidence
Source: MainWindow.xaml, MainWindow.xaml.cs, manifests/feature-policy.json, docs/FEATURE_POLICY_MATRIX.md, docs/LOCAL_AI_STUDIO.md and 36 test-project entries.
Static inventory: docs/V4_WPF_CONTROL_INVENTORY_2026-10-10.json records 187 visible-source interactive controls, all marked NOT_TESTED. Static check found 102 distinct event handler references and 0 missing methods in MainWindow.xaml.cs; that does not prove correct behavior.
PR #402 and #405 exact signed-head CI: Windows Build, CodeQL successful, 257/257 xUnit, zero failures/skips, deterministic package verified. Not a real Windows RTX functional test.

## Feature matrix
| ID | Module | Minimum functional acceptance scenario | Result |
| --- | --- | --- | --- |
| F01 | Games & DLSS | Auto/manual game selection; SR/FG/Reflex/NR GPU masking, install/verify/restore, invalid path and game launch | NOT RUN |
| F02 | Minecraft RTX | Instance preflight, Fabric/SPBR selection, native/NGX policy, install, real launch, rollback and user-data preservation | NOT RUN |
| F03 | Media Neural | Valid input/output, scale/style, local media output, bad input, cancel, no source overwrite, helper/VRAM cleanup | NOT RUN |
| F04 | VLC VSR-HDR direct | Play local video, VSR/HDR/scale/overlay toggles, stop, interrupted portable VLC recovery | NOT RUN |
| F05 | Restore HD Vidéo | AI restoration options, separate output, corrupt file, cancel and recovery | NOT RUN |
| F06 | AI-origin detection | ONNX model presence, mode selection, image/video analyze/export, invalid content | NOT RUN |
| F07 | AI Studio | Create, Model Manager, Jobs, Runtime, prompt/source/mask/output and four task types if offered; distinguish job queued from actual generation | BLOCKED / PARTIAL |
| F08 | Downloads | Install, opt-in verified Update, Redownload, Remove, Cancel, offline, wrong hash/version, >1GiB approval, rollback | BLOCKED #111/#95 |
| F09 | PC Update Center | Scan and WinGet opt-in, pinned/unknown versions, offline and vendor/driver links without silent installation | NOT RUN |
| F10 | PC Cleanup/Windows repair | Analyze/confirm/clean safe cache, junction/locks, no user-data deletion; explicit elevated DISM/SFC | NOT RUN |
| F11 | Diagnostics | Refresh and export sanitized ZIP, missing logs, invalid destination, redaction | NOT RUN |
| F12 | Advanced OptiScaler | Proxy, FPS overlay, ReShade/process filter persistence and managed installation/restore | NOT RUN |
| F13 | OptiScaler log | Reload/read-only log, missing/corrupt/locked file handling | NOT RUN |
| F14 | Application, Hardware & Profiles | First-run offline, FR/EN all pages/errors, self-update SHA/rollback, GPU capability and Compatible profile persistence | NOT RUN |

Source policy has 13 feature groups; F14 separates the additional Hardware & Profiles UI and application behaviors. Nested tabs include VLC Direct/Restore and AI Studio Create/Model Manager/Jobs/Runtime.
AI Studio documentation explicitly lists ComfyUI and Diffusers actual execution, approved private Python/PyTorch runtime, scheduler, progress/cancel and final generation as incomplete. GPT C V4.5 is a separate development lane; a queued job does not prove generation.

## Cross-module / lifecycle matrix
| ID | Required interaction | Result |
| --- | --- | --- |
| I01 | Startup and local operations with network disconnected; no mandatory updater | NOT RUN |
| I02 | French-English language changes including cached downloads, dialogs and errors | NOT RUN |
| I03 | GPU detection to saved profiles to actual game file staging, with incompatible options masked | NOT RUN |
| I04 | Active download canceled, forced exit, restart, transaction rollback and no orphan workers | BLOCKED #95 |
| I05 | Media/AI helper closing: tracked PID tree, sockets, VRAM, handles, files and output integrity | BLOCKED #95 |
| I06 | Minecraft install/launch/restore preserves user instances and sources | NOT RUN |
| I07 | Update valid/older/wrong-hash package and restore known-good manager/runtime | NOT RUN |
| I08 | Cleanup does not delete active staging, shared resources or unrelated user files | NOT RUN |
| I09 | Repeated normal/forced close/restart with baselines and log capture | BLOCKED #95 |
| I10 | Exact signed release EXE/ZIP + SHA256SUMS + component manifest + SBOM + provenance + licenses | BLOCKED #79/#95/#111 |

## Priority and release disposition
**P0:** #95 Windows RTX physical process, interrupted install, rollback, symlinks/locked files and data-loss testing; #79 Microsoft OpenMP legal REDIST provenance or tested fail-closed omission. CI is not physical acceptance.
**P1:** #111 additional approved immutable receipts for VLC, Real-ESRGAN and AI-origin; safe optional Update only. Full FR/EN GUI, offline behavior and real GPU outputs require observation.
**P2:** dependency notices and vulnerability advisories, package provenance/hash review, maintenance and only verified redundant branch cleanup.
Earlier duplicate B02 PR #348 was closed unmerged after exact equality of its three code/test files with stable main; v4.5/v4.6 GPT A/C drafts and CI mirrors are outside V4 finalization and should not be deleted or merged just for a zero count.

## Evidence protocol and closure
For each control in the JSON inventory record source SHA, tested executable SHA256, OS/GPU/driver, input, expected/actual behavior, log or screenshot, and PASS/FAIL/BLOCKED/NOT_RUN, issue/fix/retest. No item is PASS without a completed test. For normal or forced shutdown compare process ancestry, ports, handles, write activity and VRAM before/during/after. See docs/V4_WINDOWS_RTX_ACCEPTANCE_2026-10-10.md.
Only after the V4 scope is fully inventoried, obligatory physical and CI tests pass, critical defects have fixes, OpenMP/update license and receipts are resolved, all release artifacts have digests/SBOM/provenance, documentation/journals are reconciled, and owner approves may V4 be tagged and published.
