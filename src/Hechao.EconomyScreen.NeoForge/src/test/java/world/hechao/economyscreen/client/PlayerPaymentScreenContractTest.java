package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class PlayerPaymentScreenContractTest {
    private static final Path SOURCE = Path.of(
            "src", "main", "java", "world", "hechao", "economyscreen",
            "client", "PlayerPaymentScreen.java");

    @Test
    void requiresSecondClickAndWaitsForTheAuthoritativeServerReceipt()
            throws Exception {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("confirmTicks"));
        assertTrue(source.contains("confirmationCommand"));
        assertTrue(source.contains("再次点击确认"));
        assertTrue(source.contains("connection.sendCommand(command)"));
        assertTrue(source.contains("PlayerPaymentInput.command("));
        assertTrue(source.contains("EconomyMessageProtocol.isAuthorization("));
        assertTrue(source.contains("sessionId,"));
        assertTrue(source.contains("playerBox.setEditable(editable)"));
        assertTrue(source.contains("amountBox.setEditable(editable)"));
        assertTrue(source.contains("ClientEconomyUiBridge.requestHome()"));
        assertTrue(source.contains("TIMEOUT_TICKS"));
        assertTrue(source.contains("PlayerPaymentReceipt.matches(message)"));
        assertTrue(source.contains("homeButton.active = !waiting"));
        assertTrue(source.contains("if (tone != Tone.WAITING)"));
        assertTrue(source.contains("outcomeUnknown = true"));
        assertFalse(source.contains("setEditable(!waiting)"));
    }

    @Test
    void acceptsOnlyPaymentReceiptsAndRejectsDelayedBalanceMessages() {
        assertTrue(PlayerPaymentReceipt.matches(
                "[赫朝经济] 已向 Player_51 支付 12.50"));
        assertTrue(PlayerPaymentReceipt.matches(
                "[赫朝经济] 经济服务暂时无法确认请求结果，请稍后重试。"));
        assertFalse(PlayerPaymentReceipt.matches(
                "[赫朝经济] Player_51 的余额: 120.00"));
        assertFalse(PlayerPaymentReceipt.matches(
                "[赫朝经济] 购买成功，支付 12.50，物品已进入待领取。"));
        assertFalse(PlayerPaymentReceipt.matches(
                "[赫朝经济] 经济服务公告：今晚维护。"));

        assertTrue(PlayerPaymentReceipt.isError(
                "接收者必须是另一名在线玩家。"));
        assertFalse(PlayerPaymentReceipt.isError(
                "已向 Player_51 支付 12.50"));
    }
}
