@MixinConfigOption(
        description = "BlockEntity ticking caches whether the BlockEntity can exist in the BlockState at the same location.",
        nonVanillaBehavior = "This deviates from vanilla in the case of placing a hopper in a powered location, immediately updating the" +
                " cached BlockState (which is incorrect in vanilla). This might affect gameplay with other mods, as this" +
                " deviation only affects hoppers. In vanilla, hoppers never use this cached state information anyway.",
        depends = @MixinConfigDependency(dependencyPath = "mixin.world.block_entity_ticking")
)
package net.caffeinemc.mods.lithium.mixin.minimal_nonvanilla.world.block_entity_ticking.support_cache;

import net.caffeinemc.gradle.MixinConfigDependency;
import net.caffeinemc.gradle.MixinConfigOption;