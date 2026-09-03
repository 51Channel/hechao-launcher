using System.Text;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal static class ServerPropertiesEditor
{
    private static readonly IReadOnlyDictionary<string, int> LegacyDifficultyIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["peaceful"] = 0,
            ["easy"] = 1,
            ["normal"] = 2,
            ["hard"] = 3
        };

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

        var values = Parse(SharedFileReader.ReadAllLines(path));
        if (!int.TryParse(values.GetValueOrDefault("max-players"), out var maxPlayers) ||
            !int.TryParse(values.GetValueOrDefault("view-distance"), out var viewDistance) ||
            !TryReadDifficulty(
                values.GetValueOrDefault("difficulty"),
                out var difficulty,
                out var legacyDifficultyFormat) ||
            !bool.TryParse(values.GetValueOrDefault("white-list"), out var whiteList))
        {
            return null;
        }

        var simulationDistance = viewDistance;
        if (values.TryGetValue("simulation-distance", out var simulationDistanceValue))
        {
            if (!int.TryParse(simulationDistanceValue, out simulationDistance))
            {
                return null;
            }
        }
        else if (!legacyDifficultyFormat)
        {
            return null;
        }

        return new ServerQuickSettings(
            maxPlayers,
            viewDistance,
            simulationDistance,
            difficulty,
            whiteList);
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

        var lines = SharedFileReader.ReadAllLines(path).ToList();
        var originalValues = Parse(lines);
        _ = TryReadDifficulty(
            originalValues.GetValueOrDefault("difficulty"),
            out _,
            out var legacyDifficultyFormat);
        var supportsSimulationDistance =
            originalValues.ContainsKey("simulation-distance") ||
            !legacyDifficultyFormat;
        var updated = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var parsed = TryParseLine(lines[index]);
            if (parsed is null || !Values.TryGetValue(parsed.Value.Key, out var value))
            {
                continue;
            }

            var formatted = parsed.Value.Key == "difficulty" && legacyDifficultyFormat
                ? LegacyDifficultyIds[settings.Difficulty].ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : value(settings);
            lines[index] = $"{parsed.Value.Key}={formatted}";
            updated.Add(parsed.Value.Key);
        }

        foreach (var pair in Values.Where(pair =>
                     !updated.Contains(pair.Key) &&
                     (pair.Key != "simulation-distance" || supportsSimulationDistance)))
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

    internal static void ApplyDeploymentBinding(string path, int port)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "server.properties does not exist.",
                path);
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var protectedValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["server-ip"] = "127.0.0.1",
            ["server-port"] = port.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["online-mode"] = "false"
        };
        var lines = SharedFileReader.ReadAllLines(path).ToList();
        var updated = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var parsed = TryParseLine(lines[index]);
            if (parsed is null ||
                !protectedValues.TryGetValue(parsed.Value.Key, out var value))
            {
                continue;
            }

            lines[index] = $"{parsed.Value.Key}={value}";
            updated.Add(parsed.Value.Key);
        }

        foreach (var pair in protectedValues.Where(pair => !updated.Contains(pair.Key)))
        {
            lines.Add($"{pair.Key}={pair.Value}");
        }

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

    private static bool TryReadDifficulty(
        string? value,
        out string difficulty,
        out bool legacyFormat)
    {
        difficulty = string.Empty;
        legacyFormat = false;
        if (value is null)
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (LegacyDifficultyIds.ContainsKey(normalized))
        {
            difficulty = normalized;
            return true;
        }

        if (!int.TryParse(normalized, out var legacyId))
        {
            return false;
        }

        difficulty = LegacyDifficultyIds
            .SingleOrDefault(pair => pair.Value == legacyId)
            .Key ?? string.Empty;
        legacyFormat = difficulty.Length > 0;
        return legacyFormat;
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
