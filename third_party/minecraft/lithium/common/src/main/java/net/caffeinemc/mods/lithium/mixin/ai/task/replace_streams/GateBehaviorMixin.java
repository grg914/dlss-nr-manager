package net.caffeinemc.mods.lithium.mixin.ai.task.replace_streams;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.caffeinemc.mods.lithium.common.ai.WeightedListIterable;
import net.caffeinemc.mods.lithium.common.ai.brain.RunningPolicyNoStream;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.ai.Brain;
import net.minecraft.world.entity.ai.behavior.Behavior;
import net.minecraft.world.entity.ai.behavior.BehaviorControl;
import net.minecraft.world.entity.ai.behavior.GateBehavior;
import net.minecraft.world.entity.ai.behavior.ShufflingList;
import net.minecraft.world.entity.ai.memory.MemoryModuleType;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Overwrite;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

import java.util.Set;
import java.util.stream.Stream;

@Mixin(GateBehavior.class)
public abstract class GateBehaviorMixin<E extends LivingEntity> implements BehaviorControl<E> {
    @Shadow
    @Final
    private ShufflingList<BehaviorControl<? super E>> behaviors;

    @Shadow
    @Final
    private Set<MemoryModuleType<?>> exitErasedMemories;

    @Shadow
    private Behavior.Status status;

    /**
     * @reason Replace stream code with traditional iteration
     * @author JellySquid, IMS, 2No2Name
     */
    @Override
    @Overwrite
    public final void tickOrStop(ServerLevel world, E entity, long time) {
        boolean hasOneTaskRunning = false;
        for (BehaviorControl<? super E> task : WeightedListIterable.cast(this.behaviors)) {
            if (task.getStatus() == Behavior.Status.RUNNING) {
                task.tickOrStop(world, entity, time);
                hasOneTaskRunning |= task.getStatus() == Behavior.Status.RUNNING;
            }
        }

        if (!hasOneTaskRunning) {
            this.doStop(world, entity, time);
        }
    }

    /**
     * @reason Replace stream code with traditional iteration
     * @author JellySquid
     */
    @Override
    @Overwrite
    public final void doStop(ServerLevel world, E entity, long time) {
        this.status = Behavior.Status.STOPPED;
        for (BehaviorControl<? super E> task : WeightedListIterable.cast(this.behaviors)) {
            if (task.getStatus() == Behavior.Status.RUNNING) {
                task.doStop(world, entity, time);
            }
        }

        Brain<?> brain = entity.getBrain();

        for (MemoryModuleType<?> module : this.exitErasedMemories) {
            brain.eraseMemory(module);
        }
    }


    @WrapOperation(
            method = "tryStart", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/ai/behavior/GateBehavior$RunningPolicy;apply(Ljava/util/stream/Stream;Lnet/minecraft/server/level/ServerLevel;Lnet/minecraft/world/entity/LivingEntity;J)V")
    )
    public final void tryStart(GateBehavior.RunningPolicy runningPolicy, Stream<BehaviorControl<? super E>> behaviorControlStream, ServerLevel serverLevel, E e, long l, Operation<Void> original) {
        //noinspection ConstantValue
        if ((Object) runningPolicy instanceof RunningPolicyNoStream runningPolicyNoStream) {
            runningPolicyNoStream.lithium$apply(this.behaviors, serverLevel, e, l);
        } else {
            original.call(runningPolicy, behaviorControlStream, serverLevel, e, l);
        }
    }
}