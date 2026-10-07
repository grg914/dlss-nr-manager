package dev.comfyfluffy.caustica.rt;

import com.mojang.blaze3d.vulkan.VulkanCommandEncoder;
import com.mojang.blaze3d.vulkan.VulkanDevice;
import com.mojang.blaze3d.vulkan.VulkanQueue;
import dev.comfyfluffy.caustica.CausticaConfig;
import dev.comfyfluffy.caustica.CausticaMod;
import dev.comfyfluffy.caustica.rt.accel.RtImage;
import dev.comfyfluffy.caustica.rt.pipeline.RtDlssFg;
import it.unimi.dsi.fastutil.longs.LongList;
import net.minecraft.client.Minecraft;
import org.lwjgl.PointerBuffer;
import org.lwjgl.system.MemoryStack;
import org.lwjgl.vulkan.KHRSwapchain;
import org.lwjgl.vulkan.KHRSynchronization2;
import org.lwjgl.vulkan.VK10;
import org.lwjgl.vulkan.VK12;
import org.lwjgl.vulkan.VkCommandBuffer;
import org.lwjgl.vulkan.VkCommandBufferAllocateInfo;
import org.lwjgl.vulkan.VkCommandBufferBeginInfo;
import org.lwjgl.vulkan.VkCommandBufferSubmitInfo;
import org.lwjgl.vulkan.VkCommandPoolCreateInfo;
import org.lwjgl.vulkan.VkDependencyInfo;
import org.lwjgl.vulkan.VkImageBlit;
import org.lwjgl.vulkan.VkImageCopy;
import org.lwjgl.vulkan.VkImageMemoryBarrier2;
import org.lwjgl.vulkan.VkMemoryBarrier2;
import org.lwjgl.vulkan.VkPresentInfoKHR;
import org.lwjgl.vulkan.VkQueue;
import org.lwjgl.vulkan.VkSemaphoreCreateInfo;
import org.lwjgl.vulkan.VkSemaphoreSubmitInfo;
import org.lwjgl.vulkan.VkSemaphoreTypeCreateInfo;
import org.lwjgl.vulkan.VkSubmitInfo2;

import java.nio.IntBuffer;
import java.nio.LongBuffer;

import static org.lwjgl.vulkan.KHRSynchronization2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR;
import static org.lwjgl.vulkan.KHRSynchronization2.VK_PIPELINE_STAGE_2_TRANSFER_BIT_KHR;

/**
 * DLSS Frame Generation present engine. It snapshots the completed real frame on Minecraft's primary
 * graphics queue, evaluates DLSSG on a second queue, and presents generated-then-real on a third queue from
 * the same graphics family. Separating evaluate from present avoids serializing later generated frames.
 */
public final class RtFramePresenter {
    public static final RtFramePresenter INSTANCE = new RtFramePresenter();

    static final int FRAME_SLOT_COUNT = 3;
    static final int MAX_GENERATED_FRAMES = 3;
    private static final long ACQUIRE_TIMEOUT_NS = 5_000_000L;
    private static final long LOG_INTERVAL_NS = 1_000_000_000L;

    private final FrameSlot[] slots = {new FrameSlot(), new FrameSlot(), new FrameSlot()};
    private RtContext context;
    private VulkanQueue frameQueue;
    private VulkanQueue presentQueue;
    private long commandPool;
    private long inputTimeline;
    private long completionTimeline;
    private long nextFrameValue;
    private long backbufferFrameId;
    private RtFgGpuTimings gpuTimings;

    private long[] acquireSemaphores = new long[0];
    private int acquireCursor;
    private boolean failed;

    private int resourceSwapW = -1;
    private int resourceSwapH = -1;
    private int resourceRenderW = -1;
    private int resourceRenderH = -1;
    private int resourceFormat = Integer.MIN_VALUE;
    private int resourceGeneratedCount = -1;

    private int[] pendingImageIndex = new int[0];
    private long[] pendingPresentSem = new long[0];
    private long[] pendingAcquireSem = new long[0];
    private int pendingCount;
    private FrameSlot pendingSlot;
    private long pendingFrameValue;
    private boolean routeRealPresent;

    private long logWindowStartNs;
    private int realFramesInWindow;
    private int generatedFramesInWindow;
    private int interpOkInWindow;
    private int interpFallbackInWindow;
    private int busySkipsInWindow;

