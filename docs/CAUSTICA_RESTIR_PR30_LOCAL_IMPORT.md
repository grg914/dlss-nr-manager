# Experimental Caustica ReSTIR CI JAR — local Windows test

**Channel:** Experimental only, not production. **Source:** [Caustica-RTX PR #30](https://github.com/grg914/Caustica-RTX/pull/30), commit `f029e6aebfcab092e00772fcf551ca4b5f037510`, CI run [37944735474](https://github.com/grg914/Caustica-RTX/actions/runs/37944735474) (passed). No hardware Vulkan RTX validation or independent binary-release approval yet.

## Pinned test input

| Item | Value |
| --- | --- |
| CI artifact ID | `11623169522` (`caustica-bundled-jar`) |
| Outer artifact ZIP SHA-256 | `ffef65ef8f6a27b02a78a5a84b629b0b37c9f80b71b8fde77fe0d9dab564c9cf` |
| JAR inside ZIP | `caustica-rtx-0.1.0-rtx.2.jar` |
| Extracted JAR SHA-256 | `b672d8d1ac9caca2f2a182ac4e36e9841f080560ba9a7afe4b222f18a71d1a8b` |
| Minecraft / Fabric | 26.2 / Loader 0.19.3+ |
| CI NVIDIA DLSS SDK tag | 310.7.0 (the CI's SDK checkout; does not attest to any future local build) |

CI archive contains one mod JAR with `fabric.mod.json`, `lights.restir-di` compiled switch and Windows x64 `ngxshim.dll` / `nvngx_dlssd.dll` / `nvngx_dlssg.dll`. These checks establish package identity, not runtime quality or permission to redistribute the SDK independently.

The experimental JAR is **not committed to Git or added to production Manager releases**. Retrieve the immutable CI artifact from the linked run or use the extracted verified JAR provided for local evaluation.

## Manager workflow (draft PR stack #311 → #316 → test-import PR)

1. Close Minecraft. Install the normal Manager-owned Minecraft RTX profile first (stable). Ensure exactly one Caustica JAR exists in the instance's `mods` directory.
2. Open Minecraft RTX, select **Experimental (locally installed build)**, click **Import experimental JAR…** and select the extracted test JAR.
3. The Manager checks the exact pinned SHA-256, Fabric metadata, ReSTIR option and required native paths; it backs up the old Caustica before changing the mods directory. ReSTIR stays **OFF** by default. Use **Apply ReSTIR option** only for an explicit opt-in.
4. Restart Minecraft and record logs/FPS/VRAM, flicker, temporal variance and correctness in identical scenes. Compare with ReSTIR OFF. Do **not** interpret CI success as Vulkan/RTX acceptance.
5. Close Minecraft. Click **Restore previous Caustica**. Manager checks original/experimental hashes, disables ReSTIR when possible and restores the original JAR. Managed Minecraft updates are blocked while the experimental import receipt is active.

Do not place this artifact on the normal public release channel or claim that it is stable. NVIDIA SDK/runtime terms and the absence of a production-grade hardware validation remain separate blockers.
