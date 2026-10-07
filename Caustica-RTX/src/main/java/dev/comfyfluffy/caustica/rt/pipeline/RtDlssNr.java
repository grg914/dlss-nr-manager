package dev.comfyfluffy.caustica.rt.pipeline;

import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.vulkan.VulkanDevice;
import dev.comfyfluffy.caustica.CausticaConfig;
import dev.comfyfluffy.caustica.CausticaMod;
import dev.comfyfluffy.caustica.mixin.GpuDeviceAccessor;
import dev.comfyfluffy.caustica.ngx.NgxLibrary;
import dev.comfyfluffy.caustica.ngx.NgxRuntime;
import dev.comfyfluffy.caustica.rt.RtContext;
import dev.comfyfluffy.caustica.rt.accel.RtImage;
import java.lang.foreign.MemorySegment;
import org.lwjgl.vulkan.VK10;

/**
 * Optional DLSS Neural Rendering / 3D-Guided Neural Rendering backend.
 *
 * <p>The public Caustica build remains fully functional without the NVIDIA NR SDK:
 * the native shim keeps the ABI but reports unavailable. When a compatible authorized
 * SDK/runtime is supplied at build/runtime, this class creates and evaluates the real
 * NGX feature after SDR display mapping and before UI/Frame Generation.
 */
public final class RtDlssNr {
    public static final RtDlssNr INSTANCE = new RtDlssNr();

    private NgxLibrary lib;
    private MemorySegment feature = MemorySegment.NULL;
    private RtImage output;
    private boolean probed;
    private boolean available;
    private boolean failed;
    private boolean resetHistory = true;
    private int width = -1;
    private int height = -1;

    private RtDlssNr() {
    }

    public boolean isAvailable() {
        if (!probed && !failed) {
            probe();
        }
        return available && !failed;
    }

    public boolean enabled() {
        return CausticaConfig.Rt.DlssNr.ENABLED.value() && isAvailable()
                && RtDlssRr.enabled()
                && !CausticaConfig.Rt.Hdr.enabled();
    }

    public RtImage output() {
        return output;
    }

    private void probe() {
        if (probed || failed) {
            return;
        }
        probed = true;
        try {
            if (!(((GpuDeviceAccessor) RenderSystem.getDevice()).caustica$getBackend() instanceof VulkanDevice device)) {
                return;
            }
            lib = NgxRuntime.INSTANCE.acquire(device);
            if (lib == null || !lib.hasDlssNr()) {
                CausticaMod.LOGGER.info("DLSS Neural Rendering available: false (optional shim ABI absent)");
                return;
            }
            available = lib.dlssNrAvailable();
            CausticaMod.LOGGER.info("DLSS Neural Rendering available: {}", available);
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.warn("DLSS Neural Rendering capability probe failed; feature disabled", t);
        }
    }

    public boolean ensureFeature(long cmd, int displayWidth, int displayHeight) {
        if (!enabled()) {
            return false;
        }
        try {
            if (!isNull(feature) && output != null && width == displayWidth && height == displayHeight) {
                return true;
            }

            destroyFeature();

            RtContext ctx = RtContext.currentOrNull();
            if (ctx == null) {
                return false;
            }

            feature = lib.createDlssNr(cmd, displayWidth, displayHeight);
            if (isNull(feature)) {
                throw new IllegalStateException(
                        "ngxshim_create_dlssnr failed: last=0x" + Integer.toHexString(lib.lastResult()));
            }

            // NVIDIA's reference integration uses an FP32 scratch target for NR and
            // converts/blits the result back into the tonemapped UNORM display image.
            output = ctx.createStorageImage(
                    displayWidth, displayHeight, VK10.VK_FORMAT_R32G32B32A32_SFLOAT,
                    "DLSS Neural Rendering FP32 output " + displayWidth + "x" + displayHeight);
            width = displayWidth;
            height = displayHeight;
            resetHistory = true;

            CausticaMod.LOGGER.info(
                    "DLSS Neural Rendering feature created: {}x{}", displayWidth, displayHeight);
            return true;
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.error(
                    "DLSS Neural Rendering setup failed; renderer continues without NR", t);
            destroyFeature();
            return false;
        }
    }

    public boolean evaluate(long cmd, RtImage color, RtImage depth, RtImage motion,
                            int guideWidth, int guideHeight,
                            int displayWidth, int displayHeight) {
        if (!ensureFeature(cmd, displayWidth, displayHeight)) {
            return false;
        }

        try {
            // Caustica stores guide MVs in render-pixel units. DLSS-NR operates at
            // display resolution, so convert them to display-pixel units exactly like
            // NVIDIA's reference path.
            float mvScaleX = guideWidth > 0 ? (float) displayWidth / guideWidth : 1.0f;
            float mvScaleY = guideHeight > 0 ? (float) displayHeight / guideHeight : 1.0f;

            int rc = lib.evaluateDlssNr(
                    cmd, feature,
                    color.view, color.image, VK10.VK_FORMAT_R8G8B8A8_UNORM,
                    depth.view, depth.image, VK10.VK_FORMAT_R32_SFLOAT,
                    motion.view, motion.image, VK10.VK_FORMAT_R16G16_SFLOAT,
                    output.view, output.image, VK10.VK_FORMAT_R32G32B32A32_SFLOAT,
                    displayWidth, displayHeight, guideWidth, guideHeight,
                    mvScaleX, mvScaleY,
                    1, resetHistory ? 1 : 0,
                    CausticaConfig.Rt.DlssNr.INTENSITY.value(),
                    CausticaConfig.Rt.DlssNr.LOCAL_TONE.value(),
                    CausticaConfig.Rt.DlssNr.LOCAL_STRUCTURE.value(),
                    CausticaConfig.Rt.DlssNr.GLOBAL_TONE.value(),
                    CausticaConfig.Rt.DlssNr.SKIN_STRUCTURE.value(),
                    CausticaConfig.Rt.DlssNr.STYLE.value(),
                    CausticaConfig.Rt.DlssNr.AUTO_MASK.value() ? 1 : 0);

            resetHistory = false;
            if (NgxRuntime.ngxFailed(rc)) {
                throw new IllegalStateException(
                        "ngxshim_evaluate_dlssnr failed: 0x" + Integer.toHexString(rc)
                                + " last=0x" + Integer.toHexString(lib.lastResult()));
            }
            return true;
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.error(
                    "DLSS Neural Rendering evaluate failed; renderer continues without NR", t);
            return false;
        }
    }

    public void resetHistory() {
        resetHistory = true;
    }

    public void destroy() {
        destroyFeature();
        lib = null;
        probed = false;
        available = false;
        failed = false;
    }

    private void destroyFeature() {
        RtContext ctx = RtContext.currentOrNull();
        if (ctx != null && (!isNull(feature) || output != null)) {
            ctx.waitIdle();
        }
        if (lib != null && !isNull(feature)) {
            try {
                lib.release(feature);
            } catch (Throwable t) {
                CausticaMod.LOGGER.warn("DLSS Neural Rendering feature release failed", t);
            }
        }
        feature = MemorySegment.NULL;
        if (output != null) {
            output.destroy();
            output = null;
        }
        width = -1;
        height = -1;
    }

    private static boolean isNull(MemorySegment segment) {
        return segment == null || segment.equals(MemorySegment.NULL);
    }
}
