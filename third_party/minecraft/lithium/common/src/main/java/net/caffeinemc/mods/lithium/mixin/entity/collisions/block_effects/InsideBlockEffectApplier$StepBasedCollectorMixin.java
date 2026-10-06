package net.caffeinemc.mods.lithium.mixin.entity.collisions.block_effects;

import com.llamalad7.mixinextras.injector.ModifyReceiver;
import com.llamalad7.mixinextras.injector.v2.WrapWithCondition;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.InsideBlockEffectApplier;
import net.minecraft.world.entity.InsideBlockEffectType;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.*;
import java.util.function.Consumer;
import java.util.function.Function;

@Mixin(InsideBlockEffectApplier.StepBasedCollector.class)
public class InsideBlockEffectApplier$StepBasedCollectorMixin {

    @Mutable
    @Shadow
    @Final
    private Set<InsideBlockEffectType> effectsInStep;

    @Mutable
    @Shadow
    @Final
    private Map<InsideBlockEffectType, List<Consumer<Entity>>> beforeEffectsInStep;

    @Mutable
    @Shadow
    @Final
    private Map<InsideBlockEffectType, List<Consumer<Entity>>> afterEffectsInStep;


    @Mutable
    @Shadow
    @Final
    private List<Consumer<Entity>> finalEffects;

    @Inject(
            method = "flushStep", at = @At("HEAD"), cancellable = true
    )
    private void trySkip(CallbackInfo ci) {
        if (this.effectsInStep == null || this.effectsInStep.isEmpty()) {
            if (this.beforeEffectsInStep == null || this.beforeEffectsInStep.isEmpty()) {
                if (this.afterEffectsInStep == null || this.afterEffectsInStep.isEmpty()) {
                    ci.cancel();
                    return;
                }
            }
        }
        if (this.finalEffects == null) {
            this.finalEffects = new ArrayList<>();
        }
    }
    //Mixins below to avoid allocations while not causing NullPointerException
    //Once the maps / lists are initialized, they are not removed. However, most entities will never initialize these
    // in their lifetime.

    @WrapOperation(
            method = "<init>", at = @At(value = "INVOKE", target = "Lnet/minecraft/util/Util;makeEnumMap(Ljava/lang/Class;Ljava/util/function/Function;)Ljava/util/Map;")
    )
    private <K, V> Map<K, V> avoidAlloc(Class<K> class_, Function<K, V> function, Operation<Map<K, V>> original) {
        return null;
    }

    @WrapOperation(
            method = "<init>", at = @At(value = "INVOKE", target = "Ljava/util/EnumSet;noneOf(Ljava/lang/Class;)Ljava/util/EnumSet;")
    )
    private <E extends Enum<E>> EnumSet<E> avoidAlloc(Class<E> elementType, Operation<EnumSet<E>> original) {
        return null;
    }

    @WrapOperation(
            method = "<init>", at = @At(value = "NEW", target = "()Ljava/util/ArrayList;")
    )
    private <E> ArrayList<E> avoidAlloc(Operation<ArrayList<E>> original) {
        return null;
    }

    @ModifyReceiver(
            method = "applyAndClear", at = @At(value = "INVOKE", target = "Ljava/util/List;iterator()Ljava/util/Iterator;")
    )
    private <E> List<E> replaceNull(List<E> instance) {
        return instance == null ? Collections.emptyList() : instance;
    }

    @WrapWithCondition(
            method = "applyAndClear", at = @At(value = "INVOKE", target = "Ljava/util/List;clear()V")
    )
    private <E> boolean isNotNull(List<E> instance) {
        return instance != null;
    }

    @WrapWithCondition(
            method = "flushStep", at = @At(value = "INVOKE", target = "Ljava/util/List;clear()V")
    )
    private <E> boolean isNotNull2(List<E> instance) {
        return instance != null;
    }

    @WrapOperation(
            method = "flushStep", at = @At(value = "INVOKE", target = "Ljava/util/Map;get(Ljava/lang/Object;)Ljava/lang/Object;")
    )
    private <K, V> V getOrNull(Map<K, V> instance, Object o, Operation<V> original) {
        if (instance == null) {
            return null;
        }
        return original.call(instance, o);
    }


    @WrapOperation(
            method = "flushStep", at = @At(value = "INVOKE", target = "Ljava/util/Set;remove(Ljava/lang/Object;)Z")
    )
    private <K> boolean removeOrFalse(Set<K> instance, Object o, Operation<Boolean> original) {
        if (instance == null) {
            return false;
        }
        return original.call(instance, o);
    }

    @WrapOperation(
            method = "flushStep", at = @At(value = "INVOKE", target = "Ljava/util/List;addAll(Ljava/util/Collection;)Z")
    )
    private <E> boolean addAllNonNull(List<E> instance, Collection<? extends E> es, Operation<Boolean> original) {
        if (es != null && !es.isEmpty()) {
            return original.call(instance, es);
        } else {
            return false;
        }
    }


    @Inject(
            method = "apply", at = @At("HEAD")
    )
    private void init(InsideBlockEffectType insideBlockEffectType, CallbackInfo ci) {
        if (this.effectsInStep == null) {
            this.effectsInStep = EnumSet.noneOf(InsideBlockEffectType.class);
        }
    }

    @WrapOperation(
            method = "runBefore", at = @At(value = "INVOKE", target = "Ljava/util/Map;get(Ljava/lang/Object;)Ljava/lang/Object;")
    )
    private <K, V> V initBeforeAndGet(Map<K, V> map, K key, Operation<V> original) {
        if (map == null) {
            this.beforeEffectsInStep = new EnumMap<>(InsideBlockEffectType.class);
            //noinspection unchecked
            map = (Map<K, V>) this.beforeEffectsInStep;
        }
        //noinspection unchecked
        return map.computeIfAbsent(key, k -> (V) new ArrayList<>());
    }

    @WrapOperation(
            method = "runAfter", at = @At(value = "INVOKE", target = "Ljava/util/Map;get(Ljava/lang/Object;)Ljava/lang/Object;")
    )
    private <K, V> V initAfterAndGet(Map<K, V> map, K key, Operation<V> original) {
        if (map == null) {
            this.afterEffectsInStep = new EnumMap<>(InsideBlockEffectType.class);
            //noinspection unchecked
            map = (Map<K, V>) this.afterEffectsInStep;
        }
        //noinspection unchecked
        return map.computeIfAbsent(key, k -> (V) new ArrayList<>());
    }
}
