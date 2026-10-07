package dev.comfyfluffy.caustica.rt;

import dev.comfyfluffy.caustica.CausticaMod;
import org.lwjgl.system.MemoryStack;
import org.lwjgl.vulkan.VK10;
import org.lwjgl.vulkan.VK12;
import org.lwjgl.vulkan.VkCommandBuffer;
import org.lwjgl.vulkan.VkQueryPoolCreateInfo;

import java.nio.LongBuffer;
import java.util.Locale;

/** Non-blocking GPU timings for the asynchronous DLSS Frame Generation queue. */
final class RtFgGpuTimings {
    private static final int SLOT_COUNT = RtFramePresenter.FRAME_SLOT_COUNT;
    private static final int MAX_GENERATED_FRAMES = RtFramePresenter.MAX_GENERATED_FRAMES;
    private static final int QUERIES_PER_SLOT = 3 + MAX_GENERATED_FRAMES * 2;
    private static final int LOG_EVERY = 120;

    private final RtContext ctx;
    private final long queryPool;
    private final boolean[] pending = new boolean[SLOT_COUNT];
    private final int[] generatedCounts = new int[SLOT_COUNT];
    private final double[] evalMs = new double[MAX_GENERATED_FRAMES];
    private final double[] blitMs = new double[MAX_GENERATED_FRAMES];
    private double snapshotMs;
    private double handoffMs;
    private int samples;

    RtFgGpuTimings(RtContext ctx) {
        this.ctx = ctx;
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkQueryPoolCreateInfo ci = VkQueryPoolCreateInfo.calloc(stack).sType$Default()
                    .queryType(VK10.VK_QUERY_TYPE_TIMESTAMP)
                    .queryCount(SLOT_COUNT * QUERIES_PER_SLOT);
            LongBuffer out = stack.mallocLong(1);
            RtContext.check(VK10.vkCreateQueryPool(ctx.vk(), ci, null, out),
                    "vkCreateQueryPool(DLSS-FG GPU timings)");
            queryPool = out.get(0);
        }
        RtDebugLabels.name(ctx, VK10.VK_OBJECT_TYPE_QUERY_POOL, queryPool, "DLSS-FG GPU timings");
    }

    void beginSnapshot(int slot, VkCommandBuffer cmd, int generatedCount) {
        int first = firstQuery(slot);
        VK12.vkCmdResetQueryPool(cmd, queryPool, first, QUERIES_PER_SLOT);
        VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, queryPool, first);
        generatedCounts[slot] = generatedCount;
        pending[slot] = true;
    }

    void endSnapshot(int slot, VkCommandBuffer cmd) {
        VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                queryPool, firstQuery(slot) + 1);
    }

    void beginGeneration(int slot, VkCommandBuffer cmd) {
        VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                queryPool, firstQuery(slot) + 2);
    }

    void endEvaluation(int slot, VkCommandBuffer cmd, int generatedIndex) {
        VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                queryPool, firstQuery(slot) + 3 + generatedIndex * 2);
    }

    void endBlit(int slot, VkCommandBuffer cmd, int generatedIndex) {
        VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                queryPool, firstQuery(slot) + 4 + generatedIndex * 2);
    }

    void collect(int slot) {
        if (!pending[slot]) {
            return;
        }
        int generatedCount = generatedCounts[slot];
        int queryCount = 3 + generatedCount * 2;
        try (MemoryStack stack = MemoryStack.stackPush()) {
            LongBuffer result = stack.mallocLong(queryCount * 2);
            int status = VK10.vkGetQueryPoolResults(ctx.vk(), queryPool, firstQuery(slot), queryCount,
                    result, 2L * Long.BYTES,
                    VK10.VK_QUERY_RESULT_64_BIT | VK10.VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
            if (status == VK10.VK_NOT_READY) {
                return;
            }
            RtContext.check(status, "vkGetQueryPoolResults(DLSS-FG GPU timings)");
            for (int query = 0; query < queryCount; query++) {
                if (result.get(query * 2 + 1) == 0L) {
                    return;
                }
            }
            double nanosPerTick = ctx.timestampPeriodNanos();
            snapshotMs += elapsedMs(result, 0, 1, nanosPerTick);
            handoffMs += elapsedMs(result, 1, 2, nanosPerTick);
            for (int generated = 0; generated < generatedCount; generated++) {
                int evalStart = generated == 0 ? 2 : 4 + (generated - 1) * 2;
                int evalEnd = 3 + generated * 2;
                int blitEnd = evalEnd + 1;
                evalMs[generated] += elapsedMs(result, evalStart, evalEnd, nanosPerTick);
                blitMs[generated] += elapsedMs(result, evalEnd, blitEnd, nanosPerTick);
            }
            pending[slot] = false;
            samples++;
            if (samples == LOG_EVERY) {
                logAverages(generatedCount);
            }
        }
    }

    private void logAverages(int generatedCount) {
        StringBuilder line = new StringBuilder("DLSS-FG GPU avg over ").append(samples)
                .append(" frames: snapshot=").append(format(snapshotMs / samples))
                .append(" handoff=").append(format(handoffMs / samples));
        double total = snapshotMs + handoffMs;
        for (int generated = 0; generated < generatedCount; generated++) {
            line.append(" eval").append(generated + 1).append('=').append(format(evalMs[generated] / samples))
                    .append(" blit").append(generated + 1).append('=').append(format(blitMs[generated] / samples));
            total += evalMs[generated] + blitMs[generated];
            evalMs[generated] = 0.0;
            blitMs[generated] = 0.0;
        }
        line.append(" total=").append(format(total / samples));
        snapshotMs = 0.0;
        handoffMs = 0.0;
        samples = 0;
        CausticaMod.LOGGER.info(line.toString());
    }

    private static double elapsedMs(LongBuffer result, int begin, int end, double nanosPerTick) {
        return (result.get(end * 2) - result.get(begin * 2)) * nanosPerTick / 1_000_000.0;
    }

    private static String format(double milliseconds) {
        return String.format(Locale.ROOT, "%.3fms", milliseconds);
    }

    private static int firstQuery(int slot) {
        return slot * QUERIES_PER_SLOT;
    }

    void destroy() {
        VK10.vkDestroyQueryPool(ctx.vk(), queryPool, null);
    }
}
