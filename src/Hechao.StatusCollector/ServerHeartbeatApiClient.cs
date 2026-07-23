using System.Net.Http.Json;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.StatusCollector;

public sealed class ServerHeartbeatApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServerHeartbeatBatchResponse> SendAsync(
        Uri endpoint,
        string token,
        ServerHeartbeatBatchRequest heartbeat,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(heartbeat, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("X-Hechao-Heartbeat-Token", token);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 2048)
            {
                detail = detail[..2048];
            }

            throw new HttpRequestException(
                $"Heartbeat API returned {(int)response.StatusCode}: {detail}",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ServerHeartbeatBatchResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("Heartbeat API returned an empty response.");
    }
}
