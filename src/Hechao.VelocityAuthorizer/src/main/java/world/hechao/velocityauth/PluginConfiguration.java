package world.hechao.velocityauth;

import java.io.IOException;
import java.io.Reader;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.time.Duration;
import java.util.Properties;
import java.util.regex.Pattern;

record PluginConfiguration(
        AuthorizationMode mode,
        URI apiUri,
        String token,
        String proxyInstance,
        Duration requestTimeout) {

    private static final Pattern PROXY_INSTANCE_PATTERN =
            Pattern.compile("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");
    private static final String DEFAULT_CONFIGURATION = """
            # disabled, monitor, or enforce
            mode=monitor
            api-url=https://launcher-api.hechao.world/v1/internal/velocity/authorize
            token=
            proxy-instance=owl5-main
            request-timeout-millis=2500
            """;

    static PluginConfiguration load(Path dataDirectory) throws IOException {
        Files.createDirectories(dataDirectory);
        Path configPath = dataDirectory.resolve("config.properties");
        if (Files.notExists(configPath)) {
            Files.writeString(
                    configPath,
                    DEFAULT_CONFIGURATION,
                    StandardCharsets.UTF_8,
                    StandardOpenOption.CREATE_NEW);
        }

        Properties properties = new Properties();
        try (Reader reader = Files.newBufferedReader(configPath, StandardCharsets.UTF_8)) {
            properties.load(reader);
        }

        AuthorizationMode mode = AuthorizationMode.parse(required(properties, "mode"));
        URI apiUri = URI.create(required(properties, "api-url"));
        validateApiUri(apiUri);

        String token = properties.getProperty("token", "").trim();
        String proxyInstance = required(properties, "proxy-instance");
        if (!PROXY_INSTANCE_PATTERN.matcher(proxyInstance).matches()) {
            throw new IllegalArgumentException("proxy-instance is invalid");
        }

        int timeoutMillis;
        try {
            timeoutMillis = Integer.parseInt(required(properties, "request-timeout-millis"));
        } catch (NumberFormatException exception) {
            throw new IllegalArgumentException("request-timeout-millis must be an integer", exception);
        }
        if (timeoutMillis < 500 || timeoutMillis > 10_000) {
            throw new IllegalArgumentException(
                    "request-timeout-millis must be between 500 and 10000");
        }

        return new PluginConfiguration(
                mode,
                apiUri,
                token,
                proxyInstance,
                Duration.ofMillis(timeoutMillis));
    }

    static AuthorizationMode readModeHint(Path dataDirectory) {
        Path configPath = dataDirectory.resolve("config.properties");
        if (Files.notExists(configPath)) {
            return AuthorizationMode.MONITOR;
        }

        Properties properties = new Properties();
        try (Reader reader = Files.newBufferedReader(configPath, StandardCharsets.UTF_8)) {
            properties.load(reader);
            return AuthorizationMode.parse(properties.getProperty("mode", "monitor"));
        } catch (Exception ignored) {
            return AuthorizationMode.MONITOR;
        }
    }

    boolean hasCredential() {
        return token.length() >= 24 && token.length() <= 256;
    }

    private static String required(Properties properties, String key) {
        String value = properties.getProperty(key);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(key + " is required");
        }
        return value.trim();
    }

    private static void validateApiUri(URI uri) {
        if (!uri.isAbsolute() || uri.getHost() == null || uri.getFragment() != null) {
            throw new IllegalArgumentException("api-url must be an absolute URL");
        }

        boolean loopback = uri.getHost().equalsIgnoreCase("localhost")
                || uri.getHost().equals("127.0.0.1")
                || uri.getHost().equals("::1");
        if (!uri.getScheme().equalsIgnoreCase("https")
                && !(loopback && uri.getScheme().equalsIgnoreCase("http"))) {
            throw new IllegalArgumentException(
                    "api-url must use HTTPS unless it points to loopback");
        }
    }
}
