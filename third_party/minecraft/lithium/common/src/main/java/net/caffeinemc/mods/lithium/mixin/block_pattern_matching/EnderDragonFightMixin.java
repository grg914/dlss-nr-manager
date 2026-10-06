package net.caffeinemc.mods.lithium.mixin.block_pattern_matching;

import net.caffeinemc.mods.lithium.common.world.block_pattern_matching.BlockPatternExtended;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.EntityReference;
import net.minecraft.world.entity.boss.enderdragon.EndCrystal;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.pattern.BlockPattern;
import net.minecraft.world.level.dimension.end.DragonRespawnStage;
import net.minecraft.world.level.dimension.end.EnderDragonFight;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.List;
import java.util.Optional;
import java.util.UUID;

@Mixin(EnderDragonFight.class)
public class EnderDragonFightMixin {

    @Shadow
    @Final
    private BlockPattern exitPortalPattern;

    @SuppressWarnings("OptionalUsedAsFieldOrParameterType")
    @Inject(
            method = "<init>", at = @At("RETURN")
    )
    private void setPatternToDragonPattern(boolean needsStateScanning, boolean dragonKilled, boolean previouslyKilled, Optional<DragonRespawnStage> respawnStage, int respawnTime, Optional<UUID> dragonUUID, Optional<BlockPos> exitPortalLocation, List<Integer> gateways, List<EntityReference<EndCrystal>> respawnCrystals, CallbackInfo ci) {
        //Small todo: Find a way to not hardcode this, as this breaks mod compatibility when modifying the exit portal pattern
        ((BlockPatternExtended) this.exitPortalPattern).lithium$setRequiredBlock(Blocks.BEDROCK, 41);
    }
}
