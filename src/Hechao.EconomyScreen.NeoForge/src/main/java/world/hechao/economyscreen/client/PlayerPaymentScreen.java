package world.hechao.economyscreen.client;

import java.util.UUID;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import world.hechao.economyscreen.EconomyMessageProtocol;

final class PlayerPaymentScreen extends SinglePassBackgroundScreen {
    private static final int AUTHORIZATION_TIMEOUT_TICKS = 100;
    private static final int CONFIRM_TICKS = 80;
    private static final int TIMEOUT_TICKS = 200;

    private final UUID sessionId;
    private PlayerPaymentLayout.Layout layout;
    private EditBox playerBox;
    private EditBox amountBox;
    private Button submitButton;
    private Button homeButton;
    private String confirmationCommand = "";
    private String feedback = "正在验证菜单会话...";
    private Tone tone = Tone.AUTHORIZING;
    private boolean authorizationGranted;
    private boolean authorizationRejected;
    private boolean outcomeUnknown;
    private int authorizationTicks;
    private int confirmTicks;
    private int waitingTicks;

    PlayerPaymentScreen(UUID sessionId, Component title) {
        super(title);
        this.sessionId = sessionId;
    }

    @Override
    protected void init() {
        layout = PlayerPaymentLayout.calculate(width, height);
        playerBox = input(
                layout.contentLeft() + 5,
                layout.fieldTop() + 2,
                Math.max(1, layout.playerWidth() - 10),
                "收款玩家");
        playerBox.setMaxLength(16);
        playerBox.setFilter(TeamCommandInput::acceptsPlayerName);
        playerBox.setResponder(ignored -> inputChanged());

        amountBox = input(
                layout.amountLeft() + 5,
                layout.fieldTop() + 2,
                Math.max(1, layout.amountWidth() - 10),
                "金额");
        amountBox.setMaxLength(13);
        amountBox.setFilter(PlayerPaymentInput::acceptsAmount);
        amountBox.setResponder(ignored -> inputChanged());

        submitButton = new IndustrialButton(
                layout.actionLeft(),
                layout.actionY(),
                layout.actionWidth(),
                layout.buttonHeight(),
                Component.literal("下一步"),
                new ItemStack(Items.GOLD_INGOT),
                ignored -> submit());
        submitButton.setTooltip(Tooltip.create(Component.literal(
                "转账由服务端再次校验在线状态、余额与交易权限")));
        addRenderableWidget(submitButton);

        homeButton = new IndustrialButton(
                layout.returnLeft(),
                layout.returnY(),
                layout.returnWidth(),
                layout.returnHeight(),
                Component.literal("返回首页"),
                ignored -> ClientEconomyUiBridge.requestHome());
        homeButton.setTooltip(Tooltip.create(Component.literal("返回天域远征主页")));
        addRenderableWidget(homeButton);
        syncControls();
    }

    void acceptMessage(Component message) {
        String raw = message.getString();
        if (EconomyMessageProtocol.isAuthorization(
                raw,
                sessionId,
                "payment")) {
            authorizationGranted = true;
            authorizationRejected = false;
            authorizationTicks = 0;
            feedback = "菜单授权已确认，请填写收款玩家与金额。";
            tone = Tone.READY;
            syncControls();
            return;
        }
        var rejection = EconomyMessageProtocol.rejectionReason(
                raw,
                sessionId,
                "payment");
        if (rejection.isPresent()) {
            authorizationRejected = true;
            authorizationTicks = 0;
            feedback = EconomyResultState.normalize(rejection.get());
            tone = Tone.ERROR;
            syncControls();
            return;
        }
        String normalized = EconomyResultState.normalize(raw);
        if (normalized.isBlank()) {
            return;
        }
        if (!authorizationGranted) {
            return;
        }
        waitingTicks = 0;
        confirmTicks = 0;
        confirmationCommand = "";
        outcomeUnknown = false;
        feedback = normalized;
        tone = PlayerPaymentReceipt.isError(normalized) ? Tone.ERROR : Tone.SUCCESS;
        syncControls();
    }

