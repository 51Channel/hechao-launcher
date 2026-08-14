package world.hechao.economy;

import java.io.IOException;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Objects;
import java.util.regex.Pattern;
import org.bukkit.configuration.file.FileConfiguration;

record EconomyConfiguration(
        URI apiBaseUri,
        String serverId,
        String token,
        Duration requestTimeout,
        Duration balanceCacheLifetime,
        java.math.BigDecimal payConfirmThreshold,
        int defaultPersonalDailyLimit,
        int defaultServerDailyLimit) {
    private static final Pattern SERVER_ID =
            Pattern.compile("^[a-z0-9][a-z0-9._-]{1,63}$");
    private static final Pattern TOKEN =
            Pattern.compile("^[A-Za-z0-9_-]{32,256}$");

    static EconomyConfiguration load(FileConfiguration config, Path serverRoot)
            throws IOException {
        Objects.requireNonNull(config, "config");
        var apiBaseUri = URI.create(required(config, "api-base-url"));
        if (!"https".equalsIgnoreCase(apiBaseUri.getScheme())
                || apiBaseUri.getHost() == null
                || apiBaseUri.getUserInfo() != null
                || apiBaseUri.getQuery() != null
                || apiBaseUri.getFragment() != null) {
            throw new IllegalArgumentException("api-base-url must be a plain HTTPS origin");
        }

        var serverId = required(config, "server-id");
        if (!SERVER_ID.matcher(serverId).matches()) {
            throw new IllegalArgumentException("server-id is invalid");
        }

        var environmentVariable = required(config, "token-environment-variable");
        var token = System.getenv(environmentVariable);
        if (token == null || token.isBlank()) {
            var configuredPath = Path.of(required(config, "token-file"));
            var tokenPath = configuredPath.isAbsolute()
                    ? configuredPath
                    : serverRoot.resolve(configuredPath).normalize();
            var normalizedRoot = serverRoot.toAbsolutePath().normalize();
            if (!tokenPath.toAbsolutePath().normalize().startsWith(normalizedRoot)) {
                throw new IllegalArgumentException("token-file must stay inside the server directory");
            }
            token = Files.exists(tokenPath)
                    ? Files.readString(tokenPath, StandardCharsets.US_ASCII).trim()
                    : "";
        } else {
            token = token.trim();
        }
        if (!token.isEmpty() && !TOKEN.matcher(token).matches()) {
            throw new IllegalArgumentException("economy token is invalid");
        }

        int requestTimeout = integer(config, "request-timeout-seconds", 1, 10);
        int cacheSeconds = integer(config, "balance-cache-seconds", 1, 60);
        var threshold = java.math.BigDecimal.valueOf(
                config.getDouble("pay-confirm-threshold", 10_000.0));
        if (threshold.signum() <= 0 || threshold.scale() > 2) {
            throw new IllegalArgumentException("pay-confirm-threshold is invalid");
        }
        int personalLimit = integer(
                config,
                "default-personal-daily-limit",
                1,
                1_000_000);
        int serverLimit = integer(
                config,
                "default-server-daily-limit",
                personalLimit,
                100_000_000);
        return new EconomyConfiguration(
                apiBaseUri,
                serverId,
                token,
                Duration.ofSeconds(requestTimeout),
                Duration.ofSeconds(cacheSeconds),
                threshold,
                personalLimit,
                serverLimit);
    }

    boolean isConfigured() {
        return !token.isBlank();
    }

    static EconomyConfiguration failClosedDefaults() {
        return new EconomyConfiguration(
                URI.create("https://launcher-api.hechao.world"),
                "skyrealm",
                "",
                Duration.ofSeconds(3),
                Duration.ofSeconds(15),
                java.math.BigDecimal.valueOf(10_000),
                2304,
                23040);
    }

    private static String required(FileConfiguration config, String key) {
        var value = config.getString(key);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(key + " is required");
        }
        return value.trim();
    }

    private static int integer(
            FileConfiguration config,
            String key,
            int minimum,
            int maximum) {
        int value = config.getInt(key, minimum - 1);
        if (value < minimum || value > maximum) {
            throw new IllegalArgumentException(
                    key + " must be between " + minimum + " and " + maximum);
        }
        return value;
    }
}
