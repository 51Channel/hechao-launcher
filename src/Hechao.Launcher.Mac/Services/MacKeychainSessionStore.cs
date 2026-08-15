using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Mac.Services;

public sealed class MacKeychainSessionStore : ISecureSessionStore
{
    private const string SecurityToolPath = "/usr/bin/security";
    private const string ServiceName = "world.hechao.launcher.session";
    private const string AccountName = "launcher-session";
    private const int MaximumEncodedSessionCharacters = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<StoredLauncherSession?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureMacOS();
        var result = await RunAsync(
            ["find-generic-password", "-a", AccountName, "-s", ServiceName, "-w"],
            standardInput: null,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var encoded = result.StandardOutput.Trim();
        if (encoded.Length is <= 0 or > MaximumEncodedSessionCharacters)
        {
            await ClearAsync(cancellationToken);
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            var session = JsonSerializer.Deserialize<StoredLauncherSession>(bytes, JsonOptions);
            return session is not null &&
                   !string.IsNullOrWhiteSpace(session.RefreshToken) &&
                   session.Account is not null
                ? session
                : null;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task SaveAsync(
        StoredLauncherSession session,
        CancellationToken cancellationToken = default)
    {
        EnsureMacOS();
        ArgumentNullException.ThrowIfNull(session);
        var invocation = CreateSaveInvocation(session);
        var result = await RunAsync(
            invocation.Arguments,
            invocation.StandardInput,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new IOException("无法将赫朝登录会话写入 macOS Keychain。");
        }
    }

    internal static SecurityToolInvocation CreateSaveInvocation(StoredLauncherSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var encoded = Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions));
        if (encoded.Length > MaximumEncodedSessionCharacters)
        {
            throw new IOException("赫朝登录会话超过 macOS Keychain 存储上限。");
        }

        // Interactive mode keeps the credential out of the process command line.
        var command =
            $"add-generic-password -a \"{AccountName}\" -s \"{ServiceName}\" " +
            $"-w \"{encoded}\" -U{Environment.NewLine}";
        return new SecurityToolInvocation(["-i"], command);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureMacOS();
        _ = await RunAsync(
            ["delete-generic-password", "-a", AccountName, "-s", ServiceName],
            standardInput: null,
            cancellationToken);
    }

    private static async Task<SecurityToolResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(SecurityToolPath)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new IOException("无法启动 macOS Keychain 工具。");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        _ = await errorTask;
        return new SecurityToolResult(process.ExitCode, output);
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "macOS Keychain 会话存储只能在 macOS 上使用。");
        }
    }

    internal sealed record SecurityToolInvocation(
        IReadOnlyList<string> Arguments,
        string StandardInput);

    private sealed record SecurityToolResult(int ExitCode, string StandardOutput);
}
