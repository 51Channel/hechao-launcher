package world.hechao.velocityauth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import org.junit.jupiter.api.Test;

final class AuthorizationDecisionTest {
    @Test
    void parsesApiDecision() {
        AuthorizationDecision decision = AuthorizationDecision.fromJson("""
                {
                  "allowed": false,
                  "reason": "InsufficientTier",
                  "message": "你的当前称号等级不足以进入该服务器。",
                  "serverId": null,
                  "velocityTarget": "activity",
                  "accessTier": "Member"
                }
                """);

        assertFalse(decision.allowed());
        assertEquals("InsufficientTier", decision.reason());
        assertEquals("你的当前称号等级不足以进入该服务器。", decision.message());
        assertNull(decision.serverId());
    }

    @Test
    void parsesEscapedStrings() {
        AuthorizationDecision decision = AuthorizationDecision.fromJson(
                "{\"allowed\":true,\"reason\":\"Allowed\","
                        + "\"message\":\"line\\n\\u5141\\u8bb8\","
                        + "\"serverId\":\"lobby\"}");

        assertEquals("line\n允许", decision.message());
        assertEquals("lobby", decision.serverId());
    }

    @Test
    void rejectsNestedValues() {
        assertThrows(
                IllegalArgumentException.class,
                () -> AuthorizationDecision.fromJson(
                        "{\"allowed\":true,\"reason\":\"Allowed\","
                                + "\"message\":{\"nested\":true},\"serverId\":null}"));
    }
}
