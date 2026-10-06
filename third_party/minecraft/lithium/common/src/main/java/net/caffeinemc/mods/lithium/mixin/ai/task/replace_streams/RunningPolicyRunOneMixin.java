package net.caffeinemc.mods.lithium.mixin.ai.task.replace_streams;

import net.caffeinemc.mods.lithium.common.ai.brain.RunningPolicyNoStream;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.ai.behavior.Behavior;
import net.minecraft.world.entity.ai.behavior.BehaviorControl;
import net.minecraft.world.entity.ai.behavior.ShufflingList;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(targets = "net.minecraft.world.entity.ai.behavior.GateBehavior$RunningPolicy$1")
public class RunningPolicyRunOneMixin implements RunningPolicyNoStream {

    @Override
    public <E extends LivingEntity> void lithium$apply(ShufflingList<BehaviorControl<? super E>> behaviors, ServerLevel serverLevel, E livingEntity, long l) {
        for (BehaviorControl<? super E> behaviorControl : behaviors) {
            if (behaviorControl.getStatus() == Behavior.Status.STOPPED) {
                if (behaviorControl.tryStart(serverLevel, livingEntity, l)) {
                    return;
                }
            }
        }
    }
}
