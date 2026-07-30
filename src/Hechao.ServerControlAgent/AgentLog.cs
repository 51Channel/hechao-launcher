using System.Text;

namespace Hechao.ServerControlAgent;

internal sealed class AgentLog(string path)
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly object _gate = new();

    internal void Write(string level, string eventName, string message)
    {
        var safeMessage = Sanitize(message, 1600);
        var line =
            $"{DateTimeOffset.UtcNow:O}\t{level}\t{eventName}\t{safeMessage}{Environment.NewLine}";
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            RotateIfNeeded();
            File.AppendAllText(_path, line, new UTF8Encoding(false));
        }
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < 5 * 1024 * 1024)
        {
            return;
        }

        var previous = _path + ".1";
        File.Delete(previous);
        File.Move(_path, previous);
    }

    internal static string Sanitize(string value, int maximumLength)
    {
        var sanitized = new string(value
            .Where(character =>
                character is '\t' ||
                (!char.IsControl(character) && character != '\u007f'))
            .ToArray());
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
