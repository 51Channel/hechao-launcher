package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.UUID;
import org.junit.jupiter.api.Test;

final class EconomyMessageProtocolTest {
    @Test
    void menuSessionReceiptsAreExactSessionAndActionScoped() {
        var sessionId = UUID.fromString("27be6f9f-b167-4f43-aaed-ab350bf2c56e");
        String receipt = EconomyMessageProtocol.authorization(sessionId, "payment");

        assertEquals(
                "[赫朝经济] 菜单授权已通过: "
                        + "27be6f9f-b167-4f43-aaed-ab350bf2c56e:payment",
                receipt);
        assertTrue(EconomyMessageProtocol.isAuthorization(
                receipt,
                sessionId,
                "payment"));
        assertFalse(EconomyMessageProtocol.isAuthorization(
                receipt,
                sessionId,
                "teleport"));
        assertFalse(EconomyMessageProtocol.isAuthorization(
                receipt,
                UUID.fromString("df65a141-8ec2-4f7f-a66e-f7762cb77808"),
                "payment"));
        assertTrue(EconomyMessageProtocol.isAuthorizationReceipt(receipt));
        assertTrue(EconomyMessageProtocol.isMenuSessionReceipt(receipt));

        String rejection = EconomyMessageProtocol.rejection(
                sessionId,
                "payment",
                "[赫朝经济] 菜单已失效，请重新打开。");
        assertEquals(
                "[赫朝经济] 菜单已失效，请重新打开。",
                EconomyMessageProtocol.rejectionReason(
                        rejection,
                        sessionId,
                        "payment").orElseThrow());
        assertTrue(EconomyMessageProtocol.rejectionReason(
                rejection,
                UUID.fromString("df65a141-8ec2-4f7f-a66e-f7762cb77808"),
                "payment").isEmpty());
        assertTrue(EconomyMessageProtocol.isMenuSessionReceipt(rejection));
        assertThrows(
                IllegalArgumentException.class,
                () -> EconomyMessageProtocol.authorization(
                        sessionId,
                        "payment confirm"));
    }
}
