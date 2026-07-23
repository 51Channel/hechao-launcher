using Hechao.Api.Monitoring;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerHeartbeatRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_AcceptsOnlineAndOfflineTargets()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now.AddSeconds(-10),
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat("lobby", true, 12, 300, "Paper 1.21.11", 774),
                new VelocityTargetHeartbeat("activity", false, 0, 0, null, null)
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsDuplicateTargets()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat("survival2", true, 4, 100, null, 774),
                new VelocityTargetHeartbeat("survival2", false, 0, 0, null, null)
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("servers", errors);
    }

    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 1, 20)]
    [InlineData(true, 21, 20)]
    public void Validate_RejectsInconsistentPlayerCounts(
        bool online,
        int onlinePlayers,
        int maxPlayers)
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat(
                    "lobby",
                    online,
                    onlinePlayers,
                    maxPlayers,
                    null,
                    null)
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("servers[0]", errors);
    }

    [Fact]
    public void Validate_RejectsOldCaptureTime()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now.AddMinutes(-6),
            "mc-vps-primary",
            [new VelocityTargetHeartbeat("lobby", false, 0, 0, null, null)]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("capturedAt", errors);
    }
}
