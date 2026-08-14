package world.hechao.economyscreen;

import com.mojang.logging.LogUtils;
import java.time.Duration;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import net.minecraft.commands.Commands;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.loading.FMLEnvironment;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.event.RegisterCommandsEvent;
import net.neoforged.neoforge.network.PacketDistributor;
import net.neoforged.neoforge.network.event.RegisterPayloadHandlersEvent;
import net.neoforged.neoforge.network.handling.IPayloadContext;
import org.slf4j.Logger;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

@Mod(HechaoEconomyScreenMod.MOD_ID)
public final class HechaoEconomyScreenMod {
    public static final String MOD_ID = "hechao_economy_screen";

    private static final Logger LOGGER = LogUtils.getLogger();
    private static final MenuSessionRegistry SESSIONS = new MenuSessionRegistry(
            Duration.ofMinutes(2),
            Duration.ofMillis(350));
    private static final Map<String, Action> ACTIONS = actions();

    public HechaoEconomyScreenMod(IEventBus modEventBus) {
        modEventBus.addListener(this::registerPayloads);
        NeoForge.EVENT_BUS.addListener(this::registerCommands);
    }

    private void registerPayloads(RegisterPayloadHandlersEvent event) {
        var registrar = event.registrar("1");
        registrar.playToClient(
                OpenMenuPayload.TYPE,
                OpenMenuPayload.STREAM_CODEC,
                HechaoEconomyScreenMod::handleOpenMenu);
        registrar.playToServer(
                MenuActionPayload.TYPE,
                MenuActionPayload.STREAM_CODEC,
                HechaoEconomyScreenMod::handleMenuAction);
    }

    private void registerCommands(RegisterCommandsEvent event) {
        event.getDispatcher().register(
                Commands.literal("hechaomenu")
                        .executes(context -> openMenu(
                                context.getSource().getPlayerOrException()))
                        .then(Commands.literal("economy")
                                .executes(context -> openMenu(
                                        context.getSource().getPlayerOrException()))));
    }

    private static int openMenu(ServerPlayer player) {
        var sessionId = SESSIONS.issue(player.getUUID(), Instant.now());
        var buttons = ACTIONS.entrySet().stream()
                .map(entry -> new OpenMenuPayload.MenuButton(
                        entry.getKey(),
                        entry.getValue().label,
                        entry.getValue().description))
                .toList();
        PacketDistributor.sendToPlayer(
                player,
                new OpenMenuPayload(
                        sessionId,
                        "天域远征",
                        "经济与生存功能",
                        buttons));
        return 1;
    }

    private static void handleOpenMenu(
            OpenMenuPayload payload,
            IPayloadContext context) {
        if (FMLEnvironment.dist == Dist.CLIENT) {
            context.enqueueWork(() -> ClientMenuOpener.open(payload));
        }
    }

    private static void handleMenuAction(
            MenuActionPayload payload,
            IPayloadContext context) {
        if (!(context.player() instanceof ServerPlayer player)) {
            return;
        }
        context.enqueueWork(() -> {
            var action = ACTIONS.get(payload.actionId());
            if (action == null) {
                LOGGER.warn(
                        "Rejected unknown menu action from player {}",
                        player.getUUID());
                return;
            }
            var validation = SESSIONS.validateAndConsume(
                    player.getUUID(),
                    payload.sessionId(),
                    Instant.now());
            if (validation != MenuSessionRegistry.Validation.ALLOWED) {
                player.sendSystemMessage(Component.literal("菜单已过期或操作过快，请重新打开。"));
                return;
            }
            player.getServer().getCommands().performPrefixedCommand(
                    player.createCommandSourceStack(),
                    action.command);
        });
    }

    private static Map<String, Action> actions() {
        var actions = new LinkedHashMap<String, Action>();
        actions.put("balance", new Action("我的余额", "查看当前金币余额", "money"));
        actions.put("shop", new Action("回收目录", "查看当前可出售物品", "shop"));
        actions.put("sell", new Action("出售主手", "为主手物品创建报价", "sell"));
        actions.put("settings", new Action("个人设置", "打开生存服个人设置", "settings"));
        actions.put("team", new Action("我的队伍", "打开队伍功能", "team"));
        return java.util.Collections.unmodifiableMap(actions);
    }

    private record Action(String label, String description, String command) {
    }
}
