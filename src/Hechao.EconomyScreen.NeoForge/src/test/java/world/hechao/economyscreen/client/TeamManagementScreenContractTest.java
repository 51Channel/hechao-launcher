package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class TeamManagementScreenContractTest {
    private static final Path SOURCE = Path.of(
            "src", "main", "java", "world", "hechao", "economyscreen",
            "client", "TeamManagementScreen.java");

    @Test
    void exposesEveryCommandSupportedBySkyrealmCore() throws IOException {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("invite \" + player"));
        assertTrue(source.contains("execute(\"accept\""));
        assertTrue(source.contains("execute(\"leave\""));
        assertTrue(source.contains("kick \" + player"));
        assertTrue(source.contains("execute(\"list\""));
        assertTrue(source.contains("chat \" + message"));
        assertTrue(source.contains("skyrealmcore:team "));
    }

    @Test
    void destructiveMemberActionsRequireConfirmation() throws IOException {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("kickConfirmTicks"));
        assertTrue(source.contains("leaveConfirmTicks"));
        assertTrue(source.contains("确认移出"));
        assertTrue(source.contains("确认离队"));
    }
}
