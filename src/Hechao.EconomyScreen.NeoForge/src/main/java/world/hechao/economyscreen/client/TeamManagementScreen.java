package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

final class TeamManagementScreen extends SinglePassBackgroundScreen {
    private static final int CONFIRM_TICKS = 80;
    private static final int REFRESH_DELAY_TICKS = 16;

    private final TeamStatus status = new TeamStatus();
    private TeamManagementLayout.Layout layout;
    private EditBox playerBox;
    private EditBox chatBox;
    private Button inviteButton;
    private Button acceptButton;
    private Button kickButton;
    private Button leaveButton;
    private Button chatButton;
    private final List<Button> memberButtons = new ArrayList<>();
    private int pendingRefreshTicks;
    private int kickConfirmTicks;
    private int leaveConfirmTicks;
    private String kickConfirmTarget = "";
    private String selectedMember = "";

    TeamManagementScreen(Component title) {
        super(title);
    }

    @Override
    protected void init() {
        layout = TeamManagementLayout.calculate(width, height);
        int controlsLeft = layout.controlsLeft();
        int controlsWidth = layout.controlsWidth();
        int rowTop = layout.controlsTop();
        int compactButton = Math.min(76, Math.max(52, controlsWidth / 3));

        playerBox = input(
                controlsLeft + 5,
                rowTop + 2,
                Math.max(34, controlsWidth - compactButton - 15),
                "玩家名称");
        playerBox.setMaxLength(16);
        playerBox.setFilter(TeamCommandInput::acceptsPlayerName);
        playerBox.setResponder(value -> {
            if (!value.equals(kickConfirmTarget)) {
                kickConfirmTicks = 0;
                kickConfirmTarget = "";
            }
            syncButtons();
        });

        inviteButton = button(
                controlsLeft + controlsWidth - compactButton,
                rowTop,
                compactButton,
                "邀请玩家",
                ignored -> invite());
        inviteButton.setTooltip(Tooltip.create(Component.literal(
                "首次邀请会自动创建队伍")));

        int secondTop = rowTop + TeamManagementLayout.BUTTON_HEIGHT + TeamManagementLayout.GAP;
        int actionWidth = Math.max(1,
                (controlsWidth - TeamManagementLayout.GAP * 2) / 3);
        acceptButton = button(controlsLeft, secondTop, actionWidth,
                "接受邀请", ignored -> execute("accept", "正在接受邀请...", true));
        kickButton = button(
                controlsLeft + actionWidth + TeamManagementLayout.GAP,
                secondTop,
                actionWidth,
                "移出成员",
                ignored -> kick());
        leaveButton = button(
                controlsLeft + (actionWidth + TeamManagementLayout.GAP) * 2,
                secondTop,
                actionWidth,
                "离开队伍",
                ignored -> leave());
        leaveButton.setTooltip(Tooltip.create(Component.literal(
                "队长离开时会转移队长，单人队伍会解散")));

        int thirdTop = secondTop + TeamManagementLayout.BUTTON_HEIGHT + TeamManagementLayout.GAP;
        chatBox = input(
                controlsLeft + 5,
                thirdTop + 2,
                Math.max(34, controlsWidth - compactButton - 15),
                "队伍消息");
        chatBox.setMaxLength(100);
        chatBox.setFilter(TeamCommandInput::acceptsChat);
        chatButton = button(
                controlsLeft + controlsWidth - compactButton,
                thirdTop,
                compactButton,
                "发送消息",
                ignored -> sendChat());

        var refresh = button(
                layout.panelLeft() + layout.panelWidth() - 70,
                layout.panelTop() + 7,
                36,
                "刷新",
                ignored -> refresh());
        refresh.setTooltip(Tooltip.create(Component.literal("刷新队伍状态")));
        var home = button(
                width / 2 - 50,
                layout.footerY(),
                100,
                "返回首页",
                ignored -> ClientEconomyUiBridge.requestHome());
        home.setTooltip(Tooltip.create(Component.literal("返回天域远征主页")));
        addMemberButtons();
        syncButtons();
    }

    void acceptMessage(Component message) {
        status.accept(message.getString());
        syncButtons();
    }

    boolean acceptsSystemMessage(String message) {
        return status.accepts(message);
    }

