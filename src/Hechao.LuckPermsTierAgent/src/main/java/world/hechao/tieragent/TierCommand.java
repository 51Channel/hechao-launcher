package world.hechao.tieragent;

import java.util.UUID;

record TierCommand(
        UUID commandId,
        UUID minecraftUuid,
        String expectedPrimaryGroup,
        String targetPrimaryGroup,
        String targetAccessTier,
        int attemptCount) {
}
