namespace Hechao.ServerControlAgent;

internal sealed class ServerTargetRegistry(
    IEnumerable<ServerTargetRuntime> initialTargets)
{
    private readonly object sync = new();
    private readonly List<ServerTargetRuntime> targets = [.. initialTargets];

    internal IReadOnlyList<ServerTargetRuntime> Snapshot()
    {
        lock (sync)
        {
            return [.. targets];
        }
    }

    internal ServerTargetRuntime? Find(string serverId)
    {
        lock (sync)
        {
            return targets.SingleOrDefault(target => string.Equals(
                target.Configuration.ServerId,
                serverId,
                StringComparison.Ordinal));
        }
    }

    internal bool TryAdd(ServerTargetRuntime target)
    {
        lock (sync)
        {
            if (targets.Any(existing => string.Equals(
                    existing.Configuration.ServerId,
                    target.Configuration.ServerId,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            targets.Add(target);
            return true;
        }
    }
}
