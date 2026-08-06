namespace Hechao.ServerControlAgent.Tests;

public sealed class HostMemoryCapacityTests
{
    [Theory]
    [InlineData(32768, true, 8192, 32768)]
    [InlineData(32767, true, 8192, 32512)]
    [InlineData(131072, true, 8192, 65536)]
    [InlineData(32768, false, 8192, 8192)]
    public void ResolveManagedMaximumMemory_UsesPhysicalCapacityForPackageTargets(
        int hostTotalMemoryMiB,
        bool packageDeploymentEnabled,
        int configuredMaximumMemoryMiB,
        int expectedMaximumMemoryMiB)
    {
        var capacity = HostMemoryCapacity.FromTotalMemoryMiB(hostTotalMemoryMiB);
        var target = new ServerControlTargetConfiguration
        {
            PackageDeploymentEnabled = packageDeploymentEnabled,
            MaximumAllowedMemoryMiB = configuredMaximumMemoryMiB
        };

        Assert.Equal(
            expectedMaximumMemoryMiB,
            capacity.ResolveManagedMaximumMemoryMiB(target));
    }

    [Fact]
    public void ResolveManagedMaximumMemory_FallsBackWhenCapacityIsUnavailable()
    {
        var capacity = HostMemoryCapacity.FromTotalMemoryMiB(null);
        var target = new ServerControlTargetConfiguration
        {
            PackageDeploymentEnabled = true,
            MaximumAllowedMemoryMiB = 8192
        };

        Assert.Equal(8192, capacity.ResolveManagedMaximumMemoryMiB(target));
    }
}
