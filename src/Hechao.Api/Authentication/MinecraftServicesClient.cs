using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hechao.Api.Authentication;

public sealed partial class MinecraftServicesClient(HttpClient httpClient)
{
    private const int MaximumTokenLength = 16 * 1024;

    public async Task<VerifiedMinecraftIdentity> VerifyAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(minecraftAccessToken) ||
            minecraftAccessToken.Length > MaximumTokenLength ||
            minecraftAccessToken.Any(char.IsWhiteSpace))
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidToken);
        }

        using var entitlementDocument = await GetJsonAsync(
            "entitlements/mcstore",
            minecraftAccessToken,
            MinecraftEndpoint.Entitlements,
            cancellationToken);

        if (!entitlementDocument.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.NoJavaEntitlement);
        }

        using var profileDocument = await GetJsonAsync(
            "minecraft/profile",
            minecraftAccessToken,
            MinecraftEndpoint.Profile,
            cancellationToken);

        var root = profileDocument.RootElement;
        if (!root.TryGetProperty("id", out var idProperty) ||
            !root.TryGetProperty("name", out var nameProperty))
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidResponse);
        }

        var rawId = idProperty.GetString();
        var name = nameProperty.GetString();
        if (!Guid.TryParseExact(rawId, "N", out var minecraftUuid) ||
            string.IsNullOrWhiteSpace(name) ||
            !MinecraftNameRegex().IsMatch(name))
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidResponse);
        }

        return new VerifiedMinecraftIdentity(minecraftUuid, name);
    }

    private async Task<JsonDocument> GetJsonAsync(
        string relativeUri,
        string accessToken,
        MinecraftEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new MinecraftVerificationException(MinecraftVerificationFailure.ServiceUnavailable);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidToken);
            }

            if (response.StatusCode == HttpStatusCode.NotFound && endpoint == MinecraftEndpoint.Profile)
            {
                throw new MinecraftVerificationException(MinecraftVerificationFailure.NoJavaProfile);
            }

            if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                throw new MinecraftVerificationException(MinecraftVerificationFailure.ServiceUnavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidResponse);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                throw new MinecraftVerificationException(MinecraftVerificationFailure.InvalidResponse);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex MinecraftNameRegex();

    private enum MinecraftEndpoint
    {
        Entitlements,
        Profile
    }
}

public sealed record VerifiedMinecraftIdentity(Guid MinecraftUuid, string MinecraftName);

public enum MinecraftVerificationFailure
{
    InvalidToken,
    NoJavaEntitlement,
    NoJavaProfile,
    InvalidResponse,
    ServiceUnavailable
}

public sealed class MinecraftVerificationException(MinecraftVerificationFailure failure) : Exception
{
    public MinecraftVerificationFailure Failure { get; } = failure;
}
