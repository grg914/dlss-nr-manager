# Caustica RTX — DLSS Neural Rendering integration

Reference implementation package for adding **DLSS Neural Rendering / 3D-Guided Neural Rendering** to
`AriesAlex/Caustica-RTX`.

Upstream baseline:

```text
repository: AriesAlex/Caustica-RTX
branch: main
commit: e3e3ec10d0a1dd383d51f98a0cf974a469fc67f1
Minecraft: 26.2
Caustica release currently installed by DLSS NR Manager: 0.1.0-rtx.2
```

## Important SDK requirement

This is a **real renderer integration**, not DLL staging.

The public NVIDIA Streamline 2.14.x release announces the `sl.dlss_nr` feature, but the public
source/package does not currently expose the complete feature-specific header/runtime required to
compile a custom Vulkan renderer integration. NVIDIA's own public `nvpro-samples/vk_gltf_renderer`
guards the NR code behind `USE_DLSSNR` and requires a compatible DLSS-NR/NGX SDK containing:

```text
nvsdk_ngx_helpers_dlssnr_vk.h
NVSDK_NGX_Feature_DLSSNR
NVSDK_NGX_DLSSNR_Create_Params
NVSDK_NGX_VK_DLSSNR_Eval_Params
NGX_VULKAN_CREATE_DLSSNR_EXT1
NGX_VULKAN_EVALUATE_DLSSNR_EXT
```

Do not commit or redistribute NVIDIA proprietary SDK binaries here. A developer building the
DLSS-NR variant must provide an NVIDIA-authorized compatible SDK/runtime locally.

## Required render order

For Caustica's path-traced renderer the correct order is:

```text
Path tracing
  -> DLSS Ray Reconstruction (denoise + upscale)
  -> SDR display transform / tonemap
  -> DLSS Neural Rendering (display resolution, LDR 0..1)
  -> Minecraft UI composition
  -> DLSS Frame Generation / MFG
  -> present
```

NR must not be fed the scene-linear pre-tonemap HDR buffer. NVIDIA's reference integration evaluates
NR after tonemapping. The first Caustica implementation should therefore disable NR while native HDR
output is active, until an explicitly validated display-referred HDR path exists.

## Inputs Caustica already has

Caustica already produces the inputs needed for a first real implementation:

- final denoised/upscaled RR color
- path-tracing depth
- motion vectors
- display/render dimensions
- temporal reset events
- Vulkan command buffer/device/NGX context

The optional 3D-guided control mask can be added after the baseline integration. A legitimate first
version may use `InUseAutoMask = 1`; that is still a real DLSS-NR evaluation, not a fake toggle.

## Native ABI to add

Keep DLSS-NR optional so old `ngxshim.dll` builds continue to load.

Suggested flat C exports:

```c
int ngxshim_dlssnr_available(void);

void* ngxshim_create_dlssnr(
    VkCommandBuffer cmd,
    unsigned int displayWidth,
    unsigned int displayHeight);

int ngxshim_evaluate_dlssnr(
    VkCommandBuffer cmd,
    void* feature,
    VkImageView colorView, VkImage colorImage, int colorFormat,
    VkImageView depthView, VkImage depthImage, int depthFormat,
    VkImageView motionView, VkImage motionImage, int motionFormat,
    VkImageView controlMaskView, VkImage controlMaskImage, int controlMaskFormat,
    VkImageView outputView, VkImage outputImage, int outputFormat,
    unsigned int displayWidth, unsigned int displayHeight,
    unsigned int auxWidth, unsigned int auxHeight,
    float mvScaleX, float mvScaleY,
    int depthInverted, int reset,
    float intensity,
    float localToneStrength,
    float localStructureStrength,
    float globalToneStrength,
    float skinStructureStrength,
    unsigned int style,
    int useAutoMask);

void ngxshim_release(void* feature);
```

The shim implementation must map those resources to `NVSDK_NGX_Resource_VK`, populate
`NVSDK_NGX_VK_DLSSNR_Eval_Params`, and call
`NGX_VULKAN_EVALUATE_DLSSNR_EXT`.

## Java integration

Add an `RtDlssNr` singleton analogous to `RtDlssRr` / `RtDlssFg`.

Responsibilities:

1. probe the optional shim ABI;
2. report `Unavailable` when the loaded shim or authorized SDK lacks DLSS-NR;
3. allocate a display-resolution output scratch image;
4. create/recreate the feature on resolution change;
5. consume the already-produced depth + motion vectors;
6. evaluate after SDR tonemapping;
7. copy/blit the enhanced result back into the display image before UI/FG;
8. reset NR history on camera/world/resolution/style discontinuities;
9. release the NR feature before `NgxRuntime.shutdown()`.

## Suggested settings

```toml
[rt.dlssNr]
enabled = false
intensity = 1.0
localToneStrength = 1.0
localStructureStrength = 1.0
globalToneStrength = 1.0
skinStructureStrength = 1.0
style = 0
useAutoMask = true
```

The Minecraft options screen should show **DLSS Neural Rendering** only as available when the runtime
probe succeeds. Do not display an enabled control merely because an RTX 50 GPU is present.

## Control mask — phase 2

For a later material-aware implementation, add a display/render guide buffer whose RGBA channels
follow NVIDIA's NR control-mask semantics:

```text
R = per-pixel intensity
G = local tone strength
B = local structure strength
A = global tone strength
```

Caustica can derive this from its LabPBR/material classification. Until this is implemented, use
`InUseAutoMask = 1` and pass no control-mask resource.

## Build integration

In `native/ngx_shim/CMakeLists.txt`, detect the NR helper header and define a compile flag:

```cmake
include(CheckIncludeFileCXX)
check_include_file_cxx("nvsdk_ngx_helpers_dlssnr_vk.h" CAUSTICA_HAS_DLSSNR_SDK)

if(CAUSTICA_HAS_DLSSNR_SDK)
    target_compile_definitions(ngx_shim PRIVATE CAUSTICA_HAS_DLSSNR=1)
endif()
```

When `CAUSTICA_HAS_DLSSNR` is not defined, keep the exports present but return unavailable/null/error.
That keeps a single Java ABI and makes the feature degrade cleanly.

The Gradle native bundle should include `nvngx_dlssnr.dll` **only when supplied by the authorized
SDK/runtime**. Missing DLSS-NR must not fail a normal RR/FG build.

## Acceptance criteria

A build is considered a real DLSS-NR integration only when all of these are observable at runtime:

- `DLSS Neural Rendering available: true` after the NGX capability query;
- feature creation succeeds at the display resolution;
- an actual `NGX_VULKAN_EVALUATE_DLSSNR_EXT` call succeeds every rendered frame while enabled;
- toggling Intensity between 0 and 1 measurably changes the output;
- RR remains functional;
- FG/MFG receives the NR-enhanced real frame;
- no NR call is made in unsupported/HDR mode;
- feature shutdown is clean and leaves the shared NGX runtime alive for RR/FG until final teardown.

## References

The implementation contract above follows NVIDIA's public `nvpro-samples/vk_gltf_renderer`
DLSS-NR path and the equivalent DLSS-NR path visible in NVIDIA RTX Remix source. It intentionally
does not invent private SDK symbols or vendor DLLs.
