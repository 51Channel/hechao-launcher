package world.hechao.tieragent;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.io.IOException;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.util.List;

final class TierCommandApiClient implements TierCommandGateway {
    private static final String TOKEN_HEADER = "X-Hechao-Sync-Token";

    private final TierAgentConfiguration configuration;
    private final HttpClient httpClient;
    private final Gson gson;

    TierCommandApiClient(TierAgentConfiguration configuration) {
        this(
                configuration,
                HttpClient.newBuilder()
                        .connectTimeout(configuration.requestTimeout())
                        .followRedirects(HttpClient.Redirect.NEVER)
                        .build());
    }

    TierCommandApiClient(
            TierAgentConfiguration configuration,
            HttpClient httpClient) {
        this.configuration = configuration;
        this.httpClient = httpClient;
        this.gson = new GsonBuilder().disableHtmlEscaping().create();
    }

    @Override
    public List<TierCommand> claim() throws IOException, InterruptedException {
        var body = gson.toJson(new ClaimRequest(
                configuration.agentId(),
                configuration.claimLimit()));
        var request = request(configuration.claimUri())
                .POST(HttpRequest.BodyPublishers.ofString(
                        body,
                        StandardCharsets.UTF_8))
                .build();
        var response = send(request);
        var claimResponse = gson.fromJson(response.body(), ClaimResponse.class);
        return claimResponse == null || claimResponse.commands() == null
                ? List.of()
                : List.copyOf(claimResponse.commands());
    }

    @Override
    public void complete(TierCommand command, TierMutationResult result)
            throws IOException, InterruptedException {
        var body = gson.toJson(new CompletionRequest(
                configuration.agentId(),
                command.attemptCount(),
                result.outcome(),
                result.observedPrimaryGroup(),
                result.failureCode()));
        var request = request(configuration.completionUri(command.commandId()))
                .POST(HttpRequest.BodyPublishers.ofString(
                        body,
                        StandardCharsets.UTF_8))
                .build();
        send(request);
    }

    private HttpRequest.Builder request(java.net.URI uri) {
        return HttpRequest.newBuilder(uri)
                .timeout(configuration.requestTimeout())
                .header("Accept", "application/json")
                .header("Content-Type", "application/json; charset=utf-8")
                .header(TOKEN_HEADER, configuration.token());
    }

    private HttpResponse<String> send(HttpRequest request)
            throws IOException, InterruptedException {
        var response = httpClient.send(
                request,
                HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            throw new IOException(
                    "tier command API returned HTTP " + response.statusCode());
        }
        return response;
    }

    private record ClaimRequest(String agentId, int limit) {
    }

    private record ClaimResponse(List<TierCommand> commands) {
    }

    private record CompletionRequest(
            String agentId,
            int attemptCount,
            String outcome,
            String observedPrimaryGroup,
            String failureCode) {
    }
}
