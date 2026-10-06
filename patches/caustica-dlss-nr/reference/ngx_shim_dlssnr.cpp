// Reference implementation for native/ngx_shim/ngx_shim.cpp.
// Compile this block only with an NVIDIA-authorized DLSS-NR/NGX SDK that
// provides nvsdk_ngx_helpers_dlssnr_vk.h.
//
// The public/normal Caustica build should keep the same exported ABI and
// return unavailable when CAUSTICA_HAS_DLSSNR is not defined.

#include <cstring>

#if defined(CAUSTICA_HAS_DLSSNR)
#include <nvsdk_ngx_helpers_dlssnr_vk.h>
#endif

// These globals are already owned by Caustica's ngx_shim.cpp:
// extern NVSDK_NGX_Parameter* g_capabilityParams;
// extern int g_lastResult;

struct DlssNrFeature {
#if defined(CAUSTICA_HAS_DLSSNR)
    NVSDK_NGX_Handle* handle;
#else
    void* handle;
#endif
};

extern "C" {

__declspec(dllexport) int ngxshim_dlssnr_available()
{
#if defined(CAUSTICA_HAS_DLSSNR)
    if (!g_capabilityParams)
        return 0;

    // Prefer the same capability-query path already used by the shared NGX
    // context. If the SDK exposes a dedicated Available parameter, use it.
    int available = 0;

#if defined(NVSDK_NGX_Parameter_DLSSNR_Available)
    if (NVSDK_NGX_SUCCEED(
            NVSDK_NGX_Parameter_GetI(
                g_capabilityParams,
                NVSDK_NGX_Parameter_DLSSNR_Available,
                &available))) {
        return available != 0;
    }
#endif

    // A feature-create failure remains the final runtime capability test.
    // Returning 1 here is safe only for an SDK build where the DLSSNR feature
    // symbols are compiled in. The Java side still gates on create/evaluate.
    return 1;
#else
    return 0;
#endif
}

__declspec(dllexport) void* ngxshim_create_dlssnr(
    VkCommandBuffer cmd,
    unsigned int displayWidth,
    unsigned int displayHeight)
{
#if defined(CAUSTICA_HAS_DLSSNR)
    if (!g_capabilityParams || !cmd || displayWidth == 0 || displayHeight == 0) {
        return nullptr;
    }

    NVSDK_NGX_DLSSNR_Create_Params createParams{};
    createParams.Width = displayWidth;
    createParams.Height = displayHeight;

    NVSDK_NGX_Handle* handle = nullptr;

    NVSDK_NGX_Result result =
        NGX_VULKAN_CREATE_DLSSNR_EXT1(
            /* VkDevice is supplied by the existing Caustica shim context.
             * Replace g_device below with the shim's actual device global. */
            g_device,
            cmd,
            1,
            1,
            &handle,
            g_capabilityParams,
            &createParams);

    g_lastResult = static_cast<int>(result);

    if (NVSDK_NGX_FAILED(result) || !handle)
        return nullptr;

    auto* feature = new DlssNrFeature{};
    feature->handle = handle;
    return feature;
#else
    (void)cmd;
    (void)displayWidth;
    (void)displayHeight;
    return nullptr;
#endif
}

__declspec(dllexport) int ngxshim_evaluate_dlssnr(
    VkCommandBuffer cmd,
    void* opaqueFeature,

    VkImageView colorView,
    VkImage colorImage,
    int colorFormat,

    VkImageView depthView,
    VkImage depthImage,
    int depthFormat,

    VkImageView motionView,
    VkImage motionImage,
    int motionFormat,

    VkImageView controlMaskView,
    VkImage controlMaskImage,
    int controlMaskFormat,

    VkImageView outputView,
    VkImage outputImage,
    int outputFormat,

    unsigned int displayWidth,
    unsigned int displayHeight,
    unsigned int auxWidth,
    unsigned int auxHeight,

    float mvScaleX,
    float mvScaleY,

    int depthInverted,
    int reset,

    float intensity,
    float localToneStrength,
    float localStructureStrength,
    float globalToneStrength,
    float skinStructureStrength,
    unsigned int style,
    int useAutoMask)
{
#if defined(CAUSTICA_HAS_DLSSNR)
    auto* feature = static_cast<DlssNrFeature*>(opaqueFeature);
    if (!feature || !feature->handle || !g_capabilityParams)
        return static_cast<int>(NVSDK_NGX_Result_FAIL_NotInitialized);

    const VkImageSubresourceRange colorRange{
        VK_IMAGE_ASPECT_COLOR_BIT,
        0, 1, 0, 1
    };

    auto color =
        NVSDK_NGX_Create_ImageView_Resource_VK(
            colorView,
            colorImage,
            colorRange,
            static_cast<VkFormat>(colorFormat),
            displayWidth,
            displayHeight,
            false);

    auto output =
        NVSDK_NGX_Create_ImageView_Resource_VK(
            outputView,
            outputImage,
            colorRange,
            static_cast<VkFormat>(outputFormat),
            displayWidth,
            displayHeight,
            true);

    NVSDK_NGX_Resource_VK depth{};
    NVSDK_NGX_Resource_VK motion{};
    NVSDK_NGX_Resource_VK mask{};

    NVSDK_NGX_Resource_VK* pDepth = nullptr;
    NVSDK_NGX_Resource_VK* pMotion = nullptr;
    NVSDK_NGX_Resource_VK* pMask = nullptr;

    if (depthView && depthImage) {
        depth = NVSDK_NGX_Create_ImageView_Resource_VK(
            depthView,
            depthImage,
            colorRange,
            static_cast<VkFormat>(depthFormat),
            auxWidth,
            auxHeight,
            false);
        pDepth = &depth;
    }

    if (motionView && motionImage) {
        motion = NVSDK_NGX_Create_ImageView_Resource_VK(
            motionView,
            motionImage,
            colorRange,
            static_cast<VkFormat>(motionFormat),
            auxWidth,
            auxHeight,
            false);
        pMotion = &motion;
    }

    if (controlMaskView && controlMaskImage) {
        mask = NVSDK_NGX_Create_ImageView_Resource_VK(
            controlMaskView,
            controlMaskImage,
            colorRange,
            static_cast<VkFormat>(controlMaskFormat),
            auxWidth,
            auxHeight,
            false);
        pMask = &mask;
    }

    NVSDK_NGX_VK_DLSSNR_Eval_Params params{};

    params.pInColor = &color;
    params.pInOutput = &output;
    params.pInDepth = pDepth;
    params.pInMVec = pMotion;
    params.pInControlMask = pMask;

    params.InEnabled = 1;
    params.InReset = reset ? 1 : 0;

    params.InIntensity = intensity;
    params.InLocalToneStrength = localToneStrength;
    params.InLocalStructureStrength = localStructureStrength;
    params.InGlobalToneStrength = globalToneStrength;
    params.InSkinStructureStrength = skinStructureStrength;
    params.InStyle = style;
    params.InUseAutoMask = pMask ? 0 : (useAutoMask ? 1 : 0);

    params.InColorSubrectSize = {
        displayWidth,
        displayHeight
    };

    params.InOutputSubrectSize = {
        displayWidth,
        displayHeight
    };

    params.InDepthSubrectSize = {
        auxWidth,
        auxHeight
    };

    params.InMVecSubrectSize = {
        auxWidth,
        auxHeight
    };

    params.InControlMaskSubrectSize = {
        auxWidth,
        auxHeight
    };

    params.InMVecScaleX = mvScaleX;
    params.InMVecScaleY = mvScaleY;
    params.InDepthInverted = depthInverted ? 1 : 0;

    NVSDK_NGX_Result result =
        NGX_VULKAN_EVALUATE_DLSSNR_EXT(
            cmd,
            feature->handle,
            g_capabilityParams,
            &params);

    g_lastResult = static_cast<int>(result);
    return static_cast<int>(result);
#else
    (void)cmd;
    (void)opaqueFeature;
    (void)colorView;
    (void)colorImage;
    (void)colorFormat;
    (void)depthView;
    (void)depthImage;
    (void)depthFormat;
    (void)motionView;
    (void)motionImage;
    (void)motionFormat;
    (void)controlMaskView;
    (void)controlMaskImage;
    (void)controlMaskFormat;
    (void)outputView;
    (void)outputImage;
    (void)outputFormat;
    (void)displayWidth;
    (void)displayHeight;
    (void)auxWidth;
    (void)auxHeight;
    (void)mvScaleX;
    (void)mvScaleY;
    (void)depthInverted;
    (void)reset;
    (void)intensity;
    (void)localToneStrength;
    (void)localStructureStrength;
    (void)globalToneStrength;
    (void)skinStructureStrength;
    (void)style;
    (void)useAutoMask;

    return -1;
#endif
}

} // extern "C"

// Integrate feature destruction into the existing ngxshim_release implementation:
//
// if (the pointer is a DLSS-NR wrapper) {
//     if (feature->handle)
//         NVSDK_NGX_VULKAN_ReleaseFeature(feature->handle);
//     delete feature;
//     return;
// }
//
// A production patch should reuse Caustica's existing common feature wrapper
// instead of introducing two incompatible allocation formats.
