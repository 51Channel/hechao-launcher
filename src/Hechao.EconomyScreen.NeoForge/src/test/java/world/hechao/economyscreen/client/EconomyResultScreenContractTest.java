package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class EconomyResultScreenContractTest {
    private static final Path SOURCE = Path.of(
            "src", "main", "java", "world", "hechao", "economyscreen",
            "client", "EconomyResultScreen.java");

    @Test
    void failedEconomyRequestsOfferInPlaceRetryAndHomeRecovery()
            throws Exception {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("Component.literal(\"重试\")"));
        assertTrue(source.contains("|| retryButton == null"));
        assertTrue(source.contains("state.tone() == EconomyResultState.Tone.ERROR"));
        assertTrue(source.contains("state.begin(retryLoadingMessage())"));
        assertTrue(source.contains("ClientEconomyUiBridge.requestHome()"));
        assertTrue(source.contains("hechaoeconomy:money"));
        assertTrue(source.contains("hechaoeconomy:shop"));
        assertTrue(source.contains("hechaoeconomy:ah sell"));
        assertTrue(source.contains("hechaoeconomy:ah"));
    }
}
