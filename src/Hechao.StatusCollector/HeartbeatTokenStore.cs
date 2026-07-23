using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hechao.StatusCollector;

public static class HeartbeatTokenStore
{
    private static readonly Regex TokenPattern = new(
        "^[A-Za-z0-9_-]{32,256}$",
        RegexOptions.CultureInvariant);

    public static string Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > 4096)
        {
            throw new InvalidDataException("The protected heartbeat token file is missing or invalid.");
        }

        var protectedBytes = File.ReadAllBytes(file.FullName);
        byte[] clearBytes;
        try
        {
            clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.LocalMachine);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }

        try
        {
            var token = Encoding.UTF8.GetString(clearBytes);
            if (!TokenPattern.IsMatch(token))
            {
                throw new InvalidDataException("The protected heartbeat token is invalid.");
            }

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}
