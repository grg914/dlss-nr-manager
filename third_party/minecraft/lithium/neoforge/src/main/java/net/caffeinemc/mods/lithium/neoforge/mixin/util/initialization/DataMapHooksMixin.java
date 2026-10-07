package net.caffeinemc.mods.lithium.neoforge.mixin.util.initialization;

import net.caffeinemc.mods.lithium.common.initialization.BlockInfoInitializer;
import net.minecraft.core.RegistryAccess;
import net.minecraft.world.flag.FeatureFlagSet;
import net.minecraft.world.level.block.entity.FuelValues;
import net.neoforged.neoforge.common.DataMapHooks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Hook into the initialization of fuel values, because those are always updated after block tags are modified,
 * and we must among others, update our cache path node information which is also based on block tags.
 */
@Mixin(DataMapHooks.class)
public class DataMapHooksMixin {

    @Inject(
            method = "populateFuelValues",
            at = @At(value = "RETURN")
    )
    private static void initializeCachedBlockData(RegistryAccess lookupProvider, FeatureFlagSet features, CallbackInfoReturnable<FuelValues> cir) {
        BlockInfoInitializer.initializeBlockInfo();
    }
}
