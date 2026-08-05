using Hechao.Api.ServerControl;

namespace Hechao.Api.Tests;

public sealed class ServerControlTargetVisibilityTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void IncludeInOverview_HidesOnlyCompletedDeletedTargets(
        bool serverFilesPresent,
        bool deletionCleanupPending,
        bool hasActiveOperation,
        bool expected)
    {
        Assert.Equal(
            expected,
            ServerControlTargetVisibility.IncludeInOverview(
                serverFilesPresent,
                deletionCleanupPending,
                hasActiveOperation));
    }

    [Fact]
    public void IncludeInOverview_CanReturnCompletedDeletedTargetsForRedeployment()
    {
        Assert.True(ServerControlTargetVisibility.IncludeInOverview(
            serverFilesPresent: false,
            deletionCleanupPending: false,
            hasActiveOperation: false,
            includeDeletedTargets: true));
    }
}
