package net.caffeinemc.mods.lithium.mixin.world.block_entity_ticking.sleeping.sculk_sensor_shrieker;

import net.caffeinemc.mods.lithium.common.block.entity.SleepingBlockEntity;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.SculkSensorBlock;
import net.minecraft.world.level.block.entity.SculkSensorBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.vibrations.VibrationSystem;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(SculkSensorBlock.class)
public abstract class SculkSensorBlockMixin {
    @Inject(
            method = "lambda$getTicker$0",
            at = @At(value = "RETURN")
    )
    private static void checkSleep(Level level, BlockPos blockPos, BlockState blockState, SculkSensorBlockEntity sculkSensorBlockEntity, CallbackInfo ci) {
        final VibrationSystem.Data vibrationData = sculkSensorBlockEntity.getVibrationData();
        if (vibrationData.getCurrentVibration() == null &&
                vibrationData.getSelectionStrategy().chosenCandidate(Long.MAX_VALUE).isEmpty()) {
            ((SleepingBlockEntity) sculkSensorBlockEntity).lithium$startSleeping();
        }
    }
}
