package world.hechao.economy.api;

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import com.google.gson.reflect.TypeToken;
import java.io.IOException;
import java.math.BigDecimal;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.List;
import java.util.UUID;
import world.hechao.economy.EconomyConfigurationView;

public final class HttpEconomyGateway implements EconomyGateway {
    private static final Gson GSON = new Gson();

    private final EconomyConfigurationView configuration;
    private final HttpClient client;

    public HttpEconomyGateway(EconomyConfigurationView configuration) {
        this.configuration = configuration;
        this.client = HttpClient.newBuilder()
                .connectTimeout(configuration.requestTimeout())
                .followRedirects(HttpClient.Redirect.NEVER)
                .build();
    }

    @Override
    public Balance balance(UUID playerUuid) throws EconomyGatewayException {
        var request = request("/v1/internal/economy/accounts/" + playerUuid)
                .GET()
                .build();
        return send(request, Balance.class, false);
    }

    @Override
    public Transfer transfer(
            String idempotencyKey,
            UUID senderUuid,
            UUID recipientUuid,
            BigDecimal amount,
            String note) throws EconomyGatewayException {
        var body = new TransferRequest(
                idempotencyKey,
                senderUuid,
                recipientUuid,
                amount,
                note);
        var request = jsonRequest("/v1/internal/economy/transfers", body)
                .POST(HttpRequest.BodyPublishers.ofString(GSON.toJson(body)))
                .build();
        return sendWriteWithSingleRetry(request, Transfer.class);
    }

    @Override
    public SaleQuote quote(UUID playerUuid, String itemId, int quantity)
            throws EconomyGatewayException {
        var body = new QuoteRequest(playerUuid, itemId, quantity);
        var request = jsonRequest("/v1/internal/economy/sales/quotes", body)
                .POST(HttpRequest.BodyPublishers.ofString(GSON.toJson(body)))
                .build();
        var response = send(request, QuoteResponse.class, false);
        return new SaleQuote(
                response.quoteId,
                response.playerUuid,
                response.itemId,
                response.quantity,
                response.unitPrice,
                response.totalAmount,
                response.personalRemaining,
                response.serverRemaining,
                Instant.parse(response.expiresAt));
    }

    @Override
    public SaleCommit commit(String idempotencyKey, UUID quoteId, UUID playerUuid)
            throws EconomyGatewayException {
        var body = new CommitRequest(idempotencyKey, quoteId, playerUuid);
        var request = jsonRequest("/v1/internal/economy/sales/commit", body)
                .POST(HttpRequest.BodyPublishers.ofString(GSON.toJson(body)))
                .build();
        return sendWriteWithSingleRetry(request, SaleCommit.class);
    }

    @Override
    public List<Product> products(boolean includeDisabled) throws EconomyGatewayException {
        var request = request(
                "/v1/internal/economy/products?includeDisabled=" + includeDisabled)
                .GET()
                .build();
        return send(
                request,
                new TypeToken<List<Product>>() { }.getType(),
                false);
    }

    @Override
    public Product upsertProduct(
            String itemId,
            BigDecimal unitPrice,
            int personalDailyLimit,
            int serverDailyLimit,
            UUID actorUuid,
            String actorName) throws EconomyGatewayException {
        var body = new ProductUpsertRequest(
                unitPrice,
                personalDailyLimit,
                serverDailyLimit,
                actorUuid,
                actorName);
        var request = jsonRequest(
                        "/v1/internal/economy/products?itemId=" + queryValue(itemId),
                        body)
                .PUT(HttpRequest.BodyPublishers.ofString(GSON.toJson(body)))
                .build();
        return send(request, Product.class, false);
    }

    @Override
    public void disableProduct(String itemId, UUID actorUuid, String actorName)
            throws EconomyGatewayException {
        var body = new ProductDisableRequest(actorUuid, actorName);
        var request = jsonRequest(
                        "/v1/internal/economy/products/disable?itemId="
                                + queryValue(itemId),
                        body)
                .POST(HttpRequest.BodyPublishers.ofString(GSON.toJson(body)))
                .build();
        sendNoContent(request);
    }

    @Override
    public boolean isConfigured() {
        return configuration.configured();
    }

    private HttpRequest.Builder request(String path) {
        return HttpRequest.newBuilder(configuration.apiBaseUri().resolve(path))
                .timeout(configuration.requestTimeout())
                .header("Authorization", "Bearer " + configuration.token())
                .header("X-Hechao-Server-Id", configuration.serverId())
                .header("Accept", "application/json")
                .header("User-Agent", "HechaoEconomy/0.1.5");
    }

