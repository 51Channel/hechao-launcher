using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerAvailabilityRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolveStatus_ReturnsClosedBeforeOpening()
    {
        var status = ServerAvailabilityRules.ResolveStatus(
            ServerStatus.Online,
            Now.AddMinutes(1),
            Now.AddHours(2),
            Now);

        Assert.Equal(ServerStatus.Closed, status);
    }

    [Fact]
    public void ResolveStatus_ReturnsOnlineInsideWindow()
    {
        var status = ServerAvailabilityRules.ResolveStatus(
            ServerStatus.Online,
            Now.AddHours(-1),
            Now.AddHours(1),
            Now);

        Assert.Equal(ServerStatus.Online, status);
    }

    [Fact]
    public void ResolveStatus_ReturnsClosedAtClosingBoundary()
    {
        var status = ServerAvailabilityRules.ResolveStatus(
            ServerStatus.Online,
            null,
            Now,
            Now);

        Assert.Equal(ServerStatus.Closed, status);
    }

    [Theory]
    [InlineData(ServerStatus.Maintenance)]
    [InlineData(ServerStatus.Closed)]
    public void ResolveStatus_ManualNonOnlineStatusTakesPriority(ServerStatus configured)
    {
        var status = ServerAvailabilityRules.ResolveStatus(
            configured,
            Now.AddHours(-1),
            Now.AddHours(1),
            Now);

        Assert.Equal(configured, status);
    }
}
