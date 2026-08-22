package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class RtpCommandPlanTest {
    @Test
    void capsRandomTeleportAtFiveThousandBlocks() {
        var plan = RtpCommandPlan.create(0, 0, 59_999_968).orElseThrow();

        assertEquals(5_000, plan.maximumRange());
        assertEquals(
                "minecraft:spreadplayers 0.00 0.00 0 5000 false @s",
                plan.command());
    }

    @Test
    void keepsTeleportInsideSmallWorldBorders() {
        var plan = RtpCommandPlan.create(125.5, -83.25, 600).orElseThrow();

        assertEquals(268, plan.maximumRange());
        assertEquals(
                "minecraft:spreadplayers 125.50 -83.25 0 268 false @s",
                plan.command());
    }

    @Test
    void rejectsUnsafeOrInvalidWorldBorders() {
        assertTrue(RtpCommandPlan.create(0, 0, 190).isEmpty());
        assertTrue(RtpCommandPlan.create(Double.NaN, 0, 1_000).isEmpty());
        assertTrue(RtpCommandPlan.create(0, 0, Double.POSITIVE_INFINITY).isEmpty());
    }
}
