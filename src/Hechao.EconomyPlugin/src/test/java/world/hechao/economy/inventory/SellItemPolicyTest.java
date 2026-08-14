package world.hechao.economy.inventory;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class SellItemPolicyTest {
    @Test
    void acceptsPlainVanillaStack() {
        var result = SellItemPolicy.evaluate(
                "minecraft", "iron_ingot", false, false, 32);

        assertTrue(result.allowed());
        assertEquals("minecraft:iron_ingot", result.itemId());
    }

    @Test
    void rejectsContainersEvenWithoutMetadata() {
        var result = SellItemPolicy.evaluate(
                "minecraft", "chest", false, false, 1);

        assertFalse(result.allowed());
    }

    @Test
    void rejectsAir() {
        var result = SellItemPolicy.evaluate(
                "minecraft", "air", false, true, 1);

        assertFalse(result.allowed());
    }

    @Test
    void rejectsModdedAndMetadataItems() {
        assertFalse(SellItemPolicy.evaluate(
                "create", "brass_ingot", false, false, 1).allowed());
        assertFalse(SellItemPolicy.evaluate(
                "minecraft", "diamond_sword", true, false, 1).allowed());
    }
}
