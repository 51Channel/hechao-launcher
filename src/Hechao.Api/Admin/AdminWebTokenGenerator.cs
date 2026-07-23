using System.Security.Cryptography;
using System.Text;

namespace Hechao.Api.Admin;

public sealed class AdminWebTokenGenerator
{
    private const int TokenBytes = 32;

    public string Create()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public static bool IsShapeValid(string? token)
    {
        return token is { Length: 43 } && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}
