using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia;

namespace Hechao.Launcher.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        EnsureSupportedPlatform(
            OperatingSystem.IsMacOS(),
            RuntimeInformation.ProcessArchitecture,
            GetAppleCpuBrand());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static void EnsureSupportedPlatform(
        bool isMacOS,
        Architecture architecture,
        string? cpuBrand)
    {
        if (!isMacOS ||
            architecture != Architecture.Arm64 ||
            string.IsNullOrWhiteSpace(cpuBrand) ||
            !cpuBrand.StartsWith("Apple M4", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException(
                "赫朝启动器 macOS 版仅支持 M4（macOS ARM64）。");
        }
    }

    private static string? GetAppleCpuBrand()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/sysctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-n", "machdep.cpu.brand_string" }
            });
            if (process is null)
            {
                return null;
            }

            var cpuBrand = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? cpuBrand.Trim() : null;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
