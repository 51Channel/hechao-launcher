package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class RtpSafetyPolicyTest {
    @Test
    void acceptsACompleteTwoBlockLandingSpace() {
        assertTrue(RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                true, true, true, true, true, true, true)));
    }

    @Test
    void rejectsBedrockOrOtherHazardousLandingSurfaces() {
        assertFalse(RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                true, true, true, true, true, false, true)));
    }

    @Test
    void rejectsFluidsAndBlockedPlayerSpace() {
        assertFalse(RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                true, true, true, true, false, true, true)));
        assertFalse(RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                true, true, true, false, true, true, true)));
        assertFalse(RtpSafetyPolicy.accepts(new RtpSafetyPolicy.Surface(
                true, true, true, true, true, true, false)));
    }
}
