using System.Text;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal static class ServerPropertiesEditor
{
    private static readonly IReadOnlyDictionary<string, Func<ServerQuickSettings, string>>
        Values = new Dictionary<string, Func<ServerQuickSettings, string>>(
            StringComparer.Ordinal)
        {
            ["max-players"] = settings => settings.MaxPlayers.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["view-distance"] = settings => settings.ViewDistance.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["simulation-distance"] = settings =>
                settings.SimulationDistance.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ["difficulty"] = settings => settings.Difficulty,
            ["white-list"] = settings =>
                settings.WhiteList ? "true" : "false"
        };

    internal static ServerQuickSettings? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var values = Parse(File.ReadAllLines(path));
        return int.TryParse(values.GetValueOrDefault("max-players"), out var maxPlayers) &&
               int.TryParse(values.GetValueOrDefault("view-distance"), out var viewDistance) &&
               int.TryParse(
                   values.GetValueOrDefault("simulation-distance"),
                   out var simulationDistance) &&
               values.TryGetValue("difficulty", out var difficulty) &&
               bool.TryParse(values.GetValueOrDefault("white-list"), out var whiteList)
            ? new ServerQuickSettings(
                maxPlayers,
                viewDistance,
                simulationDistance,
                difficulty,
                whiteList)
            : null;
    }

    internal static void Apply(
        string path,
        string backupRoot,
        string serverId,
        ServerQuickSettings settings)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "server.properties does not exist.",
                path);
        }

        var lines = File.ReadAllLines(path).ToList();
        var updated = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var parsed = TryParseLine(lines[index]);
            if (parsed is null || !Values.TryGetValue(parsed.Value.Key, out var value))
            {
                continue;
            }

            lines[index] = $"{parsed.Value.Key}={value(settings)}";
            updated.Add(parsed.Value.Key);
        }

        foreach (var pair in Values.Where(pair => !updated.Contains(pair.Key)))
        {
            lines.Add($"{pair.Key}={pair.Value(settings)}");
        }

        var backupDirectory = Path.Combine(
            Path.GetFullPath(backupRoot),
            serverId,
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(backupDirectory);
        File.Copy(path, Path.Combine(backupDirectory, "server.properties"), overwrite: false);
        var temporary = path + $".hechao-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(
                temporary,
                lines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var parsed = TryParseLine(line);
            if (parsed is not null)
            {
                result[parsed.Value.Key] = parsed.Value.Value;
            }
        }

        return result;
    }

    private static KeyValuePair<string, string>? TryParseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] is '#' or '!')
        {
            return null;
        }

        var separator = trimmed.IndexOf('=');
        return separator <= 0
            ? null
            : new KeyValuePair<string, string>(
                trimmed[..separator].Trim(),
                trimmed[(separator + 1)..].Trim());
    }
}
