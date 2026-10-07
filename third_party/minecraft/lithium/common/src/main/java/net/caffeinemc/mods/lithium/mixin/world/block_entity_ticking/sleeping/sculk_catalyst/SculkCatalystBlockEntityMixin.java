package net.caffeinemc.mods.lithium.mixin.world.block_entity_ticking.sleeping.sculk_catalyst;

import net.caffeinemc.mods.lithium.common.block.entity.SleepingBlockEntity;
import net.caffeinemc.mods.lithium.common.block.entity.sleeping_sculk.GameEventListenerWithCallback;
import net.caffeinemc.mods.lithium.mixin.world.block_entity_ticking.sleeping.WrappedBlockEntityTickInvokerAccessor;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.entity.SculkCatalystBlockEntity;
import net.minecraft.world.level.block.entity.TickingBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.storage.ValueInput;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(SculkCatalystBlockEntity.class)
public abstract class SculkCatalystBlockEntityMixin implements SleepingBlockEntity {
    private WrappedBlockEntityTickInvokerAccessor tickWrapper = null;
    private TickingBlockEntity sleepingTicker = null;

    @Shadow
    public abstract SculkCatalystBlockEntity.CatalystListener getListener();

    @Shadow
    @Final
    private SculkCatalystBlockEntity.CatalystListener catalystListener;

    @Override
    public WrappedBlockEntityTickInvokerAccessor lithium$getTickWrapper() {
        return tickWrapper;
    }

    @Override
    public void lithium$setTickWrapper(WrappedBlockEntityTickInvokerAccessor tickWrapper) {
        this.tickWrapper = tickWrapper;
        this.lithium$setSleepingTicker(null);
    }

    @Override
    public TickingBlockEntity lithium$getSleepingTicker() {
        return sleepingTicker;
    }

    @Override
    public void lithium$setSleepingTicker(TickingBlockEntity sleepingTicker) {
        this.sleepingTicker = sleepingTicker;
    }

    @Inject(method = "<init>", at = @At("RETURN"))
    private void addCatalystListenerCallback(BlockPos blockPos, BlockState blockState, CallbackInfo ci) {
        ((GameEventListenerWithCallback) this.catalystListener.getSculkSpreader()).lithium$setGameEventCallback(this::wakeUpNow);
    }

    @Inject(method = "serverTick", at = @At("RETURN"))
    private static void sleepIfNoCursors(Level level, BlockPos blockPos, BlockState blockState, SculkCatalystBlockEntity sculkCatalystBlockEntity, CallbackInfo ci) {
        if(sculkCatalystBlockEntity.getListener().getSculkSpreader().getCursors().isEmpty()) {
            ((SleepingBlockEntity) sculkCatalystBlockEntity).lithium$startSleeping();
        }
    }

    // This is to detect modification by commands
    @Inject(method = "loadAdditional", at=@At("RETURN"))
    private void wakeupIfLoadedWithData(ValueInput valueInput, CallbackInfo ci) {
        if (!this.getListener().getSculkSpreader().getCursors().isEmpty()) {
            this.wakeUpNow();
        }
    }
}
