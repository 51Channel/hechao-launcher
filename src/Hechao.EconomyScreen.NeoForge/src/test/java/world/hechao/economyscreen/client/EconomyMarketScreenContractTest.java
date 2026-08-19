package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class EconomyMarketScreenContractTest {
    private static final Path SOURCE_ROOT = Path.of(
            "src",
            "main",
            "java",
            "world",
            "hechao",
            "economyscreen",
            "client");

    @Test
    void marketProvidesAllPlayerWorkflowsAndDebouncedSearch() throws IOException {
        var source = read("EconomyMarketScreen.java");

        assertTrue(source.contains("\"玩家市场\""));
        assertTrue(source.contains("\"上架物品\""));
        assertTrue(source.contains("\"我的挂单\""));
        assertTrue(source.contains("\"待领取\""));
        assertTrue(source.contains("SEARCH_DELAY_TICKS"));
        assertTrue(source.contains("marketCommand()"));
        assertTrue(source.contains("搜索商品或卖家"));
    }

    @Test
    void listingKeepsNativeItemInteractionAndUsesValidatedPriceCommand()
            throws IOException {
        var source = read("EconomyMarketListingScreen.java");

        assertTrue(source.contains("extends ContainerScreen"));
        assertTrue(source.contains("MarketPriceInput.parse"));
        assertTrue(source.contains("hechaoeconomy:ah list "));
        assertTrue(source.contains("handleInventoryMouseClick("));
        assertTrue(source.contains("CUSTOM_IMAGE_WIDTH = 278"));
        assertTrue(source.contains("visibleSlot(slot)"));
        assertTrue(source.contains("上架规则"));
        assertTrue(source.contains("slotClicked(Slot"));
    }

    private static String read(String fileName) throws IOException {
        return Files.readString(SOURCE_ROOT.resolve(fileName));
    }
}
