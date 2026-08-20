package world.hechao.economyscreen.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.locale.Language;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.ItemStack;

final class IndustrialButton extends Button {
    private static final int ICON_GAP = 4;

    private final ItemStack icon;

    IndustrialButton(
            int x,
            int y,
            int width,
            int height,
            Component message,
            OnPress onPress) {
        this(x, y, width, height, message, ItemStack.EMPTY, onPress);
    }

    IndustrialButton(
            int x,
            int y,
            int width,
            int height,
            Component message,
            ItemStack icon,
            OnPress onPress) {
        super(x, y, width, height, message, onPress, DEFAULT_NARRATION);
        this.icon = icon.copy();
    }

    @Override
    public void renderWidget(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        boolean highlighted = active && isHoveredOrFocused();
        int body = !active
                ? 0xD51B1E20
                : highlighted ? 0xF03C4748 : 0xEE292F31;
        int border = !active
                ? 0xFF404749
                : highlighted ? 0xFFE3BA60 : 0xFF697376;
        int textColor = !active
                ? 0xFF747B7D
                : highlighted ? 0xFFFFE2A2 : 0xFFE6EAEB;

        graphics.fill(getX() + 2, getY() + 2,
                getX() + width + 2, getY() + height + 2, 0x6B000000);
        graphics.fill(getX(), getY(), getX() + width, getY() + height, body);
        graphics.renderOutline(getX(), getY(), width, height, border);
        graphics.fill(
                getX() + 1,
                getY() + 1,
                getX() + width - 1,
                getY() + 3,
                highlighted ? 0xFF6DAEA8 : 0xFF3B5555);
        if (active) {
            graphics.fill(
                    getX() + 2,
                    getY() + height - 2,
                    getX() + width - 2,
                    getY() + height - 1,
                    highlighted ? 0xFFD7A94E : 0xFF755D32);
        }

        var minecraft = Minecraft.getInstance();
        int iconWidth = icon.isEmpty() ? 0 : 16 + ICON_GAP;
        var text = minecraft.font.ellipsize(getMessage(), width - 10 - iconWidth);
        var visualText = Language.getInstance().getVisualOrder(text);
        if (icon.isEmpty()) {
            graphics.drawCenteredString(
                    minecraft.font,
                    visualText,
                    getX() + width / 2,
                    getY() + (height - 8) / 2,
                    textColor);
            return;
        }

        int contentWidth = 16 + ICON_GAP + minecraft.font.width(text);
        int contentLeft = getX() + Math.max(5, (width - contentWidth) / 2);
        graphics.renderItem(icon, contentLeft, getY() + (height - 16) / 2);
        graphics.drawString(
                minecraft.font,
                visualText,
                contentLeft + 16 + ICON_GAP,
                getY() + (height - 8) / 2,
                textColor,
                false);
    }
}
