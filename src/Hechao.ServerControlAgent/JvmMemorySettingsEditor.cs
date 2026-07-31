using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hechao.ServerControlAgent;

internal sealed record JvmMemorySettings(
    int InitialMemoryMiB,
    int MaximumMemoryMiB);

internal static partial class JvmMemorySettingsEditor
{
    private const int MinimumMemoryMiB = 512;
    private const int MemoryStepMiB = 256;

    internal static JvmMemorySettings? Read(
        string path,
        int maximumAllowedMemoryMiB)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = ReadBytePreservingText(path);
        var initialMatches = InitialMemoryRegex().Matches(text);
        var maximumMatches = MaximumMemoryRegex().Matches(text);
        if (initialMatches.Count != 1 || maximumMatches.Count != 1 ||
            !TryConvertToMiB(initialMatches[0], out var initialMemoryMiB) ||
            !TryConvertToMiB(maximumMatches[0], out var maximumMemoryMiB) ||
            !IsValid(initialMemoryMiB, maximumMemoryMiB, maximumAllowedMemoryMiB))
        {
            return null;
        }

        return new JvmMemorySettings(initialMemoryMiB, maximumMemoryMiB);
    }

    internal static void EnsureCanApply(
        string path,
        int initialMemoryMiB,
        int maximumMemoryMiB,
        int maximumAllowedMemoryMiB)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The JVM memory settings file does not exist.",
                path);
        }

        if (!IsValid(initialMemoryMiB, maximumMemoryMiB, maximumAllowedMemoryMiB))
        {
            throw new InvalidDataException(
                "The requested JVM memory settings are outside the managed limits.");
        }

        var text = ReadBytePreservingText(path);
        if (InitialMemoryRegex().Count(text) != 1 ||
            MaximumMemoryRegex().Count(text) != 1)
        {
            throw new InvalidDataException(
                "The JVM memory settings file must contain exactly one -Xms and one -Xmx argument.");
        }
    }

    internal static void Apply(
        string path,
        string backupRoot,
        string serverId,
        int initialMemoryMiB,
        int maximumMemoryMiB,
        int maximumAllowedMemoryMiB)
    {
        EnsureCanApply(
            path,
            initialMemoryMiB,
            maximumMemoryMiB,
            maximumAllowedMemoryMiB);

        var originalBytes = File.ReadAllBytes(path);
        var text = Encoding.Latin1.GetString(originalBytes);
        var updated = InitialMemoryRegex().Replace(
            text,
            $"-Xms{initialMemoryMiB.ToString(CultureInfo.InvariantCulture)}M",
            count: 1);
        updated = MaximumMemoryRegex().Replace(
            updated,
            $"-Xmx{maximumMemoryMiB.ToString(CultureInfo.InvariantCulture)}M",
            count: 1);

        var backupDirectory = Path.Combine(
            Path.GetFullPath(backupRoot),
            serverId,
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(backupDirectory);
        File.Copy(
            path,
            Path.Combine(backupDirectory, Path.GetFileName(path)),
            overwrite: false);

        var temporary = path + $".hechao-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, Encoding.Latin1.GetBytes(updated));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ReadBytePreservingText(string path) =>
        Encoding.Latin1.GetString(File.ReadAllBytes(path));

    private static bool IsValid(
        int initialMemoryMiB,
        int maximumMemoryMiB,
        int maximumAllowedMemoryMiB) =>
        maximumAllowedMemoryMiB is >= MinimumMemoryMiB and <= 65536 &&
        initialMemoryMiB is >= MinimumMemoryMiB and <= 65536 &&
        maximumMemoryMiB is >= MinimumMemoryMiB and <= 65536 &&
        initialMemoryMiB % MemoryStepMiB == 0 &&
        maximumMemoryMiB % MemoryStepMiB == 0 &&
        initialMemoryMiB <= maximumMemoryMiB &&
        maximumMemoryMiB <= maximumAllowedMemoryMiB;

    private static bool TryConvertToMiB(Match match, out int value)
    {
        value = 0;
        if (!int.TryParse(
                match.Groups["value"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return false;
        }

        try
        {
            value = char.ToUpperInvariant(match.Groups["unit"].Value[0]) switch
            {
                'K' when number % 1024 == 0 => number / 1024,
                'M' => number,
                'G' => checked(number * 1024),
                _ => 0
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        return value > 0;
    }

    [GeneratedRegex(
        @"(?<!\S)-Xms(?<value>[1-9][0-9]*)(?<unit>[KMG])(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InitialMemoryRegex();

    [GeneratedRegex(
        @"(?<!\S)-Xmx(?<value>[1-9][0-9]*)(?<unit>[KMG])(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaximumMemoryRegex();
}
