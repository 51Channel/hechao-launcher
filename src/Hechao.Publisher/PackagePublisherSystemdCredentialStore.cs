using System.Security.Cryptography;
using System.Text;

internal static class PackagePublisherSystemdCredentialStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string ReadToken(string path)
    {
        var bytes = ReadBytes(path, 4096, "publisher token");
        try
        {
            var length = bytes.Length;
            if (length > 0 && bytes[length - 1] == (byte)'\n')
            {
                length--;
                if (length > 0 && bytes[length - 1] == (byte)'\r')
                {
                    length--;
                }
            }

            var token = StrictUtf8.GetString(bytes, 0, length);
            if (!PackagePublisherProtectedTokenStore.IsValidToken(token))
            {
                throw new InvalidDataException(
                    "The systemd package publisher token is invalid.");
            }

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static byte[] ReadBytes(
        string path,
        int maximumBytes,
        string description)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        if (!file.Exists ||
            file.Length is <= 0 ||
            file.Length > maximumBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException(
                $"The systemd {description} file is missing or invalid.");
        }

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode forbidden =
                UnixFileMode.GroupRead |
                UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite |
                UnixFileMode.OtherExecute;
            if ((mode & forbidden) != 0)
            {
                throw new PublisherUsageException(
                    $"The systemd {description} file permissions are too broad.");
            }
        }

        return File.ReadAllBytes(path);
    }
}
