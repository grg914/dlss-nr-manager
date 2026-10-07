package net.caffeinemc.mods.lithium.fabric.mixin.util.initialization;

import net.caffeinemc.mods.lithium.common.initialization.BlockInfoInitializer;
import net.minecraft.core.HolderLookup;
import net.minecraft.world.flag.FeatureFlagSet;
import net.minecraft.world.level.block.entity.FuelValues;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Hook into the initialization of fuel values, because those are always updated after block tags are modified,
 * and we must among others, update our cache path node information which is also based on block tags.
 */
@Mixin(FuelValues.class)
public class FuelValuesMixin {

    @Inject(
            method = "vanillaBurnTimes(Lnet/minecraft/core/HolderLookup$Provider;Lnet/minecraft/world/flag/FeatureFlagSet;I)Lnet/minecraft/world/level/block/entity/FuelValues;",
            at = @At(value = "RETURN")
    )
    private static void initializeBlockInfo(HolderLookup.Provider provider, FeatureFlagSet featureFlagSet, int i, CallbackInfoReturnable<FuelValues> cir) {
        BlockInfoInitializer.initializeBlockInfo();
    }
}
