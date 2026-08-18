package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertFalse;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class ArclightCompatibilityContractTest {
    @Test
    void catalogRenderingDoesNotCallPaperOnlyMaterialTranslationKey() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economy",
                "gui",
                "ShopMenu.java"));

        assertFalse(source.contains(".translationKey("));
    }
}
