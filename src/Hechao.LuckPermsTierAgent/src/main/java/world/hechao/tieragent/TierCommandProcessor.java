package world.hechao.tieragent;

import java.util.Objects;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.Consumer;

final class TierCommandProcessor {
    private final TierCommandGateway gateway;
    private final TierMutationService mutationService;
    private final long mutationTimeoutSeconds;
    private final Consumer<String> warningSink;

    TierCommandProcessor(
            TierCommandGateway gateway,
            TierMutationService mutationService,
            long mutationTimeoutSeconds,
            Consumer<String> warningSink) {
        this.gateway = Objects.requireNonNull(gateway, "gateway");
        this.mutationService =
                Objects.requireNonNull(mutationService, "mutationService");
        this.mutationTimeoutSeconds = mutationTimeoutSeconds;
        this.warningSink = Objects.requireNonNull(warningSink, "warningSink");
    }

    void runOnce() {
        try {
            for (var command : gateway.claim()) {
                process(command);
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        } catch (Exception exception) {
            warningSink.accept(
                    "LuckPerms tier command poll failed: "
                            + safeErrorType(exception));
        }
    }

    private void process(TierCommand command) {
        TierMutationResult result;
        try {
            result = mutationService.apply(command)
                    .toCompletableFuture()
                    .get(mutationTimeoutSeconds, TimeUnit.SECONDS);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return;
        } catch (TimeoutException exception) {
            result = TierMutationResult.failed(
                    command.expectedPrimaryGroup(),
                    "luckperms_operation_timeout");
        } catch (Exception exception) {
            result = TierMutationResult.failed(
                    command.expectedPrimaryGroup(),
                    "luckperms_operation_failed");
        }

        try {
            gateway.complete(command, result);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        } catch (Exception exception) {
            // The API lease will expire. Reprocessing is idempotent because the
            // mutation service treats an already-applied target as success.
            warningSink.accept(
                    "LuckPerms tier command completion failed: "
                            + safeErrorType(exception));
        }
    }

    private static String safeErrorType(Exception exception) {
        return exception.getClass().getSimpleName();
    }
}
