# GPT A — six-phase technical completion status (2026-10-09)

**Verified scope:** Manager source `main=dddd9ac76a9108a7607331966f9148245a23e686` and experimental Caustica `main=702370c060febf8f9a82029fa443b23a7d40cf83`, with focused checks of the current GitHub PR stack. This status is about implementation/evidence, **not a percentage of code completion or a GPU certification**. Cross-reference `docs/V4_SIX_PHASES_AUDIT_STATUS.md`, `docs/NVIDIA_RUNTIME_ARCHITECTURE.md` and `docs/GPT_A_NVIDIA_NGX_RESTIR_AUDIT_2026-10-09.md`.

| Phase | Implemented / verified by source or CI | Remains before verified completion | Status |
| --- | --- | --- | --- |
| **1. Dependency provenance & versions** | 27 public pinned dependencies mapped to upstream policy; NVIDIA DLSS SDK local-only; Fabric 26.2/Loader 0.19.5, Streamline v2.14.1, Caustica built/managed with SHA and upstream policy; audit corrects Caustica project-owned source license to LGPL-3.0-or-later | Preserve separate NVIDIA licenses; verify actual redistributable binary provenance; no missing mandatory new fork found | **Source implemented; legal/hardware acceptance pending** |
| **2. Native packaging / runtime ownership** | Self-contained .NET 8 WPF win-x64 publish; manager-owned versioned runtimes/assets and hashed Streamline/NR pair; immutable release packaging checks, OptiScaler separate from Minecraft | Real packaged DLL/NGX/VRAM behavior, full Windows installation and recovery; future native OptiScaler reproducibility #161 not needed for unchanged approved asset | **Build integration implemented; physical verification pending** |
| **3. Minecraft RTX / Caustica / NVIDIA integration** | Vulkan path, direct Caustica NGX shim, DLSS RR, optional NR, FG/MFG and Reflex source hooks; SPBRScandi resources; RTX performance profiles and Fabric preflight | Test real RTX 5060 Ti Minecraft 26.2, RR/NR output and fallback, full motion/normal guides, RTX on/off, UI state persistence and latency/performance | **Code integrated; runtime acceptance pending** |
| **4. Experimental ReSTIR / renderer performance** | Draft Caustica #28 smoke stack, #29 positive-support math and #30 experimental toggle; temporal multi-frame oracle and conservative history cap 1; JAR import Manager #311→#316→#319 with backup/receipt tests | Independent review; unresolved estimator variance under changing scene/proposal support; disabled spatial reuse; actual GPU flicker/ghosting/FPS/VRAM tests, stable promotion decision | **Experimental / NOT production-ready** |
| **5. Manager UX / offline / Download Manager** | 14 WPF pages including Games, Minecraft, Downloads and hardware profiles. GPT B [#324](https://github.com/grg914/dlss-nr-manager/pull/324) **merged** to disable automatic remote update lookups when offline, Build+CodeQL and 194 tests green. Download services and FR/EN resource mappings present | Real Windows offline start, interruption/cancel/resume, file locks/symlinks, rollback and full FR/EN operator acceptance #95; don't rebuild GPT B's change | **Code largely integrated; end-to-end acceptance pending** |
| **6. Release governance / certification** | Strict manager-owned GitHub releases/runtime seed, protected main CI, signed merge verification practice, deterministic packaging, SHA-256 and SBOM/provenance implementation; latest public app remains **v3.2.0** | Resolve or explicitly disable unlicensed OpenMP redistribution #79; exact final-head Build/CodeQL/signature/reviews, Windows RC acceptance, actual release version/docs, immutable new v4.0.0 assets; no production release yet | **Pipeline present; release gates OPEN** |

## Decisions that prevent duplicated development

- **GPT B owns** v4.0 stability/release/issue #95 and #79 operator acceptance. GPT A supports by source provenance and the non-destructive Windows acceptance specification in `docs/V4_RC_ACCEPTANCE_HANDOFF.md`. Final v4.0 release does **not** require merging ReSTIR draft work or AI Studio v4.5.
- **GPT A owns** renderer/NGX, experimental ReSTIR and six-phase technical analysis. Keep Manager #311/#316/#319 and Caustica #28/#29/#30 isolated drafts until reviewed, CI green, and actual GPU acceptance.
- **GPT C owns** separate v4.5 ComfyUI/Python/PyTorch/model workflows; these must not introduce mandatory dependencies into v4.0.
- **Do not add** OptiScaler DXGI proxy in Vulkan Caustica, a second DLSS/FG path, or C2ME to the locked Minecraft 26.2 performance pack without separate runtime acceptance.
- **No invented benchmarks**: toy ReSTIR variance and successful xUnit tests establish limited mathematical/structural properties, not actual VRAM, frame pacing or FPS.
- **No branch cleanup without evidence**: check semantic changes in diverged or stacked history, signed CI, current `main`, reviews and the three journals. Issue #124 remains open for unrelated historical work.

## Recommended next moves after source audit

1. **Consolidate** historical GPT A docs #320 + #325 against updated GPT B main in a single focused review branch, preserve all unique checkpoints and correct outdated statuses; do not merge either stale branch into current main blindly.
2. **Complete safe receipt handling** in Manager experimental #319 and exact-head Windows Build/CodeQL. Keep it Draft.
3. **Review** combined Caustica #28/#29/#30 source and its numerical tests; no production promotion without hardware and licensing.
4. **Allow GPT B** to finish v4.0 RC and its Windows operator checklist, then resume ReSTIR/NGX physical tests and remaining renderer features. No background testing or unapproved SDK downloads.
