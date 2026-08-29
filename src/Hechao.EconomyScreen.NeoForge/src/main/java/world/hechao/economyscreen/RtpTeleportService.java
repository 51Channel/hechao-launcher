package world.hechao.economyscreen;

import com.mojang.logging.LogUtils;
import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ChunkResult;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.level.TicketType;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.RelativeMovement;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.chunk.ChunkAccess;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.phys.Vec3;
import org.slf4j.Logger;

final class RtpTeleportService {
    private static final Logger LOGGER = LogUtils.getLogger();
    private static final Duration PLAYER_COOLDOWN = Duration.ofSeconds(10);
    private static final Duration SEARCH_TIMEOUT = Duration.ofSeconds(30);
    private static final TicketType<UUID> CHUNK_TICKET = TicketType.create(
            "hechao_rtp",
            UUID::compareTo);

    private final RtpCooldownRegistry cooldowns =
            new RtpCooldownRegistry(PLAYER_COOLDOWN);
    private final HashMap<UUID, Request> requests = new HashMap<>();
    private final ScheduledExecutorService chunkExecutor =
            Executors.newSingleThreadScheduledExecutor(task -> {
                Thread thread = new Thread(task, "Hechao-Rtp-Chunk-Request");
                thread.setDaemon(true);
                return thread;
            });

    int start(ServerPlayer player) {
        UUID playerUuid = player.getUUID();
        if (requests.containsKey(playerUuid)) {
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 正在寻找安全落点，请勿重复操作。"));
            return 0;
        }

        var cooldown = cooldowns.tryAcquire(playerUuid, Instant.now());
        if (!cooldown.allowed()) {
            long seconds = Math.max(
                    1,
                    (cooldown.remaining().toMillis() + 999) / 1000);
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 随机传送冷却中，请等待 " + seconds + " 秒。"));
            return 0;
        }

        ServerLevel level = player.serverLevel();
        var worldBorder = level.getWorldBorder();
        var plan = RtpCommandPlan.create(
                worldBorder.getCenterX(),
                worldBorder.getCenterZ(),
                worldBorder.getSize());
        if (plan.isEmpty()) {
            cooldowns.release(playerUuid);
            player.sendSystemMessage(Component.literal(
                    "[天域远征] 当前世界边界过小，无法随机传送。"));
            return 0;
        }

        Request request = new Request(
                level.getServer(),
                level,
                player,
                plan.orElseThrow().maximumRange());
        requests.put(playerUuid, request);
        request.timeoutTask = chunkExecutor.schedule(
                () -> request.server.execute(() -> timeout(request)),
                SEARCH_TIMEOUT.toSeconds(),
                TimeUnit.SECONDS);
        player.sendSystemMessage(Component.literal(
                "[天域远征] 正在寻找安全落点，请稍候。"));
        requestNextCandidate(request);
        return 1;
    }

    void playerLoggedOut(UUID playerUuid) {
        cancel(requests.get(playerUuid), null, false);
        cooldowns.release(playerUuid);
    }

    void serverStopping(MinecraftServer server) {
        requests.values().stream()
                .filter(request -> request.server == server)
                .toList()
                .forEach(request -> cancel(request, null, false));
    }

    private void requestNextCandidate(Request request) {
        if (!isCurrent(request)) {
            return;
        }
        if (!isPlayerReady(request)) {
            cancel(
                    request,
                    "[天域远征] 你的位置或维度已变化，随机传送已取消。",
                    true);
            return;
        }

        while (request.attempts < RtpSafeLocationFinder.MAX_ATTEMPTS) {
            request.attempts++;
            var candidate = RtpSafeLocationFinder.sample(
                    request.level.getWorldBorder(),
                    request.maximumRange,
                    request.random);
            if (candidate.isEmpty()) {
                continue;
            }

            request.candidate = candidate.orElseThrow();
            request.ticketedChunk = request.candidate.chunkPos();
            request.level.getChunkSource().addRegionTicket(
                    CHUNK_TICKET,
                    request.ticketedChunk,
                    1,
                    request.playerUuid);
            loadChunkAsync(request.level, request.ticketedChunk)
                    .whenComplete((result, error) -> request.server.execute(
                            () -> chunkReady(request, result, error)));
            return;
        }

        cancel(
                request,
                "[天域远征] 没有找到安全落点，请稍后再试。",
                true);
    }

