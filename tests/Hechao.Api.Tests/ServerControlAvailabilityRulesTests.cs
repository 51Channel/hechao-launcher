using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerControlAvailabilityRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(ServerStatus.Maintenance)]
    [InlineData(ServerStatus.Closed)]
    public void Resolve_ManualUnavailablePolicyOverridesRunningTarget(ServerStatus policyStatus)
    {
        var result = ServerControlAvailabilityRules.Resolve(
            policyStatus,
            new ServerControlObservation(true, Now.AddSeconds(-5)),
            Now,
            Freshness);

        Assert.Equal(policyStatus, result.Status);
        Assert.True(result.HasTarget);
        Assert.True(result.IsFresh);
        Assert.True(result.ReportedOnline);
    }

    [Fact]
    public void Resolve_UnmanagedServerPreservesPolicyStatus()
    {
        var result = ServerControlAvailabilityRules.Resolve(
            ServerStatus.Online,
            observation: null,
            Now,
            Freshness);

        Assert.Equal(ServerStatus.Online, result.Status);
        Assert.False(result.HasTarget);
        Assert.False(result.IsFresh);
        Assert.Null(result.ReportedOnline);
        Assert.Null(result.LastSeenAt);
    }

    [Fact]
    public void Resolve_FreshRunningTargetPreservesOnlineStatus()
    {
        var result = ServerControlAvailabilityRules.Resolve(
            ServerStatus.Online,
            new ServerControlObservation(true, Now.AddSeconds(-5)),
            Now,
            Freshness);

        Assert.Equal(ServerStatus.Online, result.Status);
        Assert.True(result.HasTarget);
        Assert.True(result.IsFresh);
        Assert.True(result.ReportedOnline);
    }

    [Fact]
    public void Resolve_FreshStoppedTargetClosesServer()
    {
        var result = ServerControlAvailabilityRules.Resolve(
            ServerStatus.Online,
            new ServerControlObservation(false, Now.AddSeconds(-5)),
            Now,
            Freshness);

        Assert.Equal(ServerStatus.Closed, result.Status);
        Assert.True(result.HasTarget);
        Assert.True(result.IsFresh);
        Assert.False(result.ReportedOnline);
    }

    [Fact]
    public void Resolve_StaleRunningTargetClosesServer()
    {
        var result = ServerControlAvailabilityRules.Resolve(
            ServerStatus.Online,
            new ServerControlObservation(true, Now.AddSeconds(-31)),
            Now,
            Freshness);

        Assert.Equal(ServerStatus.Closed, result.Status);
        Assert.True(result.HasTarget);
        Assert.False(result.IsFresh);
        Assert.True(result.ReportedOnline);
    }
}
