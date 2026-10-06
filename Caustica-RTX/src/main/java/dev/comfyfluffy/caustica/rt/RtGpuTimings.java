package dev.comfyfluffy.caustica.rt;

import dev.comfyfluffy.caustica.CausticaMod;
import org.lwjgl.system.MemoryStack;
import org.lwjgl.vulkan.VK10;
import org.lwjgl.vulkan.VK12;
import org.lwjgl.vulkan.VkCommandBuffer;
import org.lwjgl.vulkan.VkQueryPoolCreateInfo;

import java.nio.LongBuffer;
import java.util.Locale;

/** Non-blocking Vulkan timestamp ring used only while frame statistics are enabled. */
final class RtGpuTimings {
    private static final String[] STAGES = {
            "setup", "primary", "indirect", "denoise", "exposure", "bloom", "display", "copy"
    };
    private static final int SLOT_COUNT = 8;
    private static final int QUERY_COUNT = STAGES.length + 1;
    private static final int LOG_EVERY = 120;

    private final RtContext ctx;
    private final long queryPool;
    private final boolean[] pending = new boolean[SLOT_COUNT];
    private final double[] accumulatedMs = new double[STAGES.length];
    private int cursor;
    private int samples;

    RtGpuTimings(RtContext ctx) {
        this.ctx = ctx;
        try (MemoryStack stack = MemoryStack.stackPush()) {
            VkQueryPoolCreateInfo ci = VkQueryPoolCreateInfo.calloc(stack).sType$Default()
                    .queryType(VK10.VK_QUERY_TYPE_TIMESTAMP)
                    .queryCount(SLOT_COUNT * QUERY_COUNT);
            LongBuffer out = stack.mallocLong(1);
            RtContext.check(VK10.vkCreateQueryPool(ctx.vk(), ci, null, out),
                    "vkCreateQueryPool(RT GPU timings)");
            queryPool = out.get(0);
        }
        RtDebugLabels.name(ctx, VK10.VK_OBJECT_TYPE_QUERY_POOL, queryPool, "RT GPU timings");
    }

    Frame begin(VkCommandBuffer cmd) {
        collectReady();
        for (int attempt = 0; attempt < SLOT_COUNT; attempt++) {
            int slot = cursor++ % SLOT_COUNT;
            if (pending[slot]) {
                continue;
            }
            int first = slot * QUERY_COUNT;
            VK12.vkCmdResetQueryPool(cmd, queryPool, first, QUERY_COUNT);
            VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, queryPool, first);
            pending[slot] = true;
            return new Frame(cmd, first);
        }
        return null;
    }

    private void collectReady() {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            LongBuffer result = stack.mallocLong(QUERY_COUNT * 2);
            for (int slot = 0; slot < SLOT_COUNT; slot++) {
                if (!pending[slot]) {
                    continue;
                }
                result.clear();
                int status = VK10.vkGetQueryPoolResults(ctx.vk(), queryPool, slot * QUERY_COUNT,
                        QUERY_COUNT, result, 2L * Long.BYTES,
                        VK10.VK_QUERY_RESULT_64_BIT | VK10.VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
                if (status == VK10.VK_NOT_READY) {
                    continue;
                }
                RtContext.check(status, "vkGetQueryPoolResults(RT GPU timings)");
                boolean available = true;
                for (int query = 0; query < QUERY_COUNT; query++) {
                    available &= result.get(query * 2 + 1) != 0L;
                }
                if (!available) {
                    continue;
                }
                double nanosPerTick = ctx.timestampPeriodNanos();
                for (int stage = 0; stage < STAGES.length; stage++) {
                    long ticks = result.get((stage + 1) * 2) - result.get(stage * 2);
                    accumulatedMs[stage] += ticks * nanosPerTick / 1_000_000.0;
                }
                pending[slot] = false;
                samples++;
                if (samples == LOG_EVERY) {
                    logAverages();
                }
            }
        }
    }

    private void logAverages() {
        StringBuilder line = new StringBuilder("RT GPU avg over ").append(samples).append(" frames:");
        double total = 0.0;
        for (int i = 0; i < STAGES.length; i++) {
            double average = accumulatedMs[i] / samples;
            total += average;
            line.append(' ').append(STAGES[i]).append('=')
                    .append(String.format(Locale.ROOT, "%.3fms", average));
            accumulatedMs[i] = 0.0;
        }
        line.append(" total=").append(String.format(Locale.ROOT, "%.3fms", total));
        samples = 0;
        CausticaMod.LOGGER.info(line.toString());
    }

    void destroy() {
        VK10.vkDestroyQueryPool(ctx.vk(), queryPool, null);
    }

    final class Frame {
        private final VkCommandBuffer cmd;
        private final int firstQuery;
        private int nextStage;

        private Frame(VkCommandBuffer cmd, int firstQuery) {
            this.cmd = cmd;
            this.firstQuery = firstQuery;
        }

        void finish(String stage) {
            if (!STAGES[nextStage].equals(stage)) {
                throw new IllegalArgumentException("Expected GPU stage " + STAGES[nextStage] + ", got " + stage);
            }
            VK10.vkCmdWriteTimestamp(cmd, VK10.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                    queryPool, firstQuery + ++nextStage);
        }
    }
}
