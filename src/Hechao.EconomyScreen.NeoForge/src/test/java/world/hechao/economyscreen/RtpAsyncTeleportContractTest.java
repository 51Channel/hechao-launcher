package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class RtpAsyncTeleportContractTest {
    @Test
    void chunkGenerationNeverWaitsOnTheServerThread() throws Exception {
        String source = Files.readString(sourcePath("RtpTeleportService.java"));

        assertTrue(source.contains("CompletableFuture.supplyAsync("));
        assertTrue(source.contains("getChunkFuture("));
        assertTrue(source.contains("ChunkStatus.FULL"));
        assertTrue(source.contains("request.server.execute("));
        assertFalse(source.contains(".join()"));
        assertFalse(source.contains("managedBlock"));
    }

    @Test
    void requestsReleaseTicketsOnEveryTerminalPath() throws Exception {
        String source = Files.readString(sourcePath("RtpTeleportService.java"));

        assertTrue(source.contains("addRegionTicket("));
        assertTrue(source.contains("removeRegionTicket("));
        assertTrue(source.contains("SEARCH_TIMEOUT"));
        assertTrue(source.contains("requests.remove(request.playerUuid, request)"));
        assertTrue(source.contains("request.player.serverLevel() == request.level"));
        assertTrue(source.contains("request.player.isAlive()"));
        assertTrue(source.contains("void playerLoggedOut(UUID playerUuid)"));
        assertTrue(source.contains("void serverStopping(MinecraftServer server)"));
    }

    @Test
    void landingChecksReadOnlyTheLoadedChunk() throws Exception {
        String source = Files.readString(sourcePath("RtpSafeLocationFinder.java"));

        assertTrue(source.contains("findLoaded("));
        assertTrue(source.contains("chunk.getHeight("));
        assertTrue(source.contains("chunk.getBlockState("));
        assertFalse(source.contains("level.getHeight("));
        assertFalse(source.contains("level.getBlockState("));
    }

    private static Path sourcePath(String fileName) {
        return Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                fileName);
    }
}
