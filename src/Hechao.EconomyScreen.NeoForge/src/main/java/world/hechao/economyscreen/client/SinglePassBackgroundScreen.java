package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

abstract class SinglePassBackgroundScreen extends Screen {
    private boolean renderingWidgets;

    SinglePassBackgroundScreen(Component title) {
        super(title);
    }

    @Override
    public final void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        renderBackground(graphics, mouseX, mouseY, partialTick);
        IndustrialUiTheme.renderBackdrop(graphics, width, height);
        renderContent(graphics, mouseX, mouseY, partialTick);

        renderingWidgets = true;
        try {
            super.render(graphics, mouseX, mouseY, partialTick);
        } finally {
            renderingWidgets = false;
        }

        renderOverlay(graphics, mouseX, mouseY, partialTick);
    }

    @Override
    public final void renderBackground(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        if (!renderingWidgets) {
            super.renderBackground(graphics, mouseX, mouseY, partialTick);
        }
    }

    protected abstract void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick);

    protected void renderOverlay(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
    }
}
