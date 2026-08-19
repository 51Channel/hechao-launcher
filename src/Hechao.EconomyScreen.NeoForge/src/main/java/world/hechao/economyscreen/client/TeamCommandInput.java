package world.hechao.economyscreen.client;

final class TeamCommandInput {
    private TeamCommandInput() {
    }

    static boolean acceptsPlayerName(String value) {
        return value != null && value.matches("[A-Za-z0-9_]{0,16}");
    }

    static boolean validPlayerName(String value) {
        return value != null && value.matches("[A-Za-z0-9_]{1,16}");
    }

    static boolean acceptsChat(String value) {
        return value != null && !value.contains("\n") && !value.contains("\r");
    }

    static String normalizedChat(String value) {
        if (!acceptsChat(value)) {
            return "";
        }
        return value.strip();
    }
}
