package world.hechao.velocityauth;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;

final class AuthorizationApiClient {
    private final HttpClient httpClient;
    private final URI endpoint;
    private final String token;
    private final String proxyInstance;
    private final Duration requestTimeout;

    AuthorizationApiClient(PluginConfiguration configuration) {
        this.endpoint = configuration.apiUri();
        this.token = configuration.token();
        this.proxyInstance = configuration.proxyInstance();
        this.requestTimeout = configuration.requestTimeout();
        this.httpClient = HttpClient.newBuilder()
                .connectTimeout(configuration.requestTimeout())
                .followRedirects(HttpClient.Redirect.NEVER)
                .build();
    }

    CompletableFuture<AuthorizationDecision> authorize(
            UUID minecraftUuid,
            String minecraftName,
            String velocityTarget,
            boolean initialConnection,
            String remoteAddress) {
        String body = "{"
                + "\"minecraftUuid\":" + JsonStrings.quote(minecraftUuid.toString()) + ","
                + "\"minecraftName\":" + JsonStrings.quote(minecraftName) + ","
                + "\"velocityTarget\":" + JsonStrings.quote(velocityTarget) + ","
                + "\"initialConnection\":" + initialConnection + ","
                + "\"remoteAddress\":" + nullableString(remoteAddress) + ","
                + "\"proxyInstance\":" + JsonStrings.quote(proxyInstance)
                + "}";

        HttpRequest request = HttpRequest.newBuilder(endpoint)
                .timeout(requestTimeout)
                .header("Accept", "application/json")
                .header("Content-Type", "application/json; charset=utf-8")
                .header("User-Agent", "HechaoVelocityAuthorizer/0.1.0")
                .header("X-Hechao-Velocity-Token", token)
                .POST(HttpRequest.BodyPublishers.ofString(body, StandardCharsets.UTF_8))
                .build();

        return httpClient.sendAsync(
                        request,
                        HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8))
                .thenApply(response -> {
                    if (response.statusCode() != 200) {
                        throw new AuthorizationApiException(
                                "Authorization API returned HTTP " + response.statusCode());
                    }
                    return AuthorizationDecision.fromJson(response.body());
                });
    }

    private static String nullableString(String value) {
        return value == null ? "null" : JsonStrings.quote(value);
    }
}

final class AuthorizationApiException extends RuntimeException {
    AuthorizationApiException(String message) {
        super(message);
    }
}
