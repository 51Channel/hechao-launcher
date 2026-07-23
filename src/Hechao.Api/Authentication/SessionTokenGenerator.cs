using System.Security.Cryptography;

namespace Hechao.Api.Authentication;

public sealed class SessionTokenGenerator
{
    private const int TokenBytes = 32;

    public SessionTokenPair Create()
    {
        return new SessionTokenPair(CreateToken(), CreateToken());
    }

    public static byte[] Hash(string token)
    {
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed record SessionTokenPair(string AccessToken, string RefreshToken);
