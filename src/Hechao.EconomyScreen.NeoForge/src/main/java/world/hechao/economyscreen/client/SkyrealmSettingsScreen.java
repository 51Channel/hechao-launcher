package world.hechao.economyscreen.client;

import java.util.List;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

final class SkyrealmSettingsScreen extends SinglePassBackgroundScreen {
    private static final List<Setting> SETTINGS = List.of(
            new Setting(11, "接收 TPA 请求", new ItemStack(Items.ENDER_PEARL)),
            new Setting(13, "TPA 自动接受", new ItemStack(Items.CLOCK)),
            new Setting(15, "接收队伍邀请", new ItemStack(Items.PLAYER_HEAD)));

    private final ChestMenu menu;
    private final Button[] toggles = new Button[SETTINGS.size()];
    private SkyrealmSettingsLayout.Layout layout;

    SkyrealmSettingsScreen(ChestMenu menu) {
        super(Component.literal(ClientEconomyUiBridge.SETTINGS_TITLE));
        this.menu = menu;
    }

    @Override
    protected void init() {
        layout = SkyrealmSettingsLayout.calculate(width, height);
        for (int index = 0; index < SETTINGS.size(); index++) {
            int settingIndex = index;
            var button = new IndustrialButton(
                    layout.toggleX(),
                    layout.toggleY(index),
                    SkyrealmSettingsLayout.TOGGLE_WIDTH,
                    SkyrealmSettingsLayout.TOGGLE_HEIGHT,
                    Component.literal("同步中"),
                    ignored -> clickSetting(settingIndex));
            toggles[index] = button;
            addRenderableWidget(button);
        }
        addRenderableWidget(new IndustrialButton(
                layout.returnX(),
                layout.footerY(),
                SkyrealmSettingsLayout.RETURN_WIDTH,
                SkyrealmSettingsLayout.RETURN_HEIGHT,
                Component.literal("返回首页"),
                ignored -> returnHome()));
        syncButtons();
    }

    @Override
    public void tick() {
        super.tick();
        syncButtons();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        syncButtons();
        IndustrialUiTheme.renderPanel(
                graphics,
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelWidth(),
                layout.panelHeight());
        IndustrialUiTheme.renderEmblem(
                graphics,
                layout.panelLeft() + 10,
                layout.panelTop() + 6,
                22);
        graphics.drawString(
                font,
                title,
                layout.panelLeft() + 39,
                layout.panelTop() + 12,
                0xFFFFFFFF,
                true);

        for (int index = 0; index < SETTINGS.size(); index++) {
            renderSetting(graphics, index);
        }
        IndustrialUiTheme.renderDivider(
                graphics,
                layout.panelLeft() + 8,
                layout.panelLeft() + layout.panelWidth() - 8,
                layout.footerY() - 6);
    }

    @Override
    public void onClose() {
        closeContainer();
        super.onClose();
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void renderSetting(GuiGraphics graphics, int index) {
        var setting = SETTINGS.get(index);
        int rowTop = layout.rowTop(index);
        int rowHeight = layout.rowHeight();
        if (rowHeight <= 0) {
            return;
        }
        IndustrialUiTheme.renderCard(
                graphics,
                layout.rowsLeft(),
                rowTop,
                layout.rowWidth(),
                rowHeight,
                toggles[index] != null && toggles[index].isHovered());
        if (rowHeight >= 18) {
            int iconY = rowTop + Math.max(1, (rowHeight - 16) / 2);
            graphics.renderItem(setting.icon(), layout.rowsLeft() + 8, iconY);
            int textY = rowTop + Math.max(1, (rowHeight - 8) / 2);
            graphics.drawString(
                    font,
                    setting.label(),
                    layout.rowsLeft() + 34,
                    textY,
                    0xFFF1F3F3,
                    false);
        }
    }

    private void syncButtons() {
        for (int index = 0; index < SETTINGS.size(); index++) {
            var button = toggles[index];
            if (button == null) {
                continue;
            }
            var stack = settingItem(index);
            button.active = !stack.isEmpty();
            button.setMessage(Component.literal(
                    stack.isEmpty()
                            ? "同步中"
                            : stack.is(Items.LIME_DYE) ? "已开启" : "已关闭"));
        }
    }

    private void clickSetting(int index) {
        if (index < 0 || index >= SETTINGS.size()) {
            return;
        }
        clickControl(SETTINGS.get(index).slot());
    }

    private ItemStack settingItem(int index) {
        int slot = SETTINGS.get(index).slot();
        return slot < menu.slots.size()
                ? menu.getSlot(slot).getItem()
                : ItemStack.EMPTY;
    }

    private void clickControl(int slot) {
        if (minecraft == null || minecraft.player == null || minecraft.gameMode == null) {
            return;
        }
        minecraft.gameMode.handleInventoryMouseClick(
                menu.containerId,
                slot,
                0,
                ClickType.PICKUP,
                minecraft.player);
    }

    private void returnHome() {
        closeContainer();
        ClientEconomyUiBridge.requestHome();
    }

    private void closeContainer() {
        if (minecraft != null
                && minecraft.player != null
                && minecraft.player.containerMenu == menu) {
            minecraft.player.closeContainer();
        }
    }

    private record Setting(int slot, String label, ItemStack icon) {
    }
}
