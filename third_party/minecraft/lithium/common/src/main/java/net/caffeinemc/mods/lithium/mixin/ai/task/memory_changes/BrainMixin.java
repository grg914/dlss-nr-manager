package net.caffeinemc.mods.lithium.mixin.ai.task.memory_changes;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import net.caffeinemc.mods.lithium.common.ai.brain.memories.MemoryModificationCounter;
import net.minecraft.world.entity.ai.Brain;
import net.minecraft.world.entity.ai.memory.MemoryModuleType;
import net.minecraft.world.entity.ai.memory.MemorySlot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.function.Consumer;

@Mixin(Brain.class)
public class BrainMixin implements MemoryModificationCounter {

    //Tracking changes to the memory value, possibly including false positives (not calling equals()).
    // This does not track changes to the time to live
    @Unique
    private long memoryModCount = 1;

    @Inject(
            method = { "registerMemory", "clearMemories", }, at = @At("RETURN")
    )
    private void increaseMemoryModCount(CallbackInfo ci) {
        this.lithium$onMemoryModified();
    }

    @Inject(
            method = "eraseMemory", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/ai/memory/MemorySlot;clear()V")
    )
    private void increaseMemoryModCount(MemoryModuleType<?> type, CallbackInfo ci, @Local(name = "slot") MemorySlot<?> slot) {
        if (slot.hasValue()) {
            this.lithium$onMemoryModified();
        }
    }

    @ModifyArg(method = "forgetOutdatedMemories", at = @At(value = "INVOKE", target = "Ljava/util/Collection;forEach(Ljava/util/function/Consumer;)V"))
    private Consumer<MemorySlot<?>> tickExpiringSlotsTrackingChanges(Consumer<MemorySlot<?>> memorySlotTicker) {
        return slot -> {
            boolean b = slot.canExpire(); // canExpire implies hasValue
            if (b) {
                slot.tick();
                if (!slot.hasValue()) {
                    // Expired memory was deleted during tick
                    this.lithium$onMemoryModified();
                }
            }
        };
    }

    @WrapOperation(
            method = {
                    "setMemoryInternal(Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;Ljava/lang/Object;J)V",
                    "setMemoryInternal(Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;Ljava/lang/Object;)V"
            },
            at = @At(
                    value = "INVOKE",
                    target = "Lnet/minecraft/world/entity/ai/memory/MemorySlot;clear()V"
            )
    )
    private void clearTrackingChanges(MemorySlot<?> instance, Operation<Void> original) {
        if (instance.hasValue()) {
            this.lithium$onMemoryModified();
        }
        original.call(instance);
    }

    @WrapOperation(
            method = "setMemoryInternal(Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;Ljava/lang/Object;J)V",

            at = @At(
                    value = "INVOKE",
                    target = "Lnet/minecraft/world/entity/ai/memory/MemorySlot;set(Ljava/lang/Object;J)V"
            )
    )
    private <T> void setTrackingChanges(MemorySlot<?> instance, T value, long timeToLive, Operation<Void> original) {
        if (instance.hasValue() == (value == null)) {
            this.lithium$onMemoryModified();
        }
        original.call(instance, value, timeToLive);
    }

    @WrapOperation(
            method = "setMemoryInternal(Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;Ljava/lang/Object;)V",
            at = @At(
                    value = "INVOKE",
                    target = "Lnet/minecraft/world/entity/ai/memory/MemorySlot;set(Ljava/lang/Object;)V"
            )
    )
    private <T> void setTrackingChanges(MemorySlot<?> instance, T value, Operation<Void> original) {
        if (instance.hasValue() == (value == null)) {
            this.lithium$onMemoryModified();
        }
        original.call(instance, value);
    }

    @Override
    public long lithium$getMemoryValueModCount() {
        return this.memoryModCount;
    }

    @Override
    public void lithium$onMemoryModified() {
        this.memoryModCount++;
    }

}
