using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal static partial class PackagePublisherProtectedTokenStore
{
    internal static void Protect(string token, string outputPath)
    {
        if (!OperatingSystem.IsWindows() || !IsValidToken(token))
        {
            throw new PublisherUsageException(
                "The package publisher token is invalid or DPAPI is unavailable.");
        }

        var path = Path.GetFullPath(outputPath);
        if (File.Exists(path))
        {
            throw new PublisherUsageException(
                $"Refusing to overwrite an existing token file: {path}");
        }

        var clearBytes = Encoding.UTF8.GetBytes(token);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(
                clearBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    internal static string Read(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows() ||
            !file.Exists || file.Length is < 1 or > 4096 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The protected package publisher token is missing or invalid.");
        }

        var protectedBytes = File.ReadAllBytes(file.FullName);
        byte[]? clearBytes = null;
        try
        {
            clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(clearBytes);
            if (!IsValidToken(token))
            {
                throw new InvalidDataException(
                    "The protected package publisher token is invalid.");
            }

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (clearBytes is not null)
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
    }

    internal static bool IsValidToken(string token) =>
        TokenPattern().IsMatch(token);

    [GeneratedRegex("^[A-Za-z0-9_-]{32,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
