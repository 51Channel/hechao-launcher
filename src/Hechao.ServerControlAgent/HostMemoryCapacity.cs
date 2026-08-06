using System.Runtime.InteropServices;

namespace Hechao.ServerControlAgent;

internal sealed class HostMemoryCapacity
{
    private const int MemoryStepMiB = 256;
    private const int TechnicalMaximumMiB = 65536;

    private HostMemoryCapacity(int? totalMemoryMiB)
    {
        TotalMemoryMiB = totalMemoryMiB;
    }

    internal int? TotalMemoryMiB { get; }

    internal static HostMemoryCapacity FromTotalMemoryMiB(int? totalMemoryMiB) =>
        new(totalMemoryMiB);

    internal static HostMemoryCapacity Capture()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysicalBytes == 0)
        {
            return FromTotalMemoryMiB(null);
        }

        var totalMiB = status.TotalPhysicalBytes / (1024UL * 1024UL);
        return FromTotalMemoryMiB(totalMiB > int.MaxValue
            ? null
            : (int)totalMiB);
    }

    internal int ResolveManagedMaximumMemoryMiB(
        ServerControlTargetConfiguration target)
    {
        if (!target.PackageDeploymentEnabled || TotalMemoryMiB is null)
        {
            return target.MaximumAllowedMemoryMiB;
        }

        var roundedPhysicalMemory = TotalMemoryMiB.Value / MemoryStepMiB *
            MemoryStepMiB;
        return Math.Clamp(
            roundedPhysicalMemory,
            MemoryStepMiB * 2,
            TechnicalMaximumMiB);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysicalBytes;
        internal ulong AvailablePhysicalBytes;
        internal ulong TotalPageFileBytes;
        internal ulong AvailablePageFileBytes;
        internal ulong TotalVirtualBytes;
        internal ulong AvailableVirtualBytes;
        internal ulong AvailableExtendedVirtualBytes;
    }
}
