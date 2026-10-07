package net.caffeinemc.mods.lithium.mixin.entity.framed_maps;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import net.minecraft.world.entity.decoration.ItemFrame;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.saveddata.SavedData;
import net.minecraft.world.level.saveddata.maps.MapItemSavedData;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

import java.util.List;
import java.util.Map;
import java.util.Objects;

@Mixin(MapItemSavedData.class)
public abstract class MapItemSavedDataMixin extends SavedData {

    @Shadow
    @Final
    private Map<Player, MapItemSavedData.HoldingPlayer> carriedByPlayers;

    @WrapOperation(
            method = "tickCarriedBy(Lnet/minecraft/world/entity/player/Player;Lnet/minecraft/world/item/ItemStack;Lnet/minecraft/world/entity/decoration/ItemFrame;)V",
            at = @At(value = "INVOKE", target = "Ljava/util/List;size()I")
    )
    private int sizeOrOne(List<MapItemSavedData.HoldingPlayer> instance, Operation<Integer> original, @Local(argsOnly = true) ItemStack mapStack, @Local(argsOnly = true) ItemFrame placedInFrame) {
        return placedInFrame != null ? 1 : original.call(instance);
    }

    @WrapOperation(
            method = "tickCarriedBy(Lnet/minecraft/world/entity/player/Player;Lnet/minecraft/world/item/ItemStack;Lnet/minecraft/world/entity/decoration/ItemFrame;)V",
            at = @At(value = "INVOKE", target = "Ljava/util/List;get(I)Ljava/lang/Object;")
    )
    private <E> E getOrGetThePlayer(List<E> instance, int i, Operation<E> original, @Local(argsOnly = true) Player player, @Local(argsOnly = true) ItemStack mapStack, @Local(argsOnly = true) ItemFrame placedInFrame) {
        //noinspection unchecked
        return placedInFrame != null ? (E) Objects.requireNonNull(this.carriedByPlayers.get(player)) : original.call(instance, i);
    }
}
