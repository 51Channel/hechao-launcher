using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Hechao.Contracts;

namespace Hechao.StatusCollector;

public sealed record ServerProcessMetrics(
    long WorkingSetBytes,
    long PrivateBytes,
    double CpuPercent,
    DateTimeOffset StartedAt);

public sealed record ServerProcessProbeResult(
    ServerProcessMetrics? Process,
    long? DiskFreeBytes,
    long? DiskTotalBytes,
    IReadOnlyList<ServerMetricIssueCode> Issues,
    bool? EndpointOwnedByExpectedProcess = null);

public interface IServerProcessMetricsProvider
{
    Task<ServerProcessProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        CancellationToken cancellationToken);
}

public sealed class NullServerProcessMetricsProvider :
    IServerProcessMetricsProvider
{
    public static NullServerProcessMetricsProvider Instance { get; } = new();

    private NullServerProcessMetricsProvider()
    {
    }

    public Task<ServerProcessProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ServerProcessProbeResult(
            null,
            null,
            null,
            [ServerMetricIssueCode.ProcessProbeNotConfigured]));
}

public sealed class WindowsServerProcessMetricsProvider :
    IServerProcessMetricsProvider
{
    private static readonly TimeSpan CpuSampleDuration =
        TimeSpan.FromMilliseconds(250);

    public async Task<ServerProcessProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        CancellationToken cancellationToken)
    {
        if (!IsLoopback(server.Host) || server.DataPath is null)
        {
            return new ServerProcessProbeResult(
                null,
                null,
                null,
                [ServerMetricIssueCode.ProcessProbeNotConfigured]);
        }

        var issues = new List<ServerMetricIssueCode>();
        long? diskFreeBytes = null;
        long? diskTotalBytes = null;
        try
        {
            var root = Path.GetPathRoot(server.DataPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("The server data path has no drive root.");
            }

            var drive = new DriveInfo(root);
            diskFreeBytes = drive.AvailableFreeSpace;
            diskTotalBytes = drive.TotalSize;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            issues.Add(ServerMetricIssueCode.DiskProbeFailed);
        }

        try
        {
            var processId = TcpListenerProcessResolver.FindProcessId(
                server.Port);
            if (processId is null)
            {
                issues.Add(ServerMetricIssueCode.ProcessNotRunning);
                return new ServerProcessProbeResult(
                    null,
                    diskFreeBytes,
                    diskTotalBytes,
                    issues,
                    server.ExpectedProcessExecutablePath is null ? null : false);
            }

            using var process = Process.GetProcessById(processId.Value);
            process.Refresh();
            bool? endpointOwnedByExpectedProcess = null;
            if (server.ExpectedProcessExecutablePath is not null)
            {
                var executablePath = process.MainModule?.FileName;
                endpointOwnedByExpectedProcess = executablePath is not null &&
                    string.Equals(
                        Path.GetFullPath(executablePath),
                        server.ExpectedProcessExecutablePath,
                        StringComparison.OrdinalIgnoreCase);
                if (endpointOwnedByExpectedProcess is false)
                {
                    issues.Add(ServerMetricIssueCode.ProcessNotRunning);
                    return new ServerProcessProbeResult(
                        null,
                        diskFreeBytes,
                        diskTotalBytes,
                        issues,
                        false);
                }
            }

            var initialCpu = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(CpuSampleDuration, cancellationToken);
            process.Refresh();
            stopwatch.Stop();

            var elapsedMilliseconds = Math.Max(
                stopwatch.Elapsed.TotalMilliseconds,
                1);
            var cpuDelta = Math.Max(
                (process.TotalProcessorTime - initialCpu).TotalMilliseconds,
                0);
            var cpuPercent = Math.Clamp(
                cpuDelta / elapsedMilliseconds /
                Math.Max(Environment.ProcessorCount, 1) * 100,
                0,
                100);
            var metrics = new ServerProcessMetrics(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                cpuPercent,
                process.StartTime.ToUniversalTime());
            return new ServerProcessProbeResult(
                metrics,
                diskFreeBytes,
                diskTotalBytes,
                issues,
                endpointOwnedByExpectedProcess);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            issues.Add(ServerMetricIssueCode.ProcessNotRunning);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or Win32Exception)
        {
            issues.Add(ServerMetricIssueCode.ProcessAccessDenied);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException)
        {
            issues.Add(ServerMetricIssueCode.ProcessProbeFailed);
        }

        return new ServerProcessProbeResult(
            null,
            diskFreeBytes,
            diskTotalBytes,
            issues);
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) &&
               IPAddress.IsLoopback(address);
    }
}

internal static class TcpListenerProcessResolver
{
    private const int AddressFamilyInet = 2;
    private const int AddressFamilyInet6 = 23;
    private const uint ErrorInsufficientBuffer = 122;

    public static int? FindProcessId(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return FindProcessId(port, AddressFamilyInet) ??
               FindProcessId(port, AddressFamilyInet6);
    }

    private static int? FindProcessId(int port, int addressFamily)
    {
        var size = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            true,
            addressFamily,
            TcpTableClass.OwnerPidListener,
            0);
        if (result != ErrorInsufficientBuffer)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref size,
                true,
                addressFamily,
                TcpTableClass.OwnerPidListener,
                0);
            if (result != 0)
            {
                return null;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            if (addressFamily == AddressFamilyInet)
            {
                return FindInRows<MibTcpRowOwnerPid>(
                    rowPointer,
                    count,
                    port,
                    row => row.LocalPort,
                    row => row.OwningPid);
            }

            return FindInRows<MibTcp6RowOwnerPid>(
                rowPointer,
                count,
                port,
                row => row.LocalPort,
                row => row.OwningPid);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? FindInRows<TRow>(
        IntPtr firstRow,
        int count,
        int expectedPort,
        Func<TRow, uint> readPort,
        Func<TRow, uint> readProcessId)
        where TRow : struct
    {
        var rowSize = Marshal.SizeOf<TRow>();
        for (var index = 0; index < count; index++)
        {
            var row = Marshal.PtrToStructure<TRow>(
                IntPtr.Add(firstRow, index * rowSize));
            if (ConvertPort(readPort(row)) == expectedPort)
            {
                return checked((int)readProcessId(row));
            }
        }

        return null;
    }

    private static int ConvertPort(uint value) =>
        unchecked((ushort)IPAddress.NetworkToHostOrder((short)value));

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    private enum TcpTableClass
    {
        OwnerPidListener = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;

        public uint LocalScopeId;
        public uint LocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;

        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }
}
