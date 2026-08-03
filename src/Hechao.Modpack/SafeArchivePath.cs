using System.Text.RegularExpressions;

namespace Hechao.Modpack;

public static partial class SafeArchivePath
{
    private static readonly HashSet<string> ReservedWindowsNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value, int maximumLength = 400)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var path = value.Replace('\\', '/').TrimStart('/');
        if (path.Length > maximumLength ||
            path.Contains('\0') ||
            path.Contains(':', StringComparison.Ordinal) ||
            RootedPath().IsMatch(value))
        {
            throw new InvalidDataException("Archive path is not a safe relative path.");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(IsUnsafeSegment))
        {
            throw new InvalidDataException("Archive path contains an unsafe segment.");
        }

        return string.Join('/', segments);
    }

    public static string GetContainedPath(string rootDirectory, string relativePath)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalized = Normalize(relativePath);
        var path = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Archive path escapes the destination directory.");
        }

        return path;
    }

    public static bool IsSymbolicLink(int externalAttributes)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (externalAttributes >> 16) & 0xFFFF;
        return (unixMode & UnixFileTypeMask) == UnixSymbolicLink;
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (segment is "." or ".." ||
            segment.EndsWith(' ') ||
            segment.EndsWith('.') ||
            segment.Any(character => character < 32) ||
            segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
        {
            return true;
        }

        var stem = segment.Split('.')[0];
        return ReservedWindowsNames.Contains(stem);
    }

    [GeneratedRegex("^(?:[A-Za-z]:[\\\\/]|\\\\\\\\|/)", RegexOptions.CultureInvariant)]
    private static partial Regex RootedPath();
}
