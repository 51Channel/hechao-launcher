package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class PlayerTeleportScreenContractTest {
    private static final Path SOURCE = Path.of(
            "src", "main", "java", "world", "hechao", "economyscreen",
            "client", "PlayerTeleportScreen.java");

    @Test
    void exposesOnlyVerifiedSkyrealmCoreTeleportCommands() throws Exception {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("skyrealmcore:tpa "));
        assertTrue(source.contains("skyrealmcore:tpahere "));
        assertTrue(source.contains("skyrealmcore:tpaccept"));
        assertTrue(source.contains("skyrealmcore:tpdeny"));
        assertTrue(source.contains("TeamCommandInput.validPlayerName"));
        assertTrue(source.contains("EconomyMessageProtocol.isAuthorization("));
        assertTrue(source.contains("sessionId,"));
        assertTrue(source.contains("playerBox.setEditable(interactive)"));
        assertTrue(source.contains("ClientEconomyUiBridge.requestHome()"));
        assertTrue(source.contains("TIMEOUT_TICKS"));
        assertTrue(source.contains("PlayerTeleportReceipt.matches(pendingOperation, message)"));
        assertTrue(source.contains("homeButton.active = !waiting"));
        assertTrue(source.contains("if (tone != Tone.WAITING)"));
        assertTrue(source.contains("outcomeUnknown = true"));
    }

    @Test
    void classifiesVerifiedSkyrealmCoreReceiptsWithoutConsumingAnnouncements() {
        assertTrue(PlayerTeleportReceipt.matches(
                PlayerTeleportReceipt.Operation.SEND_TO_PLAYER,
                "传送请求已发送，60 秒内等待回应。"));
        assertTrue(PlayerTeleportReceipt.matches(
                PlayerTeleportReceipt.Operation.ACCEPT,
                "已接受请求，5 秒后执行传送。"));
        assertTrue(PlayerTeleportReceipt.matches(
                PlayerTeleportReceipt.Operation.DENY,
                "已拒绝传送请求。"));
        assertFalse(PlayerTeleportReceipt.matches(
                PlayerTeleportReceipt.Operation.SEND_TO_PLAYER,
                "OtherPlayer 请求传送。使用 /tpaccept 接受或 /tpdeny 拒绝。"));
        assertFalse(PlayerTeleportReceipt.matches(
                PlayerTeleportReceipt.Operation.ACCEPT,
                "传送请求已发送，60 秒内等待回应。"));

        assertTrue(PlayerTeleportReceipt.isError(
                PlayerTeleportReceipt.Operation.SEND_TO_PLAYER,
                "对方当前不接收 TPA 请求。"));
        assertTrue(PlayerTeleportReceipt.isError(
                PlayerTeleportReceipt.Operation.ACCEPT,
                "请求者已离线，传送请求已取消。"));
        assertFalse(PlayerTeleportReceipt.isError(
                PlayerTeleportReceipt.Operation.SEND_TO_PLAYER,
                "传送请求已发送，60 秒内等待回应。"));
        assertFalse(PlayerTeleportReceipt.isError(
                PlayerTeleportReceipt.Operation.DENY,
                "已拒绝传送请求。"));
    }
}
