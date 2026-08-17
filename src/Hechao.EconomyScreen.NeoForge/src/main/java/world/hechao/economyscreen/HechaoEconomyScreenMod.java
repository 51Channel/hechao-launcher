package world.hechao.economyscreen;

import com.mojang.logging.LogUtils;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.Set;
import net.minecraft.commands.Commands;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.loading.FMLEnvironment;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.event.RegisterCommandsEvent;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;
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
    private static final Map<String, MenuActions.Definition> ACTIONS = MenuActions.all();

    public HechaoEconomyScreenMod(IEventBus modEventBus) {
        modEventBus.addListener(this::registerPayloads);
        NeoForge.EVENT_BUS.addListener(this::registerCommands);
        NeoForge.EVENT_BUS.addListener(this::playerLoggedOut);
    }

    private void registerPayloads(RegisterPayloadHandlersEvent event) {
        var registrar = event.registrar("2");
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
        var actionIds = ACTIONS.entrySet().stream()
                .filter(entry -> entry.getValue().command().startsWith("hechaoeconomy:")
                        || isCommandRegistered(player, entry.getValue().command()))
                .map(Map.Entry::getKey)
                .toList();
        if (actionIds.isEmpty()) {
            player.sendSystemMessage(Component.literal("当前没有可用的服务器功能。"));
            return 0;
        }
        var sessionId = SESSIONS.issue(
                player.getUUID(),
                Set.copyOf(actionIds),
                Instant.now());
        PacketDistributor.sendToPlayer(
                player,
                new OpenMenuPayload(sessionId, actionIds));
        return 1;
    }

    private static boolean isCommandRegistered(
            ServerPlayer player,
            String command) {
        var separator = command.indexOf(' ');
        var root = separator < 0 ? command : command.substring(0, separator);
        return player.getServer()
                .getCommands()
                .getDispatcher()
                .getRoot()
                .getChild(root) != null;
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
            var validation = SESSIONS.validateAndConsume(
                    player.getUUID(),
                    payload.sessionId(),
                    payload.actionId(),
                    Instant.now());
            if (validation != MenuSessionRegistry.Validation.ALLOWED) {
                LOGGER.warn(
                        "Rejected menu action {} from player {}: {}",
                        payload.actionId(),
                        player.getUUID(),
                        validation);
                player.sendSystemMessage(Component.literal("菜单已失效，请重新打开。"));
                return;
            }
            var action = ACTIONS.get(payload.actionId());
            if (action == null) {
                return;
            }
            player.getServer().getCommands().performPrefixedCommand(
                    player.createCommandSourceStack(),
                    action.command());
        });
    }

    private void playerLoggedOut(PlayerEvent.PlayerLoggedOutEvent event) {
        SESSIONS.remove(event.getEntity().getUUID());
    }
}
