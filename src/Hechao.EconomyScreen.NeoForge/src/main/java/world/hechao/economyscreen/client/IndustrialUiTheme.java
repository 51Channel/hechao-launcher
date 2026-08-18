package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.resources.ResourceLocation;

final class IndustrialUiTheme {
    static final int HEADER_HEIGHT = 34;

    private static final int BACKDROP_WIDTH = 1024;
    private static final int BACKDROP_HEIGHT = 576;
    private static final int EMBLEM_TEXTURE_SIZE = 128;
    private static final ResourceLocation BACKDROP = ResourceLocation.fromNamespaceAndPath(
            "hechao_economy_screen",
            "textures/gui/industrial_backdrop.png");
    private static final ResourceLocation EMBLEM = ResourceLocation.fromNamespaceAndPath(
            "hechao_economy_screen",
            "textures/gui/expedition_emblem.png");

    private IndustrialUiTheme() {
    }

    static void renderBackdrop(
            GuiGraphics graphics,
            int screenWidth,
            int screenHeight) {
        graphics.fill(0, 0, screenWidth, screenHeight, 0xFF111416);
        var crop = BackdropCover.calculate(
                screenWidth,
                screenHeight,
                BACKDROP_WIDTH,
                BACKDROP_HEIGHT);
        graphics.blit(
                BACKDROP,
                0,
                0,
                screenWidth,
                screenHeight,
                crop.sourceX(),
                crop.sourceY(),
                crop.sourceWidth(),
                crop.sourceHeight(),
                BACKDROP_WIDTH,
                BACKDROP_HEIGHT);
        graphics.fill(0, 0, screenWidth, screenHeight, 0x19000000);
    }

    static void renderPanel(
            GuiGraphics graphics,
            int left,
            int top,
            int width,
            int height) {
        graphics.fill(left + 3, top + 3, left + width + 3, top + height + 3, 0x8A000000);
        graphics.fill(left, top, left + width, top + height, 0xF21A1E20);
        graphics.renderOutline(left, top, width, height, 0xFF9E793E);
        graphics.renderOutline(left + 2, top + 2, width - 4, height - 4, 0xFF3E474A);
        graphics.fill(
                left + 3,
                top + 3,
                left + width - 3,
                top + HEADER_HEIGHT,
                0xF02B3032);
        graphics.fill(
                left + 3,
                top + HEADER_HEIGHT - 1,
                left + width - 3,
                top + HEADER_HEIGHT + 1,
                0xFFD1A64D);
        renderRivet(graphics, left + 5, top + 5);
        renderRivet(graphics, left + width - 7, top + 5);
        renderRivet(graphics, left + 5, top + height - 7);
        renderRivet(graphics, left + width - 7, top + height - 7);
    }

    static void renderCard(
            GuiGraphics graphics,
            int left,
            int top,
            int width,
            int height,
            boolean hovered) {
        int body = hovered ? 0xF0384243 : 0xE624292C;
        int border = hovered ? 0xFFE2B95F : 0xFF566164;
        graphics.fill(left + 2, top + 2, left + width + 2, top + height + 2, 0x65000000);
        graphics.fill(left, top, left + width, top + height, body);
        graphics.renderOutline(left, top, width, height, border);
        graphics.fill(left + 1, top + 1, left + 4, top + height - 1,
                hovered ? 0xFF63A9A2 : 0xFF3E7C79);
        graphics.fill(left + 6, top + 6, left + 27, top + height - 6, 0xB8121618);
        graphics.renderOutline(left + 6, top + 6, 21, height - 12, 0xFF434D50);
    }

    static void renderDivider(
            GuiGraphics graphics,
            int left,
            int right,
            int y) {
        graphics.fill(left, y, right, y + 1, 0xFF424B4E);
        graphics.fill(left, y + 1, right, y + 2, 0x66000000);
    }

    static void renderStatusRail(
            GuiGraphics graphics,
            int left,
            int top,
            int bottom,
            int color) {
        graphics.fill(left, top, left + 3, bottom, 0xA9000000);
        graphics.fill(left, top, left + 2, bottom, color);
    }

    static void renderEmblem(
            GuiGraphics graphics,
            int left,
            int top,
            int size) {
        graphics.blit(
                EMBLEM,
                left,
                top,
                0.0F,
                0.0F,
                size,
                size,
                EMBLEM_TEXTURE_SIZE,
                EMBLEM_TEXTURE_SIZE);
    }

    private static void renderRivet(GuiGraphics graphics, int x, int y) {
        graphics.fill(x, y, x + 2, y + 2, 0xFFB78C45);
        graphics.fill(x + 1, y + 1, x + 2, y + 2, 0xFF594421);
    }
}
