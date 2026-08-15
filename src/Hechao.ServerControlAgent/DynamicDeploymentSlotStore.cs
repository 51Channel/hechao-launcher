using System.Text.Json;

namespace Hechao.ServerControlAgent;

internal sealed class DynamicDeploymentSlotStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly object sync = new();
    private readonly ServerControlAgentConfiguration configuration;
    private readonly string path;
    private List<ServerControlTargetConfiguration> targets;

    internal DynamicDeploymentSlotStore(
        ServerControlAgentConfiguration configuration)
    {
        this.configuration = configuration;
        path = Path.Combine(
            configuration.StateDirectory,
            "dynamic-deployment-slots.json");
        targets = LoadCore();
        configuration.ValidateDynamicTargets(targets);
    }

    internal IReadOnlyList<ServerControlTargetConfiguration> Snapshot()
    {
        lock (sync)
        {
            return [.. targets];
        }
    }

    internal bool Contains(string serverId)
    {
        lock (sync)
        {
            return targets.Any(target => string.Equals(
                target.ServerId,
                serverId,
                StringComparison.Ordinal));
        }
    }

    internal void Add(ServerControlTargetConfiguration target)
    {
        lock (sync)
        {
            if (targets.Any(existing => string.Equals(
                    existing.ServerId,
                    target.ServerId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            var updated = targets.Append(target).ToArray();
            configuration.ValidateDynamicTargets(updated);
            Save(updated);
            targets = [.. updated];
        }
    }

    internal void Remove(string serverId)
    {
        lock (sync)
        {
            var updated = targets
                .Where(target => !string.Equals(
                    target.ServerId,
                    serverId,
                    StringComparison.Ordinal))
                .ToArray();
            if (updated.Length == targets.Count)
            {
                return;
            }

            configuration.ValidateDynamicTargets(updated);
            Save(updated);
            targets = [.. updated];
        }
    }

    private List<ServerControlTargetConfiguration> LoadCore()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The dynamic deployment slot store cannot be a reparse point.");
        }

        var file = new FileInfo(path);
        if (file.Length > 1024 * 1024)
        {
            throw new InvalidDataException(
                "The dynamic deployment slot store is too large.");
        }

        var document = JsonSerializer.Deserialize<SlotStoreDocument>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidDataException(
                "The dynamic deployment slot store is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Targets is null)
        {
            throw new InvalidDataException(
                "The dynamic deployment slot store schema is unsupported.");
        }

        return [.. document.Targets.Select(target => target.Normalize())];
    }

    private void Save(IReadOnlyList<ServerControlTargetConfiguration> updated)
    {
        Directory.CreateDirectory(configuration.StateDirectory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new SlotStoreDocument(SchemaVersion, updated),
                    JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record SlotStoreDocument(
        int SchemaVersion,
        IReadOnlyList<ServerControlTargetConfiguration> Targets);
}
