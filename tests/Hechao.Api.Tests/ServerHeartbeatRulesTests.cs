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

    [Fact]
    public void Validate_AcceptsCompleteRuntimeMetrics()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat(
                    "lobby",
                    true,
                    12,
                    300,
                    "Paper 1.21.11",
                    774,
                    ProcessWorkingSetBytes: 4_294_967_296,
                    ProcessPrivateBytes: 5_368_709_120,
                    ProcessCpuPercent: 37.5,
                    ProcessStartedAt: Now.AddHours(-3),
                    DiskFreeBytes: 214_748_364_800,
                    DiskTotalBytes: 536_870_912_000,
                    Tps1m: 19.98,
                    Tps5m: 19.97,
                    Tps15m: 19.96,
                    MsptAverage: 18.4,
                    GcCollectionTimeMilliseconds: 12_345,
                    MetricsCapturedAt: Now.AddSeconds(-5),
                    Issues: [])
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsPartialRuntimeMetrics()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat(
                    "lobby",
                    true,
                    12,
                    300,
                    null,
                    null,
                    ProcessWorkingSetBytes: 4_294_967_296)
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("servers[0]", errors);
        Assert.Contains("process metrics", errors["servers[0]"][0]);
    }

    [Fact]
    public void Validate_RejectsStaleTickMetrics()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat(
                    "lobby",
                    true,
                    12,
                    300,
                    null,
                    null,
                    Tps1m: 20,
                    Tps5m: 20,
                    Tps15m: 20,
                    MsptAverage: 10,
                    GcCollectionTimeMilliseconds: 10,
                    MetricsCapturedAt: Now.AddMinutes(-6))
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("servers[0]", errors);
        Assert.Contains("tick metrics", errors["servers[0]"][0]);
    }

    [Fact]
    public void Validate_RejectsDuplicateIssues()
    {
        var request = new ServerHeartbeatBatchRequest(
            Now,
            "mc-vps-primary",
            [
                new VelocityTargetHeartbeat(
                    "activity",
                    false,
                    0,
                    30,
                    null,
                    null,
                    Issues:
                    [
                        ServerMetricIssueCode.ProcessNotRunning,
                        ServerMetricIssueCode.ProcessNotRunning
                    ])
            ]);

        var errors = ServerHeartbeatRules.Validate(request, Now);

        Assert.Contains("servers[0]", errors);
        Assert.Contains("issue list", errors["servers[0]"][0]);
    }
}
