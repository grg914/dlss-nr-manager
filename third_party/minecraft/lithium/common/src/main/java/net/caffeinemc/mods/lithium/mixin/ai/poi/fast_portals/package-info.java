@MixinConfigOption(
        description = "Portal search uses the faster POI search and optimized loaded state caching",
        nonVanillaBehavior = "Lithium portal search skips unnecessarily accessing the block/chunk of some portal POIs. " +
                "This causes fewer chunks to be border loaded for a single game tick, which might affect entity cannons " +
                "that shoot through/into these chunks in that game tick."
)
package net.caffeinemc.mods.lithium.mixin.ai.poi.fast_portals;

import net.caffeinemc.gradle.MixinConfigOption;