    private RtFramePresenter() {
    }

    /** Whether FG extra-present should run this frame. */
    public boolean isActive() {
        Minecraft minecraft = Minecraft.getInstance();
        return !failed && RtDeviceBringup.frameQueuesReserved()
                && RtDlssFg.enabled() && RtDlssFg.INSTANCE.isAvailable()
                && minecraft.level != null
                && (!CausticaConfig.Rt.Fg.SUSPEND_IN_MENUS.value() || minecraft.gui.screen() == null);
    }

    /**
     * Acquire extra swapchain images, snapshot this real frame, and record DLSSG work for the dedicated
     * frame queue. {@link #flushPendingPresents} submits each generated frame immediately before its own
     * present, after Minecraft has submitted the snapshot on the primary graphics queue.
     */
    public void prepareExtraFrames(VulkanCommandEncoder enc, VulkanDevice device, long swapchain,
            LongList swapchainImages, long[] presentSemaphores, int swapW, int swapH,
            long srcImage, int srcW, int srcH, int generatedCount, boolean hdrBackbuffer) {
        pendingCount = 0;
        pendingSlot = null;
        pendingFrameValue = 0L;
        routeRealPresent = false;
        if (failed || swapchain == 0L || srcImage == 0L || generatedCount <= 0) {
            return;
        }

        try {
            RtContext ctx = RtContext.currentOrNull();
            if (ctx == null) {
                return;
            }
            ensureQueueState(ctx);
            RtComposite.FgInputs inputs = RtComposite.INSTANCE.prepareFgInputs(swapW, swapH, hdrBackbuffer);
            int renderW = inputs != null ? inputs.renderWidth() : 0;
            int renderH = inputs != null ? inputs.renderHeight() : 0;
            int format = inputs != null ? inputs.backbufferFormat()
                    : (hdrBackbuffer ? VK10.VK_FORMAT_R16G16B16A16_SFLOAT : VK10.VK_FORMAT_R8G8B8A8_UNORM);
            ensureSlotImages(ctx, swapW, swapH, renderW, renderH, format, generatedCount);

            FrameSlot slot = findFreeSlot();
            if (slot == null) {
                busySkipsInWindow++;
                RtComposite.INSTANCE.resetFgHistory();
                return;
            }

            ensureAcquireCapacity(device, Math.max(swapchainImages.size() + 1,
                    FRAME_SLOT_COUNT * generatedCount), generatedCount);
            for (int i = 0; i < generatedCount; i++) {
                long acquireSem = acquireSemaphores[acquireCursor];
                acquireCursor = (acquireCursor + 1) % acquireSemaphores.length;
                int imageIndex;
                try (MemoryStack stack = MemoryStack.stackPush()) {
                    IntBuffer pIndex = stack.callocInt(1);
                    int acquireResult = KHRSwapchain.vkAcquireNextImageKHR(
                            device.vkDevice(), swapchain, ACQUIRE_TIMEOUT_NS, acquireSem, 0L, pIndex);
                    if (acquireResult == VK10.VK_TIMEOUT || acquireResult == VK10.VK_NOT_READY
                            || acquireResult == KHRSwapchain.VK_ERROR_OUT_OF_DATE_KHR) {
                        break;
                    }
                    if (acquireResult != VK10.VK_SUCCESS
                            && acquireResult != KHRSwapchain.VK_SUBOPTIMAL_KHR) {
                        throw new IllegalStateException("vkAcquireNextImageKHR(FG) failed: " + acquireResult);
                    }
                    imageIndex = pIndex.get(0);
                }
                pendingImageIndex[pendingCount] = imageIndex;
                pendingPresentSem[pendingCount] = presentSemaphores[imageIndex];
                pendingAcquireSem[pendingCount] = acquireSem;
                pendingCount++;
            }
            if (pendingCount == 0) {
                RtComposite.INSTANCE.resetFgHistory();
                return;
            }

            long frameValue = ++nextFrameValue;
            recordSnapshot(enc, slot, srcImage, srcW, srcH, swapW, swapH, inputs);
            enc.signalSemaphore(inputTimeline, frameValue, VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR);
            recordGeneratedFrames(slot, swapchainImages, swapW, swapH, inputs, pendingCount,
                    backbufferFrameId + 1L);
            slot.completionValue = frameValue;
            pendingSlot = slot;
            pendingFrameValue = frameValue;
            if (inputs != null) {
                RtComposite.INSTANCE.markFgEvaluationRecorded();
                interpOkInWindow += pendingCount;
            } else {
                interpFallbackInWindow += pendingCount;
            }
        } catch (Throwable t) {
            failed = true;
            pendingCount = 0;
            pendingSlot = null;
            pendingFrameValue = 0L;
            routeRealPresent = false;
            CausticaMod.LOGGER.error("DLSS-FG async present-record failed; frame generation disabled", t);
        }
    }