    @Override
    public void tick() {
        status.tick();
        if (kickConfirmTicks > 0 && --kickConfirmTicks == 0) {
            kickConfirmTarget = "";
        }
        if (leaveConfirmTicks > 0) {
            leaveConfirmTicks--;
        }
        if (pendingRefreshTicks > 0 && --pendingRefreshTicks == 0) {
            refresh();
        }
        syncButtons();
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
        renderStatusLamp(graphics);
        renderSummary(graphics);
        renderControls(graphics);
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void renderStatusLamp(GuiGraphics graphics) {
        int accent = status.error()
                ? 0xFFFF8A80
                : status.waiting() ? 0xFFFFD75A : 0xFF8CD99B;
        int lampLeft = layout.panelLeft() + layout.panelWidth() - 27;
        IndustrialUiTheme.renderStatusLamp(
                graphics,
                lampLeft,
                layout.panelTop() + 14,
                accent,
                true);
    }

    private void renderSummary(GuiGraphics graphics) {
        if (layout.summaryHeight() < 8) {
            return;
        }
        int accent = status.error()
                ? 0xFFFF8A80
                : status.membership() == TeamStatus.Membership.JOINED
                        ? 0xFF8CD99B
                        : 0xFFE2B95F;
        IndustrialUiTheme.renderInstrumentBay(
                graphics,
                layout.contentLeft(),
                layout.contentTop(),
                layout.summaryWidth(),
                layout.summaryHeight(),
                accent);

        int left = layout.contentLeft() + 10;
        int top = layout.contentTop() + 10;
        if (layout.twoColumns()) {
            IndustrialUiTheme.renderIconDock(graphics, left, top, 40, accent);
            graphics.renderItem(new ItemStack(Items.PLAYER_HEAD), left + 12, top + 12);
            left += 51;
        }
        String heading = switch (status.membership()) {
            case LOADING -> "正在同步";
            case NONE -> "尚未加入队伍";
            case JOINED -> status.leader().isBlank()
                    ? "已加入队伍"
                    : "队长：" + status.leader();
        };
        if (layout.summaryHeight() >= 20) {
            graphics.drawString(
                    font,
                    fit(heading, Math.max(1,
                            layout.contentLeft() + layout.summaryWidth() - left - 8)),
                    left,
                    top + (layout.twoColumns() ? 2 : 0),
                    0xFFFFFFFF,
                    true);
        }
        if (layout.twoColumns()) {
            String members = status.members().isEmpty()
                    ? "队伍成员 0 人"
                    : "队伍成员 " + status.members().size() + " 人 · 点击成员可操作";
            graphics.drawString(
                    font,
                    fit(members, Math.max(1,
                            layout.contentLeft() + layout.summaryWidth() - left - 8)),
                    left,
                    top + 19,
                    0xFFADB5B7,
                    false);
            if (status.members().isEmpty()) {
                graphics.drawString(
                        font,
                        "输入玩家名称后邀请",
                        layout.contentLeft() + 10,
                        top + 43,
                        0xFF8CD99B,
                        false);
            }
        }
        int feedbackY = layout.contentTop() + layout.summaryHeight() - 15;
        if (feedbackY > top + 10) {
            graphics.drawString(
                    font,
                    fit(status.feedback(), layout.summaryWidth() - 20),
                    layout.contentLeft() + 10,
                    feedbackY,
                    status.error() ? 0xFFFF8A80 : accent,
                    false);
        }
    }

    private void renderControls(GuiGraphics graphics) {
        int left = layout.controlsLeft();
        int top = layout.controlsTop();
        int width = layout.controlsWidth();
        int bottom = top + Math.min(
                layout.controlsHeight(),
                TeamManagementLayout.BUTTON_HEIGHT * 3 + TeamManagementLayout.GAP * 2);
        IndustrialUiTheme.renderInputField(
                graphics,
                playerBox.getX() - 5,
                playerBox.getY() - 2,
                playerBox.getWidth() + 10,
                18,
                playerBox.isFocused());
        IndustrialUiTheme.renderInputField(
                graphics,
                chatBox.getX() - 5,
                chatBox.getY() - 2,
                chatBox.getWidth() + 10,
                18,
                chatBox.isFocused());
        if (bottom < top + layout.controlsHeight()) {
            IndustrialUiTheme.renderSignalTicks(
                    graphics,
                    left,
                    left + width,
                    bottom + 6,
                    0xFF6DAEA8,
                    status.membership() == TeamStatus.Membership.JOINED ? 8 : 3);
        }
    }

    private EditBox input(int x, int y, int width, String hint) {
        var input = new EditBox(
                font,
                x,
                y,
                width,
                16,
                Component.literal(hint));
        input.setBordered(false);
        input.setHint(Component.literal(hint));
        input.setResponder(ignored -> syncButtons());
        addRenderableWidget(input);
        return input;
    }

    private Button button(
            int x,
            int y,
            int width,
            String label,
            Button.OnPress onPress) {
        var button = new IndustrialButton(
                x,
                y,
                width,
                TeamManagementLayout.BUTTON_HEIGHT,
                Component.literal(label),
                onPress);
        addRenderableWidget(button);
        return button;
    }

    private void addMemberButtons() {
        if (!layout.twoColumns()) {
            return;
        }
        int left = layout.contentLeft() + 10;
        int top = layout.contentTop() + 40;
        int buttonWidth = Math.max(1, layout.summaryWidth() - 20);
        for (int index = 0; index < 6; index++) {
            int memberIndex = index;
            var memberButton = new IndustrialButton(
                    left,
                    top + index * 17,
                    buttonWidth,
                    16,
                    Component.literal("成员 " + (index + 1)),
                    ignored -> selectMember(memberIndex));
            memberButton.setTooltip(Tooltip.create(Component.literal("填入右侧操作框")));
            addRenderableWidget(memberButton);
            memberButtons.add(memberButton);
        }
    }

    private void selectMember(int index) {
        if (index < 0 || index >= status.members().size()) {
            return;
        }
        selectedMember = status.members().get(index);
        playerBox.setValue(selectedMember);
        kickConfirmTicks = 0;
        kickConfirmTarget = "";
        status.note("已选择 " + selectedMember + "，可移出该成员");
        syncButtons();
    }

    private void invite() {
        String player = playerBox.getValue();
        if (!TeamCommandInput.validPlayerName(player)) {
            return;
        }
        execute("invite " + player, "正在邀请 " + player + "...", true);
    }

    private void kick() {
        String player = playerBox.getValue();
        if (!TeamCommandInput.validPlayerName(player)) {
            return;
        }
        if (kickConfirmTicks == 0 || !player.equals(kickConfirmTarget)) {
            kickConfirmTarget = player;
            kickConfirmTicks = CONFIRM_TICKS;
            status.note("再次点击“移出成员”以确认 " + player);
            syncButtons();
            return;
        }
        kickConfirmTicks = 0;
        kickConfirmTarget = "";
        execute("kick " + player, "正在移出 " + player + "...", true);
    }

    private void leave() {
        if (leaveConfirmTicks == 0) {
            leaveConfirmTicks = CONFIRM_TICKS;
            status.note("再次点击“确认离队”完成操作");
            syncButtons();
            return;
        }
        leaveConfirmTicks = 0;
        execute("leave", "正在离开队伍...", true);
    }

    private void sendChat() {
        String message = TeamCommandInput.normalizedChat(chatBox.getValue());
        if (message.isBlank()) {
            return;
        }
        execute("chat " + message, "队伍消息已发送", false);
        chatBox.setValue("");
    }

    private void refresh() {
        pendingRefreshTicks = 0;
        execute("list", "正在刷新队伍状态...", false);
    }

    private void execute(String subcommand, String feedback, boolean refreshAfter) {
        var connection = minecraft == null ? null : minecraft.getConnection();
        if (connection == null) {
            status.accept("队伍连接已断开。");
            syncButtons();
            return;
        }
        status.begin(feedback);
        connection.sendCommand("skyrealmcore:team " + subcommand);
        if (refreshAfter) {
            pendingRefreshTicks = REFRESH_DELAY_TICKS;
        }
        syncButtons();
    }

    private void syncButtons() {
        if (inviteButton == null || acceptButton == null || kickButton == null
                || leaveButton == null || chatButton == null) {
            return;
        }
        boolean validTarget = TeamCommandInput.validPlayerName(playerBox.getValue());
        boolean joined = status.membership() == TeamStatus.Membership.JOINED;
        boolean known = status.membership() != TeamStatus.Membership.LOADING;
        inviteButton.active = known && validTarget;
        acceptButton.active = known && !joined;
        kickButton.active = joined
                && validTarget
                && !playerBox.getValue().equals(status.leader());
        leaveButton.active = joined;
        chatButton.active = joined
                && !TeamCommandInput.normalizedChat(chatBox.getValue()).isBlank();
        kickButton.setMessage(Component.literal(
                kickConfirmTicks > 0 ? "确认移出" : "移出成员"));
        leaveButton.setMessage(Component.literal(
                leaveConfirmTicks > 0 ? "确认离队" : "离开队伍"));
        syncMemberButtons();
    }

    private void syncMemberButtons() {
        for (int index = 0; index < memberButtons.size(); index++) {
            var button = memberButtons.get(index);
            if (index >= status.members().size()) {
                button.visible = false;
                button.active = false;
                continue;
            }
            String member = status.members().get(index);
            button.visible = true;
            button.active = true;
            String label = member.equals(selectedMember)
                    ? "已选 " + member
                    : member;
            button.setMessage(Component.literal(fit(label, button.getWidth() - 8)));
        }
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0,
                maximumWidth - font.width("..."))) + "...";
    }
}
