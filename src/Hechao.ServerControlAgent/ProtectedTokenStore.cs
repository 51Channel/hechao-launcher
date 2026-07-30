using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hechao.ServerControlAgent;

internal static partial class ProtectedTokenStore
{
    internal static string Read(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Length is < 1 or > 4096)
        {
            throw new InvalidDataException(
                "The protected server control token is missing or invalid.");
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
            if (!TokenPattern().IsMatch(token))
            {
                throw new InvalidDataException(
                    "The protected server control token is invalid.");
            }

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{32,256}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
