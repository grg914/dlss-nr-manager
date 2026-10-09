# GPT A — NVIDIA NGX / Caustica RTX source-level verification (2026-10-09)

**Scope:** read-only verification of current Manager `main=dddd9ac76a9108a7607331966f9148245a23e686`, Caustica RTX `main=702370c060febf8f9a82029fa443b23a7d40cf83`, and open experimental PRs. **Not** a GPU functional certification, third-party license permission, or a production release.

## Confirmed architecture

```text
Minecraft 26.2 + Fabric 0.19.5
  -> Vulkan backend -> Caustica path tracing / RT
  -> one shared NgxRuntime/NgxLibrary (NGX C shim)
     -> DLSS Ray Reconstruction (DLSSD), upscaling render -> display
     -> optional DLSS NR when optional native ABI and compatible runtime exist
     -> optional DLSS FG/MFG under capability and input-format checks
  -> Caustica post/display and UI
```

The Manager's **Games & DLSS** component service is separate: `Services/MinecraftDlssPackageService.cs` stages selected vendor-locked runtime DLLs into a Minecraft-owned runtime directory and validates member hashes. `Services/MinecraftOneClickService.cs` prefers Vulkan and describes Caustica's direct native NGX path; it does **not** insert OptiScaler's DXGI/D3D12 proxy into Minecraft. Generic OptiScaler and double FG/upscaling/sharpening should not be added to this pipeline. SPBRScandi is a resource pack, not an NGX runtime.

## Integration evidence / remaining checks

| Integration | Code evidence | Remaining gate |
| --- | --- | --- |
| Shared NGX lifecycle | Caustica `NgxRuntime.acquire(VulkanDevice)` initializes the shared shim once; shutdown after feature release, error latch on failed init | Physical Vulkan startup / device reset and clean lifetime; driver/GPU behavior |
| DLSS Ray Reconstruction | `RtDlssRr` uses HDR, low-resolution motion vectors, inverted depth, auto exposure and multiple path-tracing guides; exposes NGX DLSSD create/evaluate | Physically confirm geometry, motion/guide alignment, black-frame/moon issues, output quality, latency |
| DLSS Neural Rendering | `RtDlssNr` probes optional `NgxLibrary.hasDlssNr()` ABI + `dlssNrAvailable()`, requires RR and correct hardware; failure disables NR without killing renderer | Real authorized NR runtime, feature eligibility, correct SDR/display buffer order, toggles and quality; unsupported DLL ABI must safely fall back |
| DLSS Frame Generation / MFG | `RtDlssFg` shares NgxRuntime, probes `hasDlssg` and cap, checks dimensions/backbuffer format before feature reuse | Check swapchain/HUD/Reflex coupling, generated vs rendered FPS and frame pacing on hardware |
| NVIDIA Reflex | Caustica config exposes capability-gated Reflex; Manager package enumerates `sl.reflex.dll` | Inspect/measure live native hook, latency/per-frame markers; config/menu presence alone is not proof |
| Streamline pair | `docs/NVIDIA_RUNTIME_ARCHITECTURE.md` pins `sl.dlss_nr.dll` SHA-256 `9f6672e5e0170dc118a3188d21bda187e1fc1aa3502895b21ab846d23165c11d` and `nvngx_dlssnr.dll` SHA-256 `e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e`. Manager's Minecraft package service also pins other member hashes | Verify the actual selected runtime ZIP's member hashes/signatures at import; do not substitute a guessed upstream download |
| SDK / compatibility | Manager local-only DLSS SDK lock `v310.9.1`; the experimental Caustica PR #30 CI JAR was built against `v310.7.0` | Do not equate build-time SDK version with available runtime ABI or distribute raw restricted SDK; regression test by signed, pinned build ID |
| Resource/pipeline conflict | `docs/OPTISCALER_MINECRAFT_AUDIT.md` and `MinecraftRenderPipelinePolicy` reject generic proxy in native Minecraft path | On-device compatibility trials after stable v4 completion |

## ReSTIR experimental stack (Caustica)

- [PR #28](https://github.com/grg914/Caustica-RTX/pull/28): integration smoke/snapshot, **five commits ahead of Caustica main**, combines shader, composite and numerical test changes. This is **not** five independently reviewed features.
- [PR #29](https://github.com/grg914/Caustica-RTX/pull/29): positive-selection support correction over historical statistical-audit ancestry. The current PR branch is divergent relative to another historical branch and the changes are represented in the #28 combined smoke snapshot; do **not** blindly merge this PR after #28 or double-apply the change. Reconcile its unique commits/files first.
- [PR #30](https://github.com/grg914/Caustica-RTX/pull/30): two commits above #28, changes a Caustica setting/comment to emphasize opt-in experimental ReSTIR, not a new independently certified renderer.

**Statistical finding already in `docs/restir-temporal-positive-support.md` on experimental #28:** a positive selection floor restores the controlled estimator mean but increases toy-model variance; for one example, variance is approximately 980.74 at floor 0.001 versus 12.92 at 0.1 and 3.94 fresh-only. Three-frame oracle still demonstrates remaining variance; history count capped to 1 experimentally. These are **finite-state toy results**, never observed GPU FPS, visual quality or production unbiasedness.

**Open acceptance:** target/support under emitter changes, temporal reprojection, history rejection, spatial reuse (still disabled), first-receiver numerical variance policy, shader output/VRAM/1% low, independent source review, licensing, physical Minecraft 26.2 RTX trials. Keep ReSTIR default OFF and all experimental JARs outside the v4 stable release.

## Security and rollback — local JAR import

Manager experimental [PR #319](https://github.com/grg914/dlss-nr-manager/pull/319) requires an exact pinned JAR SHA-256 and Fabric identity/version, a single existing Caustica mod and manager-owned backup+receipt; new tests also disallow automatic restore when the receipt is corrupted or source metadata is forged. **No user data is silently recovered or deleted when the receipt is untrustworthy**. Manual recovery is required after interrupted swaps; this remains an experimental Draft workflow. Exact final-head Windows Build/xUnit/CodeQL and physical device validation are separate gates.

## Conclusion

The **source wiring and fail-closed feature probes exist**; end-to-end NR/FG/Reflex behavior cannot be verified without actual NVIDIA hardware, driver and authorized runtime. Do not install new SDK files, invent a new dependency, publish experimental JARs or merge the experimental Caustica stack into stable v4.0 on this audit.
