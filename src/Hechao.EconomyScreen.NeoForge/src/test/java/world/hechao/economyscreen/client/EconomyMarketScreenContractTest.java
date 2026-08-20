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
        assertTrue(source.contains("SORT_SLOT"));
        assertTrue(source.contains("sortButtonLabel"));
        assertTrue(source.contains("单位价:"));
    }

    @Test
    void listingKeepsNativeItemInteractionAndUsesValidatedPriceCommand()
            throws IOException {
        var source = read("EconomyMarketListingScreen.java");

        assertTrue(source.contains("extends ContainerScreen"));
        assertTrue(source.contains("MarketPriceInput.parse"));
        assertTrue(source.contains("hechaoeconomy:ah list "));
        assertTrue(source.contains("handleInventoryMouseClick("));
        assertTrue(source.contains("EconomyMarketListingLayout.calculate("));
        assertTrue(source.contains("layout.itemModule()"));
        assertTrue(source.contains("layout.priceModule()"));
        assertTrue(source.contains("layout.inventoryModule()"));
        assertTrue(source.contains("layout.guideModule()"));
        assertTrue(source.contains("renderModule("));
        assertTrue(source.contains("visibleSlot(slot)"));
        assertTrue(source.contains("上架说明"));
        assertTrue(source.contains("compactStatus("));
        assertTrue(source.contains("slotClicked(Slot"));

        var layout = read("EconomyMarketListingLayout.java");
        assertTrue(layout.contains("IMAGE_WIDTH = 278"));
        assertTrue(layout.contains("TOP_MODULE_TOP = 26"));
        assertTrue(layout.contains("priceField = new Rect(170, 46, 50, 18)"));
        assertTrue(layout.contains("confirmButton = new Rect(224, 46, 36, 18)"));
    }

    private static String read(String fileName) throws IOException {
        return Files.readString(SOURCE_ROOT.resolve(fileName));
    }
}