    private HttpRequest.Builder jsonRequest(String path, Object ignoredBody) {
        return request(path).header("Content-Type", "application/json; charset=utf-8");
    }

    private <T> T send(HttpRequest request, Class<T> type, boolean allowConflict)
            throws EconomyGatewayException {
        return send(request, (java.lang.reflect.Type) type, allowConflict);
    }

    private <T> T sendWriteWithSingleRetry(
            HttpRequest request,
            Class<T> type) throws EconomyGatewayException {
        return retryOutcomeUnknown(() -> send(request, type, true));
    }

    static <T> T retryOutcomeUnknown(CheckedCall<T> call)
            throws EconomyGatewayException {
        EconomyGatewayException firstFailure;
        try {
            return call.run();
        } catch (EconomyGatewayException exception) {
            if (!exception.isOutcomeUnknown()) {
                throw exception;
            }
            firstFailure = exception;
        }

        try {
            return call.run();
        } catch (EconomyGatewayException retryFailure) {
            retryFailure.addSuppressed(firstFailure);
            throw retryFailure;
        }
    }

    @FunctionalInterface
    interface CheckedCall<T> {
        T run() throws EconomyGatewayException;
    }

    private <T> T send(
            HttpRequest request,
            java.lang.reflect.Type type,
            boolean allowConflict) throws EconomyGatewayException {
        HttpResponse<String> response;
        try {
            response = client.send(
                    request,
                    HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new EconomyGatewayException(
                    "economy request was interrupted",
                    exception,
                    true);
        } catch (IOException exception) {
            throw new EconomyGatewayException(
                    "economy service is unavailable",
                    exception,
                    true);
        }

        var status = response.statusCode();
        if (status >= 200 && status < 300) {
            return parse(response.body(), type, status);
        }
        if (allowConflict && status == 409 && hasOperationId(response.body())) {
            return parse(response.body(), type, status);
        }
        throw new EconomyGatewayException(
                "economy service returned HTTP " + status,
                false,
                status);
    }

    private void sendNoContent(HttpRequest request) throws EconomyGatewayException {
        HttpResponse<Void> response;
        try {
            response = client.send(request, HttpResponse.BodyHandlers.discarding());
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new EconomyGatewayException(
                    "economy request was interrupted",
                    exception,
                    true);
        } catch (IOException exception) {
            throw new EconomyGatewayException(
                    "economy service is unavailable",
                    exception,
                    true);
        }
        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            throw new EconomyGatewayException(
                    "economy service returned HTTP " + response.statusCode(),
                    false,
                    response.statusCode());
        }
    }

    private static <T> T parse(String json, java.lang.reflect.Type type, int status)
            throws EconomyGatewayException {
        try {
            @SuppressWarnings("unchecked")
            var value = (T) GSON.fromJson(json, type);
            if (value == null) {
                throw new IllegalArgumentException("empty JSON response");
            }
            return value;
        } catch (RuntimeException exception) {
            throw new EconomyGatewayException(
                    "economy service returned invalid JSON after HTTP " + status,
                    exception,
                    true);
        }
    }

    private static boolean hasOperationId(String json) {
        try {
            JsonObject object = GSON.fromJson(json, JsonObject.class);
            return object != null && object.has("operationId");
        } catch (RuntimeException ignored) {
            return false;
        }
    }

    static String queryValue(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    private record TransferRequest(
            String idempotencyKey,
            UUID senderUuid,
            UUID recipientUuid,
            BigDecimal amount,
            String note) {
    }

    private record QuoteRequest(UUID playerUuid, String itemId, int quantity) {
    }

    private record QuoteResponse(
            UUID quoteId,
            UUID playerUuid,
            String itemId,
            int quantity,
            BigDecimal unitPrice,
            BigDecimal totalAmount,
            int personalRemaining,
            int serverRemaining,
            String expiresAt) {
    }

    private record CommitRequest(String idempotencyKey, UUID quoteId, UUID playerUuid) {
    }

    private record ProductUpsertRequest(
            BigDecimal unitPrice,
            int personalDailyLimit,
            int serverDailyLimit,
            UUID actorUuid,
            String actorName) {
    }

    private record ProductDisableRequest(UUID actorUuid, String actorName) {
    }
}
