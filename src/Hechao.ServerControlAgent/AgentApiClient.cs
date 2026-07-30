using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed class AgentApiClient(
    HttpClient httpClient,
    string agentId,
    string token)
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    internal async Task SendHeartbeatAsync(
        ServerControlAgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            "v1/internal/server-control/heartbeat");
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    internal async Task<ServerControlCommandClaimResponse> ClaimAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            "v1/internal/server-control/commands/claim");
        message.Content = JsonContent.Create(
            new ServerControlCommandClaimRequest(agentId, limit),
            options: JsonOptions);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<
                   ServerControlCommandClaimResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException(
                   "The server control claim response is empty.");
    }

    internal async Task CompleteAsync(
        Guid commandId,
        ServerControlCommandCompletionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            $"v1/internal/server-control/commands/{commandId:D}/complete");
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Hechao-Server-Control-Token", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Server control API returned {(int)response.StatusCode}: " +
            AgentLog.Sanitize(body, 500),
            inner: null,
            response.StatusCode);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
