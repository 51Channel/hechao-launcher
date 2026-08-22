package world.hechao.economyscreen;

import com.mojang.logging.LogUtils;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.Set;
import net.minecraft.commands.CommandSourceStack;
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
import world.hechao.economyscreen.client.ClientEconomyUiBridge;
import world.hechao.economyscreen.client.ClientPauseMenuEntry;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

@Mod(HechaoEconomyScreenMod.MOD_ID)
public final class HechaoEconomyScreenMod {
    public static final String MOD_ID = "hechao_economy_screen";

    private static final Logger LOGGER = LogUtils.getLogger();
    private static final MenuSessionRegistry SESSIONS = new MenuSessionRegistry(
            Duration.ofMinutes(2),
            Duration.ofMillis(350));
    private static final RtpCooldownRegistry RTP_COOLDOWNS =
            new RtpCooldownRegistry(Duration.ofSeconds(60));
    private static final Map<String, MenuActions.Definition> ACTIONS = MenuActions.all();

    public HechaoEconomyScreenMod(IEventBus modEventBus) {
        modEventBus.addListener(this::registerPayloads);
        NeoForge.EVENT_BUS.addListener(this::registerCommands);
        NeoForge.EVENT_BUS.addListener(this::playerLoggedOut);
        if (FMLEnvironment.dist == Dist.CLIENT) {
            ClientPauseMenuEntry.register();
            ClientEconomyUiBridge.register();
        }
    }

    private void registerPayloads(RegisterPayloadHandlersEvent event) {
        var registrar = event.registrar("3");
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
                                        context.getSource().getPlayerOrException())))
                        .then(Commands.literal("rtp")
                                .executes(context -> randomTeleport(
                                        context.getSource().getPlayerOrException())))
                        .then(Commands.literal("setcity")
                                .requires(source -> source.hasPermission(2))
                                .executes(context -> setCity(context.getSource()))));
        event.getDispatcher().register(
                Commands.literal("rtp")
                        .executes(context -> randomTeleport(
                                context.getSource().getPlayerOrException())));
        event.getDispatcher().register(
                Commands.literal("setcity")
                        .requires(source -> source.hasPermission(2))
                        .executes(context -> setCity(context.getSource())));
    }

    private static int randomTeleport(ServerPlayer player) {
        var now = Instant.now();
        var cooldown = RTP_COOLDOWNS.tryAcquire(player.getUUID(), now);
        if (!cooldown.allowed()) {
            long seconds = Math.max(
                    1,
                    (cooldown.remaining().toMillis() + 999) / 1000);
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 随机传送冷却中，请等待 " + seconds + " 秒。"));
            return 0;
        }

        var worldBorder = player.serverLevel().getWorldBorder();
        var plan = RtpCommandPlan.create(
                worldBorder.getCenterX(),
                worldBorder.getCenterZ(),
                worldBorder.getSize());
        if (plan.isEmpty()) {
            RTP_COOLDOWNS.release(player.getUUID());
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 当前世界边界过小，无法随机传送。"));
            return 0;
        }

        try {
            var commandSource = player.createCommandSourceStack()
                    .withPermission(2)
                    .withCallback((success, ignoredResult) -> {
                        if (!success) {
                            RTP_COOLDOWNS.release(player.getUUID());
                        }
                    });
            player.getServer().getCommands().performPrefixedCommand(
                    commandSource,
                    plan.orElseThrow().command());
            return 1;
        } catch (RuntimeException exception) {
            RTP_COOLDOWNS.release(player.getUUID());
            LOGGER.error("Random teleport failed for player {}", player.getUUID(), exception);
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 暂时无法找到安全落点，请稍后重试。"));
            return 0;
        }
    }

    private static int setCity(CommandSourceStack source) {
        var command = "essentialsspawn:setspawn";
        if (!isCommandUsable(source, command)) {
            source.sendFailure(Component.literal(
                    "[天域远征] 主城组件当前不可用，位置没有修改。"));
            return 0;
        }
        source.getServer().getCommands().performPrefixedCommand(
                source,
                command);
        return 1;
    }

    private static int openMenu(ServerPlayer player) {
        var actionIds = ACTIONS.entrySet().stream()
                .filter(entry -> isCommandUsable(player, entry.getValue().command()))
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

    private static boolean isCommandUsable(
            ServerPlayer player,
            String command) {
        return isCommandUsable(player.createCommandSourceStack(), command);
    }

    private static boolean isCommandUsable(
            CommandSourceStack source,
            String command) {
        var separator = command.indexOf(' ');
        var root = separator < 0 ? command : command.substring(0, separator);
        var node = source.getServer()
                .getCommands()
                .getDispatcher()
                .getRoot()
                .getChild(root);
        return node != null && node.canUse(source);
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
            var action = ACTIONS.get(payload.actionId());
            if (validation != MenuSessionRegistry.Validation.ALLOWED) {
                LOGGER.warn(
                        "Rejected menu action {} from player {}: {}",
                        payload.actionId(),
                        player.getUUID(),
                        validation);
                String rejection = rejectionMessage(validation);
                if (action != null
                        && action.executionMode()
                                == MenuActions.ExecutionMode.CLIENT_SCREEN) {
                    rejection = EconomyMessageProtocol.rejection(
                            payload.sessionId(),
                            payload.actionId(),
                            rejection);
                }
                player.sendSystemMessage(Component.literal(rejection));
                return;
            }
            if (action == null) {
                return;
            }
            if (action.executionMode() == MenuActions.ExecutionMode.SERVER) {
                player.getServer().getCommands().performPrefixedCommand(
                        player.createCommandSourceStack(),
                        action.command());
            } else {
                player.sendSystemMessage(Component.literal(
                        EconomyMessageProtocol.authorization(
                                payload.sessionId(),
                                payload.actionId())));
            }
        });
    }

    private void playerLoggedOut(PlayerEvent.PlayerLoggedOutEvent event) {
        SESSIONS.remove(event.getEntity().getUUID());
        RTP_COOLDOWNS.release(event.getEntity().getUUID());
    }

    private static String rejectionMessage(MenuSessionRegistry.Validation validation) {
        return switch (validation) {
            case RATE_LIMITED -> "[赫朝经济] 操作太快，请稍后重试。";
            case ACTION_NOT_ALLOWED -> "[赫朝经济] 当前功能不可用，请重新打开菜单。";
            default -> "[赫朝经济] 菜单已失效，请重新打开。";
        };
    }
}
