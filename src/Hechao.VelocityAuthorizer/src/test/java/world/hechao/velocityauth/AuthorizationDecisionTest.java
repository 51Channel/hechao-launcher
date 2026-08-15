package world.hechao.velocityauth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

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
        assertEquals("activity", decision.velocityTarget());
    }

    @Test
    void parsesEscapedStrings() {
        AuthorizationDecision decision = AuthorizationDecision.fromJson(
                "{\"allowed\":true,\"reason\":\"Allowed\","
                        + "\"message\":\"line\\n\\u5141\\u8bb8\","
                        + "\"serverId\":\"lobby\","
                        + "\"velocityTarget\":\"survival2\"}");

        assertEquals("line\n允许", decision.message());
        assertEquals("lobby", decision.serverId());
        assertEquals("survival2", decision.velocityTarget());
    }

    @Test
    void parsesApprovedDynamicBackend() {
        AuthorizationDecision decision = AuthorizationDecision.fromJson("""
                {
                  "allowed": true,
                  "reason": "Allowed",
                  "message": "ok",
                  "serverId": "survival-industry",
                  "velocityTarget": "survival-industry",
                  "backendHost": "127.0.0.1",
                  "backendPort": 25600
                }
                """);

        assertTrue(decision.hasDynamicBackend());
        assertTrue(decision.hasValidDynamicBackend());
        assertEquals(25600, decision.backendPort());
    }

    @Test
    void rejectsExternalOrPartialDynamicBackend() {
        AuthorizationDecision external = new AuthorizationDecision(
                true, "Allowed", "ok", "pvp-ranked", "pvp-ranked",
                "203.0.113.10", 25600);
        AuthorizationDecision partial = new AuthorizationDecision(
                true, "Allowed", "ok", "pvp-ranked", "pvp-ranked",
                "127.0.0.1", null);

        assertFalse(external.hasValidDynamicBackend());
        assertFalse(partial.hasValidDynamicBackend());
    }

    @Test
    void rejectsNestedValues() {
        assertThrows(
                IllegalArgumentException.class,
                () -> AuthorizationDecision.fromJson(
                        "{\"allowed\":true,\"reason\":\"Allowed\","
                                + "\"message\":{\"nested\":true},\"serverId\":null,"
                                + "\"velocityTarget\":\"lobby\"}"));
    }

    @Test
    void rejectsAllowedDecisionWithoutGrantedTarget() {
        assertThrows(
                IllegalArgumentException.class,
                () -> AuthorizationDecision.fromJson(
                        "{\"allowed\":true,\"reason\":\"Allowed\","
                                + "\"message\":\"ok\",\"serverId\":\"lobby\"}"));
    }

    @Test
    void identifiesCompatibilityFailuresAsImmediateDenials() {
        AuthorizationDecision profileMismatch = AuthorizationDecision.fromJson("""
                {
                  "allowed": false,
                  "reason": "ClientProfileMismatch",
                  "message": "wrong profile",
                  "serverId": "activity",
                  "velocityTarget": "activity"
                }
                """);
        AuthorizationDecision tierFailure = AuthorizationDecision.fromJson("""
                {
                  "allowed": false,
                  "reason": "InsufficientTier",
                  "message": "tier",
                  "serverId": "activity",
                  "velocityTarget": "activity"
                }
                """);

        assertTrue(profileMismatch.requiresImmediateDenial());
        assertFalse(tierFailure.requiresImmediateDenial());
    }

    @Test
    void validatesInitialSessionServerId() {
        AuthorizationDecision complete = AuthorizationDecision.fromJson("""
                {
                  "allowed": true,
                  "reason": "Allowed",
                  "message": "ok",
                  "serverId": "lobby",
                  "velocityTarget": "lobby"
                }
                """);
        AuthorizationDecision missing = AuthorizationDecision.fromJson("""
                {
                  "allowed": true,
                  "reason": "Allowed",
                  "message": "ok",
                  "serverId": null,
                  "velocityTarget": "lobby"
                }
                """);

        assertTrue(complete.hasSessionServerId());
        assertFalse(missing.hasSessionServerId());
    }
}
