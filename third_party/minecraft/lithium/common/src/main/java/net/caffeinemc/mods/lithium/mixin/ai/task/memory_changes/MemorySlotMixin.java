package net.caffeinemc.mods.lithium.mixin.ai.task.memory_changes;

import net.minecraft.world.entity.ai.memory.MemorySlot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MemorySlot.class)
public abstract class MemorySlotMixin {

    @Shadow
    public abstract void clear();

    @Shadow
    private long timeToLive;

    @Inject(
            method = "set(Ljava/lang/Object;J)V", at = @At("RETURN")
    )
    private <T> void ensureConsistency(T value, long timeToLive, CallbackInfo ci) {
        if (value == null && timeToLive != Long.MAX_VALUE) {
            this.timeToLive = Long.MAX_VALUE;
        }
    }
}