    /** Submit and queue each generated frame in display order before vanilla queues the real frame. */
    public void flushPendingPresents(long swapchain) {
        backbufferFrameId++;
        int presentedThisFrame = 0;
        if (!failed && pendingCount != 0 && pendingSlot != null) {
            try (MemoryStack stack = MemoryStack.stackPush()) {
                synchronized (context.deviceQueueHostLock()) {
                    boolean outOfDate = false;
                    for (int i = 0; i < pendingCount; i++) {
                        submitGeneratedFrame(pendingSlot, pendingFrameValue, i, i == pendingCount - 1);
                        if (outOfDate) {
                            continue;
                        }
                        VkPresentInfoKHR present = VkPresentInfoKHR.calloc(stack).sType$Default()
                                .pWaitSemaphores(stack.longs(pendingPresentSem[i]))
                                .swapchainCount(1)
                                .pSwapchains(stack.longs(swapchain))
                                .pImageIndices(stack.ints(pendingImageIndex[i]));
                        int result = KHRSwapchain.vkQueuePresentKHR(presentQueue.vkQueue(), present);
                        if (result == KHRSwapchain.VK_ERROR_OUT_OF_DATE_KHR) {
                            outOfDate = true;
                            continue;
                        }
                        if (result != VK10.VK_SUCCESS && result != KHRSwapchain.VK_SUBOPTIMAL_KHR) {
                            throw new IllegalStateException("vkQueuePresentKHR(FG) failed: " + result);
                        }
                        presentedThisFrame++;
                    }
                }
            } catch (Throwable t) {
                failed = true;
                CausticaMod.LOGGER.error("DLSS-FG present failed; frame generation disabled", t);
            } finally {
                pendingCount = 0;
                pendingSlot = null;
                pendingFrameValue = 0L;
            }
        }
        routeRealPresent = presentedThisFrame != 0;
        if (RtDlssFg.enabled()) {
            logPresentRate(presentedThisFrame);
        }
    }

    /** Route the matching real present behind generated presents without stalling the primary graphics queue. */
    public VkQueue consumeRealPresentQueue(VkQueue vanillaQueue) {
        if (!routeRealPresent || presentQueue == null) {
            return vanillaQueue;
        }
        routeRealPresent = false;
        return presentQueue.vkQueue();
    }

