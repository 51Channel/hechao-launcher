using Hechao.Contracts;

namespace Hechao.Launcher.Services;

public sealed class DemoServerCatalogClient : IServerCatalogClient
{
    public Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServerSummary> servers =
        [
            new("lobby", "大厅", "厅", "⌂", ServerStatus.Online, 42, 300, "1.21.11", ModLoaderKind.Paper, AccessTier.Member, "base-1.21.11"),
            new("survival2", "天域生存", "域", "山", ServerStatus.Online, 18, 100, "1.21.11", ModLoaderKind.Paper, AccessTier.Member, "base-1.21.11"),
            new("activity", "海绵小镇躲猫猫", "海", "海", ServerStatus.Online, 21, 30, "1.21.11", ModLoaderKind.NeoForge, AccessTier.Participant, "activity-neoforge-1.21.11"),
            new("dollnight", "玩偶惊魂夜", "偶", "偶", ServerStatus.Maintenance, 0, 30, "1.21.11", ModLoaderKind.Paper, AccessTier.Participant, "dollnight-1.21.11")
        ];

        IReadOnlyList<ClientProfileSummary> profiles =
        [
            new("base-1.21.11", "基础客户端", "1.0.4", 48_234_102, string.Empty, DateTimeOffset.UtcNow),
            new("activity-neoforge-1.21.11", "活动服模组包", "1.0.9", 132_120_576, string.Empty, DateTimeOffset.UtcNow),
            new("dollnight-1.21.11", "玩偶惊魂夜资源", "0.6.2", 82_575_360, string.Empty, DateTimeOffset.UtcNow)
        ];

        return Task.FromResult(new LauncherCatalogSnapshot(DateTimeOffset.UtcNow, servers, profiles));
    }
}
