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
import org.lwjgl.vulkan.VK10;

import java.lang.foreign.MemorySegment;

/**
 * DLSS Neural Rendering / 3D-Guided Neural Rendering.
 *
 * Evaluate this only after the SDR display transform and before UI/FG/present.
 * The loaded native shim may not expose the NR ABI; that case is a normal
 * unsupported state, not an initialization failure for RR/FG.
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

    private RtDlssNr() {}

    public boolean isAvailable() {
        return probed && available && !failed;
    }

    public boolean isReady() {
        return isAvailable()
                && feature != null
                && !feature.equals(MemorySegment.NULL)
                && output != null;
    }

    public void probeAvailabilityOnce() {
        if (probed || failed)
            return;

        if (!(((GpuDeviceAccessor) RenderSystem.getDevice())
                .caustica$getBackend() instanceof VulkanDevice device)) {
            return;
        }

        NgxLibrary runtime = NgxRuntime.INSTANCE.acquire(device);
        if (runtime == null)
            return;

        lib = runtime;
        probed = true;

        if (!lib.hasDlssNr()) {
            available = false;
            CausticaMod.LOGGER.info(
                    "DLSS Neural Rendering available: false (ngxshim has no DLSS-NR ABI)");
            return;
        }

        try {
            available = lib.dlssNrAvailable();
            CausticaMod.LOGGER.info(
                    "DLSS Neural Rendering available: {}", available);
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.error(
                    "DLSS Neural Rendering capability probe failed", t);
        }
    }

    /**
     * Creates/recreates the display-resolution feature and output scratch image.
     */
    public boolean ensureFeature(long cmd, int displayWidth, int displayHeight) {
        if (failed)
            return false;

        probeAvailabilityOnce();
        if (!available || lib == null)
            return false;

        try {
            if (width == displayWidth
                    && height == displayHeight
                    && feature != null
                    && !feature.equals(MemorySegment.NULL)
                    && output != null) {
                return true;
            }

            destroyFeatureOnly();

            RtContext ctx = RtContext.currentOrNull();
            if (ctx == null)
                return false;

            feature = lib.createDlssNr(cmd, displayWidth, displayHeight);
            if (feature == null || feature.equals(MemorySegment.NULL)) {
                throw new IllegalStateException(
                        "ngxshim_create_dlssnr failed: 0x"
                                + Integer.toHexString(lib.lastResult()));
            }

            // NVIDIA's public reference uses a floating-point display-res
            // scratch resource and copies/blits the result back to the
            // tonemapped display image.
            output = ctx.createStorageImage(
                    displayWidth,
                    displayHeight,
                    VK10.VK_FORMAT_R32G32B32A32_SFLOAT,
                    "DLSS-NR output " + displayWidth + "x" + displayHeight);

            width = displayWidth;
            height = displayHeight;
            resetHistory = true;

            CausticaMod.LOGGER.info(
                    "DLSS Neural Rendering feature created: {}x{}",
                    displayWidth,
                    displayHeight);

            return true;
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.error(
                    "DLSS Neural Rendering feature creation failed", t);
            destroyFeatureOnly();
            return false;
        }
    }

    /**
     * Evaluates NR on the already-tonemapped SDR display image.
     *
     * @param color       display-resolution LDR input (0..1)
     * @param depth       RR guide depth at render resolution
     * @param motion      RR motion vectors at render resolution
     * @param controlMask optional material mask; pass null for auto-mask
     */
    public boolean evaluate(
            long cmd,
            RtImage color,
            RtImage depth,
            RtImage motion,
            RtImage controlMask,
            int renderWidth,
            int renderHeight,
            int displayWidth,
            int displayHeight) {

        if (!ensureFeature(cmd, displayWidth, displayHeight))
            return false;

        if (!CausticaConfig.Rt.DlssNr.ENABLED.value())
            return false;

        // First implementation is SDR-only. NVIDIA's public sample evaluates
        // NR after tonemapping on LDR input; feeding PQ/scene-linear HDR here
        // is intentionally forbidden until separately validated.
        if (CausticaConfig.Rt.Hdr.ENABLED.value()) {
            CausticaMod.LOGGER.debug(
                    "DLSS Neural Rendering skipped: native HDR output is active");
            return false;
        }

        float mvScaleX = renderWidth > 0
                ? (float) displayWidth / renderWidth : 1.0f;
        float mvScaleY = renderHeight > 0
                ? (float) displayHeight / renderHeight : 1.0f;

        try {
            int rc = lib.evaluateDlssNr(
                    cmd,
                    feature,
                    color.view, color.image, VK10.VK_FORMAT_R8G8B8A8_UNORM,
                    depth.view, depth.image, VK10.VK_FORMAT_R16_SFLOAT,
                    motion.view, motion.image, VK10.VK_FORMAT_R16G16_SFLOAT,
                    controlMask != null ? controlMask.view : 0L,
                    controlMask != null ? controlMask.image : 0L,
                    controlMask != null
                            ? VK10.VK_FORMAT_R16G16B16A16_SFLOAT : 0,
                    output.view, output.image,
                    VK10.VK_FORMAT_R32G32B32A32_SFLOAT,
                    displayWidth,
                    displayHeight,
                    renderWidth,
                    renderHeight,
                    mvScaleX,
                    mvScaleY,
                    0, // RR guide depth is linear ViewZ, not reversed-Z
                    resetHistory ? 1 : 0,
                    CausticaConfig.Rt.DlssNr.INTENSITY.value(),
                    CausticaConfig.Rt.DlssNr.LOCAL_TONE.value(),
                    CausticaConfig.Rt.DlssNr.LOCAL_STRUCTURE.value(),
                    CausticaConfig.Rt.DlssNr.GLOBAL_TONE.value(),
                    CausticaConfig.Rt.DlssNr.SKIN_STRUCTURE.value(),
                    CausticaConfig.Rt.DlssNr.STYLE.value(),
                    controlMask == null
                            && CausticaConfig.Rt.DlssNr.AUTO_MASK.value()
                            ? 1 : 0);

            resetHistory = false;

            if (NgxRuntime.ngxFailed(rc)) {
                throw new IllegalStateException(
                        "ngxshim_evaluate_dlssnr failed: 0x"
                                + Integer.toHexString(rc)
                                + " last=0x"
                                + Integer.toHexString(lib.lastResult()));
            }

            return true;
        } catch (Throwable t) {
            failed = true;
            CausticaMod.LOGGER.error(
                    "DLSS Neural Rendering evaluation failed; continuing without NR",
                    t);
            return false;
        }
    }

    public RtImage output() {
        return output;
    }

    public void resetHistory() {
        resetHistory = true;
    }

    public void destroy() {
        RtContext ctx = RtContext.currentOrNull();
        if (ctx != null) {
            ctx.waitIdle();
        }
        destroyFeatureOnly();
        probed = false;
        available = false;
        failed = false;
        lib = null;
    }

    private void destroyFeatureOnly() {
        if (lib != null
                && feature != null
                && !feature.equals(MemorySegment.NULL)) {
            try {
                lib.release(feature);
            } catch (Throwable t) {
                CausticaMod.LOGGER.warn(
                        "DLSS-NR feature release failed", t);
            }
        }

        feature = MemorySegment.NULL;

        if (output != null) {
            try {
                output.destroy();
            } catch (Throwable t) {
                CausticaMod.LOGGER.warn(
                        "DLSS-NR scratch image release failed", t);
            }
            output = null;
        }

        width = -1;
        height = -1;
    }
}
