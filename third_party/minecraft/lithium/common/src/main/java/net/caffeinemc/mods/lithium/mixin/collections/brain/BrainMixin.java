package net.caffeinemc.mods.lithium.mixin.collections.brain;

import com.mojang.datafixers.util.Pair;
import it.unimi.dsi.fastutil.objects.Object2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.objects.Reference2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.objects.Reference2ReferenceLinkedOpenHashMap;
import net.minecraft.world.entity.ai.Brain;
import net.minecraft.world.entity.ai.memory.ExpirableValue;
import net.minecraft.world.entity.ai.memory.MemoryModuleType;
import net.minecraft.world.entity.ai.memory.MemoryStatus;
import net.minecraft.world.entity.schedule.Activity;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.Map;
import java.util.Optional;
import java.util.Set;

@Mixin(Brain.class)
public class BrainMixin {

    @Mutable
    @Shadow
    @Final
    private Map<MemoryModuleType<?>, Optional<? extends ExpirableValue<?>>> memories;

    @Mutable
    @Shadow
    @Final
    private Map<?, ?> sensors;

    @Shadow
    @Final
    @Mutable
    private Map<Activity, Set<Pair<MemoryModuleType<?>, MemoryStatus>>> activityRequirements;

    @Inject(
            method = "<init>(Ljava/util/Collection;Ljava/util/Collection;Ljava/util/List;Lnet/minecraft/world/entity/ai/memory/MemoryMap;Lnet/minecraft/util/RandomSource;)V",
            at = @At("RETURN")
    )
    private void reinitializeBrainCollections(CallbackInfo ci) {
        this.memories = new Reference2ObjectOpenHashMap<>(this.memories);
        this.sensors = new Reference2ReferenceLinkedOpenHashMap<>(this.sensors);
        this.activityRequirements = new Object2ObjectOpenHashMap<>(this.activityRequirements);
        //TODO why is this optimization only replacing 3 of 5 collections?
    }
}
