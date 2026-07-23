using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.Launcher.Services;

public interface ISecureSessionStore
{
    Task<StoredLauncherSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredLauncherSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredLauncherSession(string RefreshToken, HechaoAccount Account);

public sealed class DpapiSessionStore : ISecureSessionStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private const int MaximumSessionFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _sessionPath;

    public DpapiSessionStore()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _sessionPath = Path.Combine(applicationData, "Hechao", "Launcher", "session.dat");
    }

    public async Task<StoredLauncherSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_sessionPath))
            {
                return null;
            }

            var file = new FileInfo(_sessionPath);
            if (file.Length is <= 0 or > MaximumSessionFileBytes)
            {
                await ClearAsync(cancellationToken);
                return null;
            }

            var encrypted = await File.ReadAllBytesAsync(_sessionPath, cancellationToken);
            var plaintext = Unprotect(encrypted);
            var session = JsonSerializer.Deserialize<StoredLauncherSession>(
                plaintext,
                SerializerOptions);
            if (session?.Account is null ||
                string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                await ClearAsync(cancellationToken);
                return null;
            }

            return session;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task SaveAsync(StoredLauncherSession session, CancellationToken cancellationToken = default)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, SerializerOptions);
        var encrypted = Protect(plaintext);
        var directory = Path.GetDirectoryName(_sessionPath)!;
        var temporaryPath = _sessionPath + ".tmp";

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
        File.Move(temporaryPath, _sessionPath, overwrite: true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Delete(_sessionPath);
            File.Delete(_sessionPath + ".tmp");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] plaintext)
    {
        return Transform(plaintext, protect: true);
    }

    private static byte[] Unprotect(byte[] encrypted)
    {
        return Transform(encrypted, protect: false);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { Size = input.Length, Data = inputPointer };

            var success = protect
                ? CryptProtectData(
                    ref inputBlob,
                    "Hechao Launcher Session",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
                return result;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
