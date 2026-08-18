package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;

final class SinglePassBackgroundScreenTest {
    private static final Path SOURCE_ROOT = Path.of(
            "src",
            "main",
            "java",
            "world",
            "hechao",
            "economyscreen",
            "client");

    @Test
    void customScreensUseTheSinglePassRenderingPipeline() throws IOException {
        for (var fileName : List.of(
                "EconomyResultScreen.java",
                "EconomyCatalogScreen.java",
                "HechaoNavigationScreen.java")) {
            var source = readSource(fileName);
            assertTrue(source.contains("extends SinglePassBackgroundScreen"));
            assertTrue(source.contains("protected void renderContent("));
            assertFalse(source.contains("super.render("));
            assertFalse(source.contains("renderBackground(graphics"));
        }
    }

    @Test
    void renderingOrderCannotBeOverridden() throws IOException {
        var source = readSource("SinglePassBackgroundScreen.java");

        assertEquals(1, occurrences(source, "public final void render("));
        assertEquals(1, occurrences(source, "public final void renderBackground("));
        assertTrue(source.indexOf("renderBackground(graphics")
                < source.indexOf("renderContent(graphics"));
        assertTrue(source.indexOf("renderContent(graphics")
                < source.indexOf("super.render(graphics"));
        assertTrue(source.indexOf("super.render(graphics")
                < source.indexOf("renderOverlay(graphics"));
    }

    private static String readSource(String fileName) throws IOException {
        return Files.readString(SOURCE_ROOT.resolve(fileName));
    }

    private static int occurrences(String value, String token) {
        return (value.length() - value.replace(token, "").length()) / token.length();
    }
}
