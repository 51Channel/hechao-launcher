package world.hechao.tieragent;

import java.io.IOException;
import java.io.Reader;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Objects;
import java.util.Properties;
import java.util.regex.Pattern;

record TierAgentConfiguration(
        URI apiBaseUri,
        String token,
        String agentId,
        Duration requestTimeout,
        Duration pollInterval,
        int claimLimit) {
    private static final Pattern SAFE_TOKEN =
            Pattern.compile("^[A-Za-z0-9_-]{32,256}$");
    private static final Pattern SAFE_AGENT_ID =
            Pattern.compile("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$");

    static TierAgentConfiguration load(Path path) throws IOException {
        Objects.requireNonNull(path, "path");
        var properties = new Properties();
        try (Reader reader = Files.newBufferedReader(path, StandardCharsets.UTF_8)) {
            properties.load(reader);
        }

        var apiBaseUri = URI.create(required(properties, "api-base-url"));
        if (!"https".equalsIgnoreCase(apiBaseUri.getScheme())
                || apiBaseUri.getHost() == null
                || apiBaseUri.getUserInfo() != null
                || apiBaseUri.getQuery() != null
                || apiBaseUri.getFragment() != null) {
            throw new IllegalArgumentException(
                    "api-base-url must be a plain HTTPS origin or base path");
        }

        var token = required(properties, "token");
        if (!SAFE_TOKEN.matcher(token).matches()) {
            throw new IllegalArgumentException("token is invalid");
        }

        var agentId = required(properties, "agent-id");
        if (!SAFE_AGENT_ID.matcher(agentId).matches()) {
            throw new IllegalArgumentException("agent-id is invalid");
        }

        int timeoutSeconds = integer(properties, "request-timeout-seconds", 1, 30);
        int pollSeconds = integer(properties, "poll-interval-seconds", 5, 300);
        int claimLimit = integer(properties, "claim-limit", 1, 20);
        return new TierAgentConfiguration(
                apiBaseUri,
                token,
                agentId,
                Duration.ofSeconds(timeoutSeconds),
                Duration.ofSeconds(pollSeconds),
                claimLimit);
    }

    URI claimUri() {
        return apiBaseUri.resolve(
                "/v1/internal/luckperms/tier-commands/claim");
    }

    URI completionUri(java.util.UUID commandId) {
        return apiBaseUri.resolve(
                "/v1/internal/luckperms/tier-commands/"
                        + commandId
                        + "/complete");
    }

    private static String required(Properties properties, String key) {
        var value = properties.getProperty(key);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(key + " is required");
        }
        return value.trim();
    }

    private static int integer(
            Properties properties,
            String key,
            int minimum,
            int maximum) {
        int value;
        try {
            value = Integer.parseInt(required(properties, key));
        } catch (NumberFormatException exception) {
            throw new IllegalArgumentException(key + " must be an integer", exception);
        }
        if (value < minimum || value > maximum) {
            throw new IllegalArgumentException(
                    key + " must be between " + minimum + " and " + maximum);
        }
        return value;
    }
}
