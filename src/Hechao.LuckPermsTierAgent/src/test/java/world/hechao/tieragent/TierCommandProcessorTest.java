package world.hechao.tieragent;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;

class TierCommandProcessorTest {
    @Test
    void completesEveryClaimedCommandWithMutationResult() {
        var first = command("default", "vip");
        var second = command("vip", "admin");
        var gateway = new FakeGateway(List.of(first, second));
        var warnings = new ArrayList<String>();
        var processor = new TierCommandProcessor(
                gateway,
                command -> CompletableFuture.completedFuture(
                        TierMutationResult.applied(
                                command.targetPrimaryGroup())),
                5,
                warnings::add);

        processor.runOnce();

        assertEquals(List.of(first, second), gateway.completed);
        assertTrue(warnings.isEmpty());
    }

    @Test
    void leavesCompletionForRetryWhenGatewayFails() {
        var command = command("default", "vip");
        var gateway = new FakeGateway(List.of(command));
        gateway.failCompletion = true;
        var warnings = new ArrayList<String>();
        var processor = new TierCommandProcessor(
                gateway,
                ignored -> CompletableFuture.completedFuture(
                        TierMutationResult.applied("vip")),
                5,
                warnings::add);

        processor.runOnce();

        assertTrue(gateway.completed.isEmpty());
        assertEquals(1, warnings.size());
    }

    private static TierCommand command(String expected, String target) {
        return new TierCommand(
                UUID.randomUUID(),
                UUID.randomUUID(),
                expected,
                target,
                "Participant",
                1);
    }

    private static final class FakeGateway implements TierCommandGateway {
        private final List<TierCommand> claimed;
        private final List<TierCommand> completed = new ArrayList<>();
        private boolean failCompletion;

        private FakeGateway(List<TierCommand> claimed) {
            this.claimed = claimed;
        }

        @Override
        public List<TierCommand> claim() {
            return claimed;
        }

        @Override
        public void complete(TierCommand command, TierMutationResult result)
                throws java.io.IOException {
            if (failCompletion) {
                throw new java.io.IOException("simulated");
            }
            completed.add(command);
        }
    }
}