    private CompletableFuture<ChunkResult<ChunkAccess>> loadChunkAsync(
            ServerLevel level,
            ChunkPos chunkPos) {
        return CompletableFuture.supplyAsync(
                        () -> level.getChunkSource().getChunkFuture(
                                chunkPos.x,
                                chunkPos.z,
                                ChunkStatus.FULL,
                                true),
                        chunkExecutor)
                .thenCompose(future -> future);
    }

    private void chunkReady(
            Request request,
            ChunkResult<ChunkAccess> result,
            Throwable error) {
        if (!isCurrent(request)) {
            return;
        }
        if (!isPlayerReady(request)) {
            cancel(
                    request,
                    "[天域远征] 你的位置或维度已变化，随机传送已取消。",
                    true);
            return;
        }
        if (error != null) {
            LOGGER.error(
                    "Asynchronous RTP chunk load failed for player {}",
                    request.playerUuid,
                    error);
            cancel(
                    request,
                    "[天域远征] 区块加载失败，请稍后重试。",
                    true);
            return;
        }

        ChunkAccess chunkAccess = result == null ? null : result.orElse(null);
        if (!(chunkAccess instanceof LevelChunk levelChunk)) {
            releaseTicket(request);
            requestNextCandidate(request);
            return;
        }

        try {
            var location = RtpSafeLocationFinder.findLoaded(
                    request.player,
                    levelChunk,
                    request.candidate);
            if (location.isEmpty()) {
                releaseTicket(request);
                requestNextCandidate(request);
                return;
            }

            var target = location.orElseThrow();
            boolean teleported = request.player.teleportTo(
                    request.level,
                    target.x(),
                    target.y(),
                    target.z(),
                    Set.<RelativeMovement>of(),
                    request.player.getYRot(),
                    request.player.getXRot());
            if (!teleported) {
                cancel(
                        request,
                        "[天域远征] 传送失败，请稍后重试。",
                        true);
                return;
            }
            request.player.setDeltaMovement(Vec3.ZERO);
            request.player.resetFallDistance();
            complete(request);
        } catch (RuntimeException exception) {
            LOGGER.error(
                    "Random teleport failed for player {}",
                    request.playerUuid,
                    exception);
            cancel(
                    request,
                    "[天域远征] 暂时无法找到安全落点，请稍后重试。",
                    true);
        }
    }

    private void timeout(Request request) {
        cancel(
                request,
                "[天域远征] 随机传送查找超时，请稍后重试。",
                true);
    }

    private boolean isCurrent(Request request) {
        return request != null
                && requests.get(request.playerUuid) == request;
    }

    private boolean isPlayerReady(Request request) {
        return request.server.getPlayerList().getPlayer(request.playerUuid)
                        == request.player
                && !request.player.isRemoved()
                && request.player.isAlive()
                && request.player.serverLevel() == request.level;
    }

    private void complete(Request request) {
        if (!removeCurrent(request)) {
            return;
        }
        releaseTicket(request);
        cancelTimeout(request);
    }

    private void cancel(
            Request request,
            String message,
            boolean notifyPlayer) {
        if (!removeCurrent(request)) {
            return;
        }
        releaseTicket(request);
        cancelTimeout(request);
        cooldowns.release(request.playerUuid);
        if (notifyPlayer
                && message != null
                && request.server.getPlayerList().getPlayer(request.playerUuid)
                        == request.player) {
            request.player.sendSystemMessage(Component.literal(message));
        }
    }

    private boolean removeCurrent(Request request) {
        return request != null
                && requests.remove(request.playerUuid, request);
    }

    private void releaseTicket(Request request) {
        if (request.ticketedChunk == null) {
            return;
        }
        request.level.getChunkSource().removeRegionTicket(
                CHUNK_TICKET,
                request.ticketedChunk,
                1,
                request.playerUuid);
        request.ticketedChunk = null;
    }

    private void cancelTimeout(Request request) {
        if (request.timeoutTask != null) {
            request.timeoutTask.cancel(false);
            request.timeoutTask = null;
        }
    }

    private static final class Request {
        private final MinecraftServer server;
        private final ServerLevel level;
        private final ServerPlayer player;
        private final UUID playerUuid;
        private final int maximumRange;
        private final RandomSource random = RandomSource.create();
        private int attempts;
        private RtpSafeLocationFinder.Candidate candidate;
        private ChunkPos ticketedChunk;
        private ScheduledFuture<?> timeoutTask;

        private Request(
                MinecraftServer server,
                ServerLevel level,
                ServerPlayer player,
                int maximumRange) {
            this.server = server;
            this.level = level;
            this.player = player;
            this.playerUuid = player.getUUID();
            this.maximumRange = maximumRange;
        }
    }
}
