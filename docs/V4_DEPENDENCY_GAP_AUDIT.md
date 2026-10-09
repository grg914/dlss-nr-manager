# v4.0 dependency gap audit — 2026-10-09

Scope: **Manager v4.0 completion only**. Current production source/release remains v3.2.0. This is a verified snapshot, not authorization to publish or evidence of RTX Windows acceptance. AI Studio v4.5 / Python-CUDA execution and experimental Caustica ReSTIR are outside this v4 release gate.

## Result: no additional mandatory repository or fork identified

- **Public dependencies:** 27 immutable source entries in `third_party/DEPENDENCIES.lock.json` match 27 public IDs in `third_party/UPSTREAMS.json` one-for-one; the separately recorded restricted NVIDIA DLSS SDK is a **local-only** locked input, not vendored distributable source. Source refresh is distinct from authorizing a new runtime.
- **Windows app:** .NET 8 WPF is published self-contained win-x64/single-file. An end user does **not** need a separately installed .NET SDK to launch the Manager. Build CI uses a preinstalled .NET 8 SDK and manager-owned SHA-256-locked offline NuGet packages.
- **Published components:** production `v3.2.0` has the Windows Manager EXE/ZIP, Caustica RTX Minecraft 26.2 JAR, Minecraft runtime, SPBRScandi, Temurin 25 JRE, FFmpeg, video2dlssnr, OptiScaler, Streamline, ReShade, Real-ESRGAN/NCNN, and AI-origin models. Manager-owned `runtime-seed-v1` also holds the offline NuGet seed and VLC 3.0.24 Windows runtime. The presence of an asset does **not** prove an installed component runs on every machine.
- **Minecraft/Fabric:** `third_party/minecraft/RUNTIME.lock.json` pins Minecraft 26.2 and Fabric Loader **0.19.5**, Fabric API and optional performance mods. C2ME is **excluded** from the approved runtime lock: do not add it silently to the v4 performance pack.
- **SDK versions:** Manager local-only NVIDIA DLSS source input is pinned to `v310.9.1`; the *experimental* Caustica CI used `v310.7.0`. SDK is a **build input**, not a selectable runtime upgrade for existing JARs; no new NVIDIA SDK fork or raw redistribution is justified by the mismatch.

## Targeted discrepancy fixed here

The Caustica lockfile previously described its source as GPL-3.0; its exact vendored `Caustica-RTX/LICENSE.md` and locked upstream revision state **LGPL-3.0-or-later** for **project-owned source**. The lock metadata is corrected to that precise scope. Bundled NVIDIA/native third-party files continue under their respective licenses, and this edit does not approve distributing SDK materials.

## Remaining v4 gates — not reasons to vendor random new dependencies

| Category | Actual gap | Safe action |
| --- | --- | --- |
| Microsoft OpenMP / Real-ESRGAN, issue #79 | Evidence of permitted redistributable `vcomp140.dll` source, signed x64 REDIST origin, version/hash, operator license authorization and physical Windows test | Keep the existing fail-closed REDIST gate. Either approve the appropriate licensed Microsoft package/DLL with provenance or keep OpenMP packaging disabled; **never** copy untracked System32 files |
| Win11 / RTX reliability, issue #95 | On-device crash/kill/restart, file-lock/junction, interrupted redownload and rollback acceptance | Complete code/CI gates first, then operator Windows acceptance before releasing v4.0; synthetic tests are insufficient |
| Offline startup, GPT B branch #324 | Last-mile offline release probe/runtime behavior | Owner GPT B handles exact-head Build/CodeQL and protected merge. No additional browser/network adapter package is needed |
| Historical branches, issue #124 | Semantic branch cleanup to exclude unmerged or superseded code | Audit branches and approved consumers; no blanket forks or sync |
| OptiScaler native reproducibility, issue #161 | Future native rebuild and canonical archive comparison | Already-approved pinned runtime exists; no new native toolchain or binary rebuild needed just to ship unchanged v4 |
| Experimental ReSTIR PRs #311 → #316 → #319 | Draft integration, separate SDK provenance and RTX validation | **Out of v4 stable release path** until reviewed and tested; do not publish its CI JAR on the stable channel |
| AI Studio v4.5, issue #285 | Python, PyTorch CUDA, ComfyUI/Diffusers runtime approval and model licensing | **Separate GPT C project lane.** Do not add its large runtime to the v4 app or publish experimental model weights to complete v4 |

## External machine prerequisites (not to clone into Git)

- Windows x64 and a supported NVIDIA RTX GPU/driver for relevant RTX features; compatibility must be checked by the existing preflight.
- Supported Vulkan runtime provided with the driver as applicable; build-time Vulkan SDK / Slang / C++ MSVC are developer toolchain inputs, **not** runtime downloads for every Manager user.
- Microsoft-supported Visual C++ Redistributable *only if required by a particular native component*; use a verified official distribution and license path. Official guidance: [Redistribute Visual C++ Files](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) and [Determine which DLLs to redistribute](https://learn.microsoft.com/en-us/cpp/windows/determining-which-dlls-to-redistribute).
- Minecraft account/authentication and the actual game installation remain external prerequisites for Minecraft; the Manager runtime bundle does not grant game ownership.

## Before declaring v4.0 complete

1. Preserve `main` and immutable approved release assets while GPT B finishes v4 reliability/rollback and offline source checks.
2. Record operator-approved Microsoft REDIST provenance or keep affected OpenMP redistribution disabled.
3. Complete release checklist: README/version, exact reviewed signed source SHA, Windows Build/CodeQL, xUnit, offline deterministic package/SBOM, hashes/manifest, FR/EN, source/third-party notices, rollback and Windows device evidence.
4. Promote **only** an approved v4 source to a new immutable `v4.0.0` release after those gates. Do not change version/tag early, bypass CI rulesets, or merge GPT A/C experimental branches to accelerate publication.

**Conclusion:** For v4 there is **no demonstrated missing mandatory new dependency**; the remaining blockers are compliance, source/runtimes already locked, runtime behavior and acceptance. No new fork, clone, auto-download or third-party SDK distribution has been authorized by this audit.