    private void ensureQueueState(RtContext ctx) {
        if (context == ctx && commandPool != 0L) {
            return;
        }
        if (!RtDeviceBringup.frameQueuesReserved()) {
            throw new IllegalStateException("DLSS-FG requires dedicated evaluate and present queues");
        }
        context = ctx;
        frameQueue = ctx.frameQueue();
        presentQueue = ctx.presentQueue();
        inputTimeline = createTimeline("DLSS-FG input timeline");
        completionTimeline = createTimeline("DLSS-FG completion timeline");
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkCommandPoolCreateInfo ci = VkCommandPoolCreateInfo.calloc(stack).sType$Default()
                    .flags(VK10.VK_COMMAND_POOL_CREATE_TRANSIENT_BIT
                            | VK10.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT)
                    .queueFamilyIndex(frameQueue.queueFamilyIndex());
            LongBuffer pPool = stack.mallocLong(1);
            RtContext.check(VK10.vkCreateCommandPool(ctx.vk(), ci, null, pPool),
                    "vkCreateCommandPool(DLSS-FG)");
            commandPool = pPool.get(0);
            RtDebugLabels.name(ctx, VK10.VK_OBJECT_TYPE_COMMAND_POOL, commandPool, "DLSS-FG command pool");

            int commandCount = FRAME_SLOT_COUNT * MAX_GENERATED_FRAMES;
            VkCommandBufferAllocateInfo ai = VkCommandBufferAllocateInfo.calloc(stack).sType$Default()
                    .commandPool(commandPool).level(VK10.VK_COMMAND_BUFFER_LEVEL_PRIMARY)
                    .commandBufferCount(commandCount);
            PointerBuffer pCommands = stack.mallocPointer(commandCount);
            RtContext.check(VK10.vkAllocateCommandBuffers(ctx.vk(), ai, pCommands),
                    "vkAllocateCommandBuffers(DLSS-FG)");
            for (int slotIndex = 0; slotIndex < FRAME_SLOT_COUNT; slotIndex++) {
                FrameSlot slot = slots[slotIndex];
                slot.index = slotIndex;
                for (int generatedIndex = 0; generatedIndex < MAX_GENERATED_FRAMES; generatedIndex++) {
                    int commandIndex = slotIndex * MAX_GENERATED_FRAMES + generatedIndex;
                    slot.commandBuffers[generatedIndex] = new VkCommandBuffer(pCommands.get(commandIndex), ctx.vk());
                    RtDebugLabels.name(ctx, VK10.VK_OBJECT_TYPE_COMMAND_BUFFER,
                            slot.commandBuffers[generatedIndex].address(),
                            "DLSS-FG frame slot " + slotIndex + " output " + generatedIndex);
                }
            }
        }
        if (RtFrameStats.enabled()) {
            gpuTimings = new RtFgGpuTimings(ctx);
        }
        CausticaMod.LOGGER.info(
                "DLSS-FG async pipeline active on graphics-family evaluate queue <{},{}> and present queue <{},{}>",
                frameQueue.queueFamilyIndex(), RtDeviceBringup.frameQueueIndex(),
                presentQueue.queueFamilyIndex(), RtDeviceBringup.presentQueueIndex());
    }

    private long createTimeline(String label) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkSemaphoreTypeCreateInfo type = VkSemaphoreTypeCreateInfo.calloc(stack).sType$Default()
                    .semaphoreType(VK12.VK_SEMAPHORE_TYPE_TIMELINE).initialValue(0L);
            VkSemaphoreCreateInfo ci = VkSemaphoreCreateInfo.calloc(stack).sType$Default().pNext(type);
            LongBuffer out = stack.mallocLong(1);
            RtContext.check(VK10.vkCreateSemaphore(context.vk(), ci, null, out),
                    "vkCreateSemaphore(" + label + ")");
            long semaphore = out.get(0);
            RtDebugLabels.name(context, VK10.VK_OBJECT_TYPE_SEMAPHORE, semaphore, label);
            return semaphore;
        }
    }

    private void ensureSlotImages(RtContext ctx, int swapW, int swapH, int renderW, int renderH,
                                  int format, int generatedCount) {
        if (resourceSwapW == swapW && resourceSwapH == swapH
                && resourceRenderW == renderW && resourceRenderH == renderH
                && resourceFormat == format && resourceGeneratedCount == generatedCount) {
            return;
        }
        if (resourceSwapW >= 0) {
            ctx.waitIdle();
            destroySlotImages();
        }
        for (int i = 0; i < FRAME_SLOT_COUNT; i++) {
            slots[i].allocate(ctx, i, swapW, swapH, renderW, renderH, format, generatedCount);
        }
        resourceSwapW = swapW;
        resourceSwapH = swapH;
        resourceRenderW = renderW;
        resourceRenderH = renderH;
        resourceFormat = format;
        resourceGeneratedCount = generatedCount;
    }

    private FrameSlot findFreeSlot() {
        long completed = queryTimeline(completionTimeline);
        for (FrameSlot slot : slots) {
            if (slot.completionValue <= completed) {
                if (gpuTimings != null) {
                    gpuTimings.collect(slot.index);
                }
                return slot;
            }
        }
        return null;
    }

    private long queryTimeline(long semaphore) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            LongBuffer out = stack.mallocLong(1);
            RtContext.check(VK12.vkGetSemaphoreCounterValue(context.vk(), semaphore, out),
                    "vkGetSemaphoreCounterValue(DLSS-FG)");
            return out.get(0);
        }
    }

    private void recordSnapshot(VulkanCommandEncoder enc, FrameSlot slot, long srcImage,
                                int srcW, int srcH, int swapW, int swapH, RtComposite.FgInputs inputs) {
        VkCommandBuffer cmd = enc.allocateAndBeginTransientCommandBuffer();
        if (gpuTimings != null) {
            gpuTimings.beginSnapshot(slot.index, cmd, pendingCount);
        }
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VulkanCommandEncoder.memoryBarrier(cmd, stack);
            recordGeneralBlit(cmd, srcImage, srcW, srcH, slot.backbuffer.image, swapW, swapH,
                    VK10.VK_FILTER_NEAREST);
            if (inputs != null) {
                recordGeneralCopy(cmd, inputs.depth().image, slot.depth.image,
                        inputs.renderWidth(), inputs.renderHeight());
                recordGeneralCopy(cmd, inputs.motion().image, slot.motion.image,
                        inputs.renderWidth(), inputs.renderHeight());
                if (inputs.hudless() != null) {
                    recordGeneralCopy(cmd, inputs.hudless().image, slot.hudless.image, swapW, swapH);
                }
                if (inputs.uiImage() != 0L) {
                    recordGeneralCopy(cmd, inputs.uiImage(), slot.ui.image, swapW, swapH);
                }
            }
            VulkanCommandEncoder.memoryBarrier(cmd, stack);
        }
        if (gpuTimings != null) {
            gpuTimings.endSnapshot(slot.index, cmd);
        }
        RtContext.check(VK10.vkEndCommandBuffer(cmd), "vkEndCommandBuffer(DLSS-FG snapshot)");
        enc.execute(cmd);
    }

    private void recordGeneratedFrames(FrameSlot slot, LongList swapchainImages, int swapW, int swapH,
                                       RtComposite.FgInputs inputs, int count, long frameId) {
        for (int i = 0; i < count; i++) {
            VkCommandBuffer cmd = slot.commandBuffers[i];
            RtContext.check(VK10.vkResetCommandBuffer(cmd, 0), "vkResetCommandBuffer(DLSS-FG)");
            try (MemoryStack stack = MemoryStack.stackPush()) {
                VkCommandBufferBeginInfo begin = VkCommandBufferBeginInfo.calloc(stack).sType$Default()
                        .flags(VK10.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT);
                RtContext.check(VK10.vkBeginCommandBuffer(cmd, begin), "vkBeginCommandBuffer(DLSS-FG)");
            }
            if (gpuTimings != null && i == 0) {
                gpuTimings.beginGeneration(slot.index, cmd);
            }
            long blitSource = slot.backbuffer.image;
            if (inputs != null) {
                RtImage output = slot.interpolated[i];
                boolean ok = RtDlssFg.INSTANCE.evaluate(cmd.address(),
                        slot.backbuffer.view, slot.backbuffer.image, inputs.backbufferFormat(),
                        slot.depth.view, slot.depth.image, VK10.VK_FORMAT_R32_SFLOAT,
                        slot.motion.view, slot.motion.image, VK10.VK_FORMAT_R16G16_SFLOAT,
                        inputs.hudless() != null ? slot.hudless.view : 0L,
                        inputs.hudless() != null ? slot.hudless.image : 0L,
                        inputs.hudless() != null ? inputs.hudlessFormat() : 0,
                        inputs.uiImage() != 0L ? slot.ui.view : 0L,
                        inputs.uiImage() != 0L ? slot.ui.image : 0L,
                        inputs.uiImage() != 0L ? VK10.VK_FORMAT_R8G8B8A8_UNORM : 0,
                        output.view, output.image, inputs.backbufferFormat(),
                        swapW, swapH, inputs.renderWidth(), inputs.renderHeight(), count, i + 1,
                        frameId, 1.0f, 1.0f,
                        true, inputs.hdrBackbuffer(), true, inputs.reset() && i == 0,
                        inputs.clipToPrevClip(), inputs.prevClipToClip());
                if (!ok) {
                    throw new IllegalStateException("ngxshim_evaluate_dlssg failed");
                }
                if (gpuTimings != null) {
                    gpuTimings.endEvaluation(slot.index, cmd, i);
                }
                blitSource = output.image;
            }
            recordSwapchainBlit(cmd, blitSource,
                    swapchainImages.getLong(pendingImageIndex[i]), swapW, swapH);
            if (gpuTimings != null) {
                gpuTimings.endBlit(slot.index, cmd, i);
            }
            RtContext.check(VK10.vkEndCommandBuffer(cmd), "vkEndCommandBuffer(DLSS-FG)");
        }
    }

    private void submitGeneratedFrame(FrameSlot slot, long frameValue, int generatedIndex, boolean last) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            boolean first = generatedIndex == 0;
            VkSemaphoreSubmitInfo.Buffer waits = VkSemaphoreSubmitInfo.calloc(first ? 2 : 1, stack);
            int waitIndex = 0;
            if (first) {
                waits.get(waitIndex++).sType$Default().semaphore(inputTimeline).value(frameValue)
                        .stageMask(VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR);
            }
            waits.get(waitIndex).sType$Default().semaphore(pendingAcquireSem[generatedIndex]).value(0L)
                    .stageMask(VK_PIPELINE_STAGE_2_TRANSFER_BIT_KHR);

            VkSemaphoreSubmitInfo.Buffer signals = VkSemaphoreSubmitInfo.calloc(last ? 2 : 1, stack);
            signals.get(0).sType$Default().semaphore(pendingPresentSem[generatedIndex]).value(0L)
                    .stageMask(VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR);
            if (last) {
                signals.get(1).sType$Default().semaphore(completionTimeline).value(frameValue)
                        .stageMask(VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR);
            }

            VkCommandBufferSubmitInfo.Buffer command = VkCommandBufferSubmitInfo.calloc(1, stack)
                    .sType$Default().commandBuffer(slot.commandBuffers[generatedIndex]);
            VkSubmitInfo2.Buffer submit = VkSubmitInfo2.calloc(1, stack).sType$Default()
                    .pWaitSemaphoreInfos(waits).pCommandBufferInfos(command).pSignalSemaphoreInfos(signals);
            VulkanDiagnostics.noteQueueSubmission(frameQueue.vkQueue(), "Caustica DLSS-FG frame queue");
            RtContext.check(KHRSynchronization2.vkQueueSubmit2KHR(frameQueue.vkQueue(), submit, 0L),
                    "vkQueueSubmit2KHR(DLSS-FG)");
        }
    }

    private static void recordGeneralCopy(VkCommandBuffer cmd, long srcImage, long dstImage,
                                          int width, int height) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkImageCopy.Buffer region = VkImageCopy.calloc(1, stack);
            region.get(0).srcSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).dstSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).extent().set(width, height, 1);
            VK10.vkCmdCopyImage(cmd, srcImage, VK10.VK_IMAGE_LAYOUT_GENERAL,
                    dstImage, VK10.VK_IMAGE_LAYOUT_GENERAL, region);
        }
    }

    private static void recordGeneralBlit(VkCommandBuffer cmd, long srcImage, int srcW, int srcH,
                                          long dstImage, int dstW, int dstH, int filter) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkImageBlit.Buffer region = VkImageBlit.calloc(1, stack);
            region.get(0).srcSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).dstSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).srcOffsets(1).set(srcW, srcH, 1);
            region.get(0).dstOffsets(1).set(dstW, dstH, 1);
            VK10.vkCmdBlitImage(cmd, srcImage, VK10.VK_IMAGE_LAYOUT_GENERAL,
                    dstImage, VK10.VK_IMAGE_LAYOUT_GENERAL, region, filter);
        }
    }

    private static void recordSwapchainBlit(VkCommandBuffer cmd, long srcImage, long dstImage,
                                            int width, int height) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkImageMemoryBarrier2.Buffer toDst = VkImageMemoryBarrier2.calloc(1, stack).sType$Default();
            toDst.get(0).srcStageMask(0L).srcAccessMask(0L)
                    .dstStageMask(VK_PIPELINE_STAGE_2_TRANSFER_BIT_KHR).dstAccessMask(4096L)
                    .oldLayout(VK10.VK_IMAGE_LAYOUT_UNDEFINED).newLayout(VK10.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
                    .srcQueueFamilyIndex(VK10.VK_QUEUE_FAMILY_IGNORED)
                    .dstQueueFamilyIndex(VK10.VK_QUEUE_FAMILY_IGNORED).image(dstImage);
            toDst.get(0).subresourceRange().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .baseMipLevel(0).levelCount(1).baseArrayLayer(0).layerCount(1);
            VkMemoryBarrier2.Buffer srcVisible = VkMemoryBarrier2.calloc(1, stack).sType$Default();
            srcVisible.get(0).srcStageMask(VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR).srcAccessMask(65536L)
                    .dstStageMask(VK_PIPELINE_STAGE_2_TRANSFER_BIT_KHR).dstAccessMask(2048L);
            KHRSynchronization2.vkCmdPipelineBarrier2KHR(cmd,
                    VkDependencyInfo.calloc(stack).sType$Default()
                            .pImageMemoryBarriers(toDst).pMemoryBarriers(srcVisible));

            VkImageBlit.Buffer region = VkImageBlit.calloc(1, stack);
            region.get(0).srcSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).dstSubresource().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .mipLevel(0).baseArrayLayer(0).layerCount(1);
            region.get(0).srcOffsets(1).set(width, height, 1);
            region.get(0).dstOffsets(0).set(0, height, 0);
            region.get(0).dstOffsets(1).set(width, 0, 1);
            VK10.vkCmdBlitImage(cmd, srcImage, VK10.VK_IMAGE_LAYOUT_GENERAL,
                    dstImage, VK10.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, region, VK10.VK_FILTER_NEAREST);

            VkImageMemoryBarrier2.Buffer toPresent = VkImageMemoryBarrier2.calloc(1, stack).sType$Default();
            toPresent.get(0).srcStageMask(VK_PIPELINE_STAGE_2_TRANSFER_BIT_KHR).srcAccessMask(4096L)
                    .dstStageMask(VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR).dstAccessMask(0L)
                    .oldLayout(VK10.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
                    .newLayout(KHRSwapchain.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR)
                    .srcQueueFamilyIndex(VK10.VK_QUEUE_FAMILY_IGNORED)
                    .dstQueueFamilyIndex(VK10.VK_QUEUE_FAMILY_IGNORED).image(dstImage);
            toPresent.get(0).subresourceRange().aspectMask(VK10.VK_IMAGE_ASPECT_COLOR_BIT)
                    .baseMipLevel(0).levelCount(1).baseArrayLayer(0).layerCount(1);
            KHRSynchronization2.vkCmdPipelineBarrier2KHR(cmd,
                    VkDependencyInfo.calloc(stack).sType$Default().pImageMemoryBarriers(toPresent));
        }
    }

    private void ensureAcquireCapacity(VulkanDevice device, int semaphoreCount, int generatedCount) {
        if (pendingImageIndex.length < generatedCount) {
            pendingImageIndex = new int[generatedCount];
            pendingPresentSem = new long[generatedCount];
            pendingAcquireSem = new long[generatedCount];
        }
        if (acquireSemaphores.length >= semaphoreCount) {
            return;
        }
        destroyAcquireSemaphores(device);
        acquireSemaphores = new long[semaphoreCount];
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkSemaphoreCreateInfo ci = VkSemaphoreCreateInfo.calloc(stack).sType$Default();
            LongBuffer out = stack.mallocLong(1);
            for (int i = 0; i < semaphoreCount; i++) {
                RtContext.check(VK10.vkCreateSemaphore(device.vkDevice(), ci, null, out),
                        "vkCreateSemaphore(DLSS-FG acquire)");
                acquireSemaphores[i] = out.get(0);
            }
        }
        acquireCursor = 0;
    }

    private void logPresentRate(int generatedThisFrame) {
        realFramesInWindow++;
        generatedFramesInWindow += generatedThisFrame;
        long now = System.nanoTime();
        if (logWindowStartNs == 0L) {
            logWindowStartNs = now;
            return;
        }
        long elapsed = now - logWindowStartNs;
        if (elapsed < LOG_INTERVAL_NS) {
            return;
        }
        double seconds = elapsed / 1.0e9;
        double realFps = realFramesInWindow / seconds;
        double totalFps = (realFramesInWindow + generatedFramesInWindow) / seconds;
        CausticaMod.LOGGER.info(
                "[FG present-rate] real={} gen={} realFps={} totalPresentFps={} configuredMultiFrameCount={} "
                        + "interpOk={} interpFallbackDuplicate={} busySkips={}",
                realFramesInWindow, generatedFramesInWindow,
                String.format("%.1f", realFps), String.format("%.1f", totalFps),
                RtDlssFg.INSTANCE.effectiveMultiFrameCount(), interpOkInWindow, interpFallbackInWindow,
                busySkipsInWindow);
        logWindowStartNs = now;
        realFramesInWindow = 0;
        generatedFramesInWindow = 0;
        interpOkInWindow = 0;
        interpFallbackInWindow = 0;
        busySkipsInWindow = 0;
    }

    /** Device teardown after feature release/device idle. */
    public void destroy(VulkanDevice device) {
        if (context != null) {
            context.waitIdle();
        } else {
            VK10.vkDeviceWaitIdle(device.vkDevice());
        }
        destroySlotImages();
        destroyAcquireSemaphores(device);
        if (commandPool != 0L) {
            VK10.vkDestroyCommandPool(device.vkDevice(), commandPool, null);
            commandPool = 0L;
        }
        if (inputTimeline != 0L) {
            VK10.vkDestroySemaphore(device.vkDevice(), inputTimeline, null);
            inputTimeline = 0L;
        }
        if (completionTimeline != 0L) {
            VK10.vkDestroySemaphore(device.vkDevice(), completionTimeline, null);
            completionTimeline = 0L;
        }
        if (gpuTimings != null) {
            gpuTimings.destroy();
            gpuTimings = null;
        }
        for (FrameSlot slot : slots) {
            for (int i = 0; i < slot.commandBuffers.length; i++) {
                slot.commandBuffers[i] = null;
            }
            slot.completionValue = 0L;
        }
        context = null;
        frameQueue = null;
        presentQueue = null;
        nextFrameValue = 0L;
        backbufferFrameId = 0L;
        pendingCount = 0;
        pendingSlot = null;
        pendingFrameValue = 0L;
        routeRealPresent = false;
    }

    private void destroyAcquireSemaphores(VulkanDevice device) {
        for (long semaphore : acquireSemaphores) {
            if (semaphore != 0L) {
                VK10.vkDestroySemaphore(device.vkDevice(), semaphore, null);
            }
        }
        acquireSemaphores = new long[0];
        acquireCursor = 0;
    }

    private void destroySlotImages() {
        for (FrameSlot slot : slots) {
            slot.destroyImages();
        }
        resourceSwapW = -1;
        resourceSwapH = -1;
        resourceRenderW = -1;
        resourceRenderH = -1;
        resourceFormat = Integer.MIN_VALUE;
        resourceGeneratedCount = -1;
    }

    private static final class FrameSlot {
        int index;
        final VkCommandBuffer[] commandBuffers = new VkCommandBuffer[MAX_GENERATED_FRAMES];
        RtImage backbuffer;
        RtImage depth;
        RtImage motion;
        RtImage hudless;
        RtImage ui;
        RtImage[] interpolated = new RtImage[0];
        long completionValue;

        void allocate(RtContext ctx, int slot, int swapW, int swapH, int renderW, int renderH,
                      int format, int generatedCount) {
            backbuffer = ctx.createStorageImage(swapW, swapH, format,
                    "FG slot " + slot + " backbuffer " + swapW + "x" + swapH);
            if (renderW <= 0 || renderH <= 0) {
                return;
            }
            depth = ctx.createStorageImage(renderW, renderH, VK10.VK_FORMAT_R32_SFLOAT,
                    "FG slot " + slot + " depth " + renderW + "x" + renderH);
            motion = ctx.createStorageImage(renderW, renderH, VK10.VK_FORMAT_R16G16_SFLOAT,
                    "FG slot " + slot + " motion " + renderW + "x" + renderH);
            hudless = ctx.createStorageImage(swapW, swapH, format,
                    "FG slot " + slot + " hudless " + swapW + "x" + swapH);
            ui = ctx.createStorageImage(swapW, swapH, VK10.VK_FORMAT_R8G8B8A8_UNORM,
                    "FG slot " + slot + " UI " + swapW + "x" + swapH);
            interpolated = new RtImage[generatedCount];
            for (int i = 0; i < generatedCount; i++) {
                interpolated[i] = ctx.createStorageImage(swapW, swapH, format,
                        "FG slot " + slot + " interp " + i + " " + swapW + "x" + swapH);
            }
        }

        void destroyImages() {
            destroy(backbuffer);
            destroy(depth);
            destroy(motion);
            destroy(hudless);
            destroy(ui);
            for (RtImage image : interpolated) {
                destroy(image);
            }
            backbuffer = null;
            depth = null;
            motion = null;
            hudless = null;
            ui = null;
            interpolated = new RtImage[0];
            completionValue = 0L;
        }

        private static void destroy(RtImage image) {
            if (image != null) {
                image.destroy();
            }
        }
    }
}
