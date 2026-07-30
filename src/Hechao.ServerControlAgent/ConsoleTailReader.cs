using System.Text;

namespace Hechao.ServerControlAgent;

internal static class ConsoleTailReader
{
    private const int MaximumReadBytes = 256 * 1024;
    private const int MaximumLines = 160;
    private const int MaximumCharacters = 65536;

    internal static string Read(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var truncated = stream.Length > MaximumReadBytes;
            var bytesToRead = (int)Math.Min(stream.Length, MaximumReadBytes);
            stream.Position = stream.Length - bytesToRead;
            var bytes = new byte[bytesToRead];
            stream.ReadExactly(bytes);
            var text = Encoding.UTF8.GetString(bytes);
            if (truncated)
            {
                var newline = text.IndexOf('\n');
                text = newline >= 0 ? text[(newline + 1)..] : string.Empty;
            }

            var lines = text
                .Split('\n')
                .TakeLast(MaximumLines)
                .Select(line => AgentLog.Sanitize(line.TrimEnd('\r'), 2000));
            var result = string.Join(Environment.NewLine, lines);
            return result.Length <= MaximumCharacters
                ? result
                : result[^MaximumCharacters..];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
