package net.caffeinemc.mods.lithium.mixin.ai.useless_behaviors.nitwit_job_search;

import net.caffeinemc.mods.lithium.common.ai.useless_behaviors.LithiumEmptyBehavior;
import net.minecraft.core.BlockPos;
import net.minecraft.core.GlobalPos;
import net.minecraft.core.Holder;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.PathfinderMob;
import net.minecraft.world.entity.ai.behavior.AcquirePoi;
import net.minecraft.world.entity.ai.behavior.BehaviorControl;
import net.minecraft.world.entity.ai.behavior.VillagerGoalPackages;
import net.minecraft.world.entity.ai.memory.MemoryModuleType;
import net.minecraft.world.entity.ai.village.poi.PoiType;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

import java.util.Optional;
import java.util.function.BiPredicate;
import java.util.function.Predicate;

/**
 * Nitwits still run AcquirePoi task for job sites even though it will always fail.
 * This removes that goal if the acquirable job site is PoiType.NONE.
 * Note: Villagers refresh their brains when modified via commands so this should work even when data modified.
 * @author jcw780
 *
 * Special thanks to Autumn for running the tests that showed this was an issue.
 */
@Mixin(VillagerGoalPackages.class)
public abstract class VillagerGoalPackagesMixin {
    @Redirect(method = "getCorePackage", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/ai/behavior/AcquirePoi;create(Ljava/util/function/Predicate;Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;Lnet/minecraft/world/entity/ai/memory/MemoryModuleType;ZLjava/util/Optional;Ljava/util/function/BiPredicate;)Lnet/minecraft/world/entity/ai/behavior/BehaviorControl;"))
    private static BehaviorControl<PathfinderMob> returnNullIfAcquirePoiIsUseless(Predicate<Holder<PoiType>> predicate,
                                                                                  MemoryModuleType<GlobalPos> memoryModuleType,
                                                                                  MemoryModuleType<GlobalPos> memoryModuleType2,
                                                                                  boolean bl, @SuppressWarnings("OptionalUsedAsFieldOrParameterType") Optional<Byte> optional,
                                                                                  BiPredicate<ServerLevel, BlockPos> biPredicate) {
        if (predicate == PoiType.NONE) {
            //noinspection unchecked
            return LithiumEmptyBehavior.EMPTY_BEHAVIOR_SENTINEL;
        }
        return AcquirePoi.create(predicate, memoryModuleType, memoryModuleType2, bl, optional, biPredicate);
    }
}