    boolean acceptsSystemMessage(String message) {
        return (!authorizationGranted
                        && (EconomyMessageProtocol.isAuthorization(
                                        message,
                                        sessionId,
                                        "payment")
                                || EconomyMessageProtocol.rejectionReason(
                                                message,
                                                sessionId,
                                                "payment")
                                        .isPresent()))
                || (authorizationGranted
                        && (tone == Tone.WAITING || outcomeUnknown)
                        && PlayerPaymentReceipt.matches(message));
    }

    boolean awaitingAuthorization() {
        return !authorizationGranted && !authorizationRejected;
    }

    @Override
    public void tick() {
        if (awaitingAuthorization()
                && ++authorizationTicks >= AUTHORIZATION_TIMEOUT_TICKS) {
            authorizationRejected = true;
            feedback = "服务器未确认菜单授权，请返回首页后重试。";
            tone = Tone.ERROR;
        }
        if (confirmTicks > 0 && --confirmTicks == 0) {
            confirmationCommand = "";
            feedback = "确认已超时，请重新核对玩家与金额。";
            tone = Tone.READY;
        }
        if (tone == Tone.WAITING && ++waitingTicks >= TIMEOUT_TICKS) {
            waitingTicks = 0;
            outcomeUnknown = true;
            feedback = "转账结果暂时未知，请返回首页核对余额后再操作。";
            tone = Tone.ERROR;
        }
        syncControls();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        IndustrialUiTheme.renderPanel(
                graphics,
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelWidth(),
                layout.panelHeight(),
                layout.headerHeight());
        int emblemSize = layout.compact() ? 16 : 22;
        IndustrialUiTheme.renderEmblem(
                graphics,
                layout.panelLeft() + 10,
                layout.panelTop() + (layout.compact() ? 3 : 6),
                emblemSize);
        graphics.drawString(
                font,
                title,
                layout.panelLeft() + 17 + emblemSize,
                layout.panelTop() + (layout.compact() ? 7 : 12),
                0xFFFFFFFF,
                true);
        IndustrialUiTheme.renderStatusLamp(
                graphics,
                layout.panelLeft() + layout.panelWidth() - 27,
                layout.panelTop() + (layout.compact() ? 7 : 14),
                tone.color(),
                true);
        IndustrialUiTheme.renderInputField(
                graphics,
                layout.contentLeft(),
                layout.fieldTop(),
                layout.playerWidth(),
                layout.fieldHeight(),
                playerBox.isFocused());
        IndustrialUiTheme.renderInputField(
                graphics,
                layout.amountLeft(),
                layout.fieldTop(),
                layout.amountWidth(),
                layout.fieldHeight(),
                amountBox.isFocused());
        renderFeedback(graphics);
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    @Override
    public void onClose() {
        if (tone != Tone.WAITING) {
            super.onClose();
        }
    }

    private EditBox input(int x, int y, int width, String hint) {
        var input = new EditBox(
                font,
                x,
                y,
                width,
                Math.max(12, layout.fieldHeight() - 2),
                Component.literal(hint));
        input.setBordered(false);
        input.setHint(Component.literal(hint));
        addRenderableWidget(input);
        return input;
    }

    private void inputChanged() {
        String command = currentCommand();
        if (confirmTicks > 0 && !confirmationCommand.equals(command)) {
            confirmTicks = 0;
            confirmationCommand = "";
            feedback = "内容已修改，请重新核对后提交。";
            tone = Tone.READY;
        }
        syncControls();
    }

    private void submit() {
        String command = currentCommand();
        if (!authorizationGranted || outcomeUnknown
                || command == null || tone == Tone.WAITING) {
            return;
        }
        if (confirmTicks == 0 || !command.equals(confirmationCommand)) {
            confirmationCommand = command;
            confirmTicks = CONFIRM_TICKS;
            feedback = "再次点击确认：向 " + playerBox.getValue()
                    + " 转账 " + PlayerPaymentInput.parseAmount(amountBox.getValue())
                    .toPlainString() + " 金币。";
            tone = Tone.CONFIRM;
            syncControls();
            return;
        }

        var connection = minecraft == null ? null : minecraft.getConnection();
        if (connection == null) {
            feedback = "经济连接已经断开。";
            tone = Tone.ERROR;
            confirmTicks = 0;
            confirmationCommand = "";
            syncControls();
            return;
        }
        confirmTicks = 0;
        waitingTicks = 0;
        outcomeUnknown = false;
        feedback = "正在等待服务端确认转账结果...";
        tone = Tone.WAITING;
        connection.sendCommand(command);
        syncControls();
    }

    private String currentCommand() {
        if (playerBox == null || amountBox == null) {
            return null;
        }
        return PlayerPaymentInput.command(
                playerBox.getValue(),
                amountBox.getValue());
    }

    private void syncControls() {
        if (submitButton == null || homeButton == null
                || playerBox == null || amountBox == null) {
            return;
        }
        boolean waiting = tone == Tone.WAITING;
        boolean editable = authorizationGranted && !waiting && !outcomeUnknown;
        submitButton.active = editable && currentCommand() != null;
        submitButton.setMessage(Component.literal(
                !authorizationGranted
                        ? authorizationRejected ? "不可用" : "授权中"
                        : outcomeUnknown ? "请先核对"
                        : waiting
                        ? "处理中"
                        : confirmTicks > 0 ? "确认转账" : "下一步"));
        playerBox.setEditable(editable);
        amountBox.setEditable(editable);
        homeButton.active = !waiting;
    }

    private void renderFeedback(GuiGraphics graphics) {
        if (layout.statusHeight() < 8) {
            return;
        }
        if (layout.compact()) {
            IndustrialUiTheme.renderCompactStatusBay(
                    graphics,
                    layout.statusLeft(),
                    layout.statusTop(),
                    layout.statusWidth(),
                    layout.statusHeight(),
                    tone.color());
        } else {
            IndustrialUiTheme.renderInstrumentBay(
                    graphics,
                    layout.statusLeft(),
                    layout.statusTop(),
                    layout.statusWidth(),
                    layout.statusHeight(),
                    tone.color());
        }
        int textInset = layout.compact() ? 6 : 8;
        int textLeft = layout.statusLeft() + textInset;
        int textTop = layout.statusTop() + (layout.compact() ? 4 : 8);
        int textWidth = Math.max(1, layout.statusWidth() - textInset * 2);
        int verticalInset = layout.compact() ? 7 : 12;
        int maximumLines = Math.max(1, (layout.statusHeight() - verticalInset) / 10);
        var lines = font.split(Component.literal(feedback), textWidth);
        for (int index = 0; index < Math.min(maximumLines, lines.size()); index++) {
            graphics.drawString(
                    font,
                    lines.get(index),
                    textLeft,
                    textTop + index * 10,
                    tone.textColor(),
                    false);
        }
    }

    private enum Tone {
        AUTHORIZING(0xFFE2B95F, 0xFFFFD66B),
        READY(0xFF6DAEA8, 0xFFB9C2C4),
        CONFIRM(0xFFE2B95F, 0xFFFFD66B),
        WAITING(0xFFE2B95F, 0xFFFFD66B),
        SUCCESS(0xFF8CD99B, 0xFFBDE8C7),
        ERROR(0xFFFF8A80, 0xFFFFA49C);

        private final int color;
        private final int textColor;

        Tone(int color, int textColor) {
            this.color = color;
            this.textColor = textColor;
        }

        int color() {
            return color;
        }

        int textColor() {
            return textColor;
        }
    }
}
