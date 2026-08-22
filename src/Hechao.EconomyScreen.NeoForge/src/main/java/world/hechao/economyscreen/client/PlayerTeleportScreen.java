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

final class PlayerTeleportScreen extends SinglePassBackgroundScreen {
    private static final int AUTHORIZATION_TIMEOUT_TICKS = 100;
    private static final int TIMEOUT_TICKS = 200;

    private final UUID sessionId;
    private PlayerTeleportLayout.Layout layout;
    private EditBox playerBox;
    private Button goButton;
    private Button hereButton;
    private Button acceptButton;
    private Button denyButton;
    private Button homeButton;
    private PlayerTeleportReceipt.Operation pendingOperation;
    private String feedback = "正在验证菜单会话...";
    private Tone tone = Tone.AUTHORIZING;
    private boolean authorizationGranted;
    private boolean authorizationRejected;
    private boolean outcomeUnknown;
    private int authorizationTicks;
    private int waitingTicks;

    PlayerTeleportScreen(UUID sessionId, Component title) {
        super(title);
        this.sessionId = sessionId;
    }

    @Override
    protected void init() {
        layout = PlayerTeleportLayout.calculate(width, height);
        playerBox = new EditBox(
                font,
                layout.contentLeft() + 5,
                layout.fieldTop() + 2,
                Math.max(1, layout.contentWidth() - 10),
                Math.max(12, layout.fieldHeight() - 2),
                Component.literal("目标玩家"));
        playerBox.setBordered(false);
        playerBox.setHint(Component.literal("目标玩家"));
        playerBox.setMaxLength(16);
        playerBox.setFilter(TeamCommandInput::acceptsPlayerName);
        playerBox.setResponder(ignored -> syncControls());
        addRenderableWidget(playerBox);

        goButton = actionButton(
                0,
                "传送过去",
                new ItemStack(Items.ENDER_PEARL),
                "向目标玩家发送传送请求",
                ignored -> executeTarget(
                        "skyrealmcore:tpa ",
                        "正在发送传送请求...",
                        PlayerTeleportReceipt.Operation.SEND_TO_PLAYER));
        hereButton = actionButton(
                1,
                "邀请过来",
                new ItemStack(Items.COMPASS),
                "邀请目标玩家传送到你身边",
                ignored -> executeTarget(
                        "skyrealmcore:tpahere ",
                        "正在发送邀请...",
                        PlayerTeleportReceipt.Operation.INVITE_PLAYER));
        acceptButton = actionButton(
                2,
                "接受请求",
                new ItemStack(Items.LIME_DYE),
                "接受当前待处理的传送请求",
                ignored -> execute(
                        "skyrealmcore:tpaccept",
                        "正在接受传送请求...",
                        PlayerTeleportReceipt.Operation.ACCEPT));
        denyButton = actionButton(
                3,
                "拒绝请求",
                new ItemStack(Items.BARRIER),
                "拒绝当前待处理的传送请求",
                ignored -> execute(
                        "skyrealmcore:tpdeny",
                        "正在拒绝传送请求...",
                        PlayerTeleportReceipt.Operation.DENY));

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
                "teleport")) {
            authorizationGranted = true;
            authorizationRejected = false;
            authorizationTicks = 0;
            feedback = "菜单授权已确认，可发起或处理传送请求。";
            tone = Tone.READY;
            syncControls();
            return;
        }
        var rejection = EconomyMessageProtocol.rejectionReason(
                raw,
                sessionId,
                "teleport");
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
        var completedOperation = pendingOperation;
        waitingTicks = 0;
        pendingOperation = null;
        outcomeUnknown = false;
        feedback = normalized;
        tone = PlayerTeleportReceipt.isError(completedOperation, normalized)
                ? Tone.ERROR
                : Tone.SUCCESS;
        syncControls();
    }

    boolean acceptsSystemMessage(String message) {
        return (!authorizationGranted
                        && (EconomyMessageProtocol.isAuthorization(
                                        message,
                                        sessionId,
                                        "teleport")
                                || EconomyMessageProtocol.rejectionReason(
                                                message,
                                                sessionId,
                                                "teleport")
                                        .isPresent()))
                || (authorizationGranted
                        && pendingOperation != null
                        && (tone == Tone.WAITING || outcomeUnknown)
                        && PlayerTeleportReceipt.matches(pendingOperation, message));
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
        if (tone == Tone.WAITING && ++waitingTicks >= TIMEOUT_TICKS) {
            waitingTicks = 0;
            outcomeUnknown = true;
            feedback = "传送结果暂时未知，请返回首页确认状态后再操作。";
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
                layout.contentWidth(),
                layout.fieldHeight(),
                playerBox.isFocused());
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

    private Button actionButton(
            int index,
            String label,
            ItemStack icon,
            String tooltip,
            Button.OnPress onPress) {
        var button = new IndustrialButton(
                layout.actionX(index),
                layout.actionY(index),
                layout.buttonWidth(),
                layout.buttonHeight(),
                Component.literal(label),
                icon,
                onPress);
        button.setTooltip(Tooltip.create(Component.literal(tooltip)));
        addRenderableWidget(button);
        return button;
    }

    private void executeTarget(
            String commandPrefix,
            String waitingMessage,
            PlayerTeleportReceipt.Operation operation) {
        String player = playerBox.getValue();
        if (!TeamCommandInput.validPlayerName(player)) {
            return;
        }
        execute(commandPrefix + player, waitingMessage, operation);
    }

    private void execute(
            String command,
            String waitingMessage,
            PlayerTeleportReceipt.Operation operation) {
        if (!authorizationGranted || outcomeUnknown || tone == Tone.WAITING) {
            return;
        }
        var connection = minecraft == null ? null : minecraft.getConnection();
        if (connection == null) {
            feedback = "传送连接已经断开。";
            tone = Tone.ERROR;
            syncControls();
            return;
        }
        waitingTicks = 0;
        pendingOperation = operation;
        outcomeUnknown = false;
        feedback = waitingMessage;
        tone = Tone.WAITING;
        connection.sendCommand(command);
        syncControls();
    }

    private void syncControls() {
        if (goButton == null || hereButton == null || acceptButton == null
                || denyButton == null || homeButton == null || playerBox == null) {
            return;
        }
        boolean waiting = tone == Tone.WAITING;
        boolean interactive = authorizationGranted && !waiting && !outcomeUnknown;
        boolean validTarget = TeamCommandInput.validPlayerName(playerBox.getValue());
        goButton.active = interactive && validTarget;
        hereButton.active = interactive && validTarget;
        acceptButton.active = interactive;
        denyButton.active = interactive;
        playerBox.setEditable(interactive);
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
