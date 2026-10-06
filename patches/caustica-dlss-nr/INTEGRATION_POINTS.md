# Integration points against Caustica main

Baseline: `e3e3ec10d0a1dd383d51f98a0cf974a469fc67f1`.

This file lists the exact Caustica source locations that must be modified when applying the reference implementation.

## 1. native/ngx_shim/CMakeLists.txt

Detect `nvsdk_ngx_helpers_dlssnr_vk.h` in the NVIDIA SDK include path. Define
`CAUSTICA_HAS_DLSSNR=1` only when present. A standard RR/FG build must not fail when it is absent.

## 2. native/ngx_shim/ngx_shim.cpp

Add optional exports described by `reference/ngx_shim_dlssnr.cpp`.

Use the shim's existing shared NGX capability parameter block and the same Vulkan device already used by RR/FG.
Do not initialize or shut down a second NGX runtime.

For production, reuse Caustica's existing feature-wrapper allocation so `ngxshim_release` can safely release SR,
RR, FG and NR handles through one mechanism.

## 3. NgxLibrary.java

Add the optional FFM handles from `reference/NgxLibrary.dlssnr.java`.

They must use `optionalHandle(...)`, not `handle(...)`, so current release JARs remain compatible with an older
shim and simply expose NR as unavailable.

## 4. CausticaConfig.java

Add:

```java
public static final class DlssNr {
    public static final BooleanSetting ENABLED = ... false;
    public static final FloatSetting INTENSITY = ... 1.0f;
    public static final FloatSetting LOCAL_TONE = ... 1.0f;
    public static final FloatSetting LOCAL_STRUCTURE = ... 1.0f;
    public static final FloatSetting GLOBAL_TONE = ... 1.0f;
    public static final FloatSetting SKIN_STRUCTURE = ... 1.0f;
    public static final IntSetting STYLE = ... 0;
    public static final BooleanSetting AUTO_MASK = ... true;
}
```

Clamp strengths to [0,1]. Keep the default disabled.

## 5. RtVideoOptions.java

Add a `DLSS Neural Rendering` control group after the existing DLSS RR controls and before FG/Reflex.

The toggle must be disabled when:

- the optional shim ABI is absent;
- the NGX capability probe reports unavailable;
- RR is disabled (path-tracing path needs a denoised input);
- native HDR is enabled in the first implementation.

Expose Intensity, Local Tone, Local Structure, Global Tone, Skin Structure, Style and Auto Mask.

## 6. New RtDlssNr.java

Start from `reference/RtDlssNr.java`.

The production class must own only the NR feature + output scratch image. It must share `NgxRuntime.INSTANCE`
with RR/FG.

## 7. RtComposite / display path

Hook NR after `RtDisplayPipeline.dispatch(...)` has produced the SDR display-resolution image and before the
frame is handed to UI composition / `RtFramePresenter.prepareExtraFrames(...)`.

Required order:

```text
RR output -> RtDisplayPipeline SDR tonemap -> RtDlssNr.evaluate -> UI -> FG/MFG -> present
```

Do not evaluate on the scene-linear ACEScg input. Do not evaluate on PQ HDR in phase 1.

After successful evaluation, copy/blit `RtDlssNr.output()` back to the SDR display image that the existing
present/FG path consumes.

## 8. Guide buffers

Phase 1:
- depth = existing RR depth guide;
- motion = existing RR motion guide;
- control mask = null;
- `useAutoMask = true`.

Phase 2:
add an RGBA control mask generated from LabPBR/material semantics:

- R intensity
- G local tone
- B local structure
- A global tone

## 9. Reset conditions

Call `RtDlssNr.INSTANCE.resetHistory()` on:
- camera teleport/cut;
- dimension change;
- render/display resolution change;
- NR style change;
- RR feature recreation;
- F3+A/full RT reset.

## 10. shutdown

In `CausticaClient.shutdownRt()`:

```text
destroy NR feature
destroy RR/FG features
destroy presenter/reflex
NgxRuntime.INSTANCE.shutdown()
destroy Vulkan RT context
```

The shared NGX runtime must be shut down once, after all NGX feature handles are released.

## 11. build.gradle native bundle

When the authorized SDK/runtime supplies the Windows x64 NR DLL, include it alongside RR/FG:

```groovy
vendorIncludes: [
    "nvngx_dlssd.dll",
    "nvngx_dlssg.dll",
    // conditionally, only for a DLSS-NR SDK build:
    "nvngx_dlssnr.dll"
]
```

Do not make `nvngx_dlssnr.dll` mandatory for normal Caustica releases until the public SDK includes it.

## 12. runtime log acceptance

A working RTX 50 test should emit lines equivalent to:

```text
DLSS Neural Rendering available: true
DLSS Neural Rendering feature created: 2560x1440
DLSS Neural Rendering enabled: style=..., intensity=...
```

Any create/evaluate error must disable only NR and leave RR/FG/rendering alive.
