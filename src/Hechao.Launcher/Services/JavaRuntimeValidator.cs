using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Hechao.Launcher.Services;

internal sealed record JavaRuntimeValidationResult(
    string ExecutablePath,
    int MajorVersion,
    string VersionOutput);

internal static partial class JavaRuntimeValidator
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(12);

    public static async Task<JavaRuntimeValidationResult> ValidateAsync(
        string executablePath,
        int expectedMajorVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new JavaRuntimeValidationException("Java executable path is required.");
        }

        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(executablePath.Trim()));
        var fileName = Path.GetFileName(fullPath);
        if (!File.Exists(fullPath) ||
            !IsSupportedExecutableName(fileName, OperatingSystem.IsWindows()))
        {
            throw new JavaRuntimeValidationException(
                "The selected file is not a Java executable.");
        }

        var validationPath = fileName.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(fullPath)!, "java.exe")
            : fullPath;
        if (!File.Exists(validationPath))
        {
            throw new JavaRuntimeValidationException(
                "The selected Java runtime does not include a Java executable.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = validationPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.StartInfo.ArgumentList.Add("-version");

        try
        {
            if (!process.Start())
            {
                throw new JavaRuntimeValidationException(
                    "The selected Java runtime could not be started.");
            }
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException)
        {
            throw new JavaRuntimeValidationException(
                "The selected Java runtime could not be started.",
                exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ValidationTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new JavaRuntimeValidationException(
                "The selected Java runtime did not respond.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await standardError, await standardOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var majorVersion = ParseMajorVersion(output);
        if (process.ExitCode != 0 || majorVersion is null)
        {
            throw new JavaRuntimeValidationException(
                "The selected Java runtime did not report a valid version.");
        }

        if (majorVersion.Value != expectedMajorVersion)
        {
            throw new JavaRuntimeVersionMismatchException(
                expectedMajorVersion,
                majorVersion.Value);
        }

        return new JavaRuntimeValidationResult(
            fullPath,
            majorVersion.Value,
            output.Trim());
    }

    internal static int? ParseMajorVersion(string value)
    {
        var match = JavaVersionRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var version = match.Groups["version"].Value;
        var components = version.Split(['.', '-', '_', '+']);
        if (components.Length == 0)
        {
            return null;
        }

        if (components[0] == "1" &&
            components.Length > 1 &&
            int.TryParse(components[1], out var legacyMajor))
        {
            return legacyMajor;
        }

        return int.TryParse(components[0], out var major) ? major : null;
    }

    internal static bool IsSupportedExecutableName(string fileName, bool isWindows) =>
        isWindows
            ? fileName.Equals("java.exe", StringComparison.OrdinalIgnoreCase) ||
              fileName.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)
            : fileName.Equals("java", StringComparison.Ordinal);

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    [GeneratedRegex(
        @"(?:openjdk|java)\s+version\s+""(?<version>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaVersionRegex();
}

public class JavaRuntimeValidationException : IOException
{
    public JavaRuntimeValidationException(string message)
        : base(message)
    {
    }

    public JavaRuntimeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class JavaRuntimeVersionMismatchException(
    int expectedMajorVersion,
    int actualMajorVersion)
    : JavaRuntimeValidationException(
        $"Java {expectedMajorVersion} is required, but Java {actualMajorVersion} was selected.")
{
    public int ExpectedMajorVersion { get; } = expectedMajorVersion;
    public int ActualMajorVersion { get; } = actualMajorVersion;
}
