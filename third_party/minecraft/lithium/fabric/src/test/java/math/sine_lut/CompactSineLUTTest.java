package math.sine_lut;


import net.caffeinemc.mods.lithium.common.util.math.CompactSineLUT;
import net.minecraft.util.Mth;
import org.junit.jupiter.api.Assertions;
import org.junit.jupiter.api.Test;

import java.util.Random;

public class CompactSineLUTTest {

    @Test
    public void testSin() {
        Random rand = new Random();
        for (int i = 0; i < 1_000_000; i++) {
            double d = rand.nextDouble() * 1000;
            float lithiumSin = CompactSineLUT.sin(d);
            float vanillaSin = Mth.sin(d);//TODO: Make sure to only run this without applying mixins
            Assertions.assertEquals(lithiumSin, vanillaSin);
        }
    }

    @Test
    public void testCos() {
        Random rand = new Random();
        for (int i = 0; i < 1_000_000; i++) {
            double d = rand.nextDouble() * 1000;
            float lithiumCos = CompactSineLUT.cos(d);
            float vanillaCos = Mth.cos(d); //TODO: Make sure to only run this without applying mixins
            Assertions.assertEquals(lithiumCos, vanillaCos);
        }
    }
}
