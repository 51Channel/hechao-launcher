using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.Launcher.Services;

public interface ISecureSessionStore
{
    Task<StoredLauncherSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredLauncherSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredLauncherSession(
    string RefreshToken,
    HechaoAccount Account,
    string? AccessToken = null,
    DateTimeOffset? AccessTokenExpiresAt = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);

public sealed class DpapiSessionStore : ISecureSessionStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private const int MaximumSessionFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private readonly string _sessionPath;
    private readonly string _backupPath;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;

    public DpapiSessionStore()
        : this(GetDefaultSessionPath())
    {
    }

    internal DpapiSessionStore(
        string sessionPath,
        Func<byte[], byte[]>? protect = null,
        Func<byte[], byte[]>? unprotect = null)
    {
        _sessionPath = Path.GetFullPath(sessionPath);
        _backupPath = _sessionPath + ".bak";
        _protect = protect ?? Protect;
        _unprotect = unprotect ?? Unprotect;
    }

    public async Task<StoredLauncherSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var path in new[] { _sessionPath, _backupPath })
        {
            var session = await TryLoadFileAsync(path, cancellationToken);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    public async Task SaveAsync(StoredLauncherSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, SerializerOptions);
        var encrypted = _protect(plaintext);
        if (encrypted.Length is <= 0 or > MaximumSessionFileBytes)
        {
            throw new InvalidDataException("The encrypted launcher session is invalid.");
        }

        var directory = Path.GetDirectoryName(_sessionPath)!;
        Directory.CreateDirectory(directory);
        await SaveGate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_sessionPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            ReplaceAtomically(temporaryPath);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }

            SaveGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SaveGate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(_sessionPath);
            TryDelete(_backupPath);
            var directory = Path.GetDirectoryName(_sessionPath);
            if (directory is not null && Directory.Exists(directory))
            {
                try
                {
                    var temporaryPrefix = $".{Path.GetFileName(_sessionPath)}.";
                    foreach (var path in Directory.EnumerateFiles(directory, $"{temporaryPrefix}*.tmp"))
                    {
                        TryDelete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        finally
        {
            SaveGate.Release();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // Keep the async method's cancellation contract explicit even
            // though file deletion itself is best-effort.
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task<StoredLauncherSession?> TryLoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var file = new FileInfo(path);
            if (file.Length is <= 0 or > MaximumSessionFileBytes)
            {
                return null;
            }

            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
            var plaintext = _unprotect(encrypted);
            var session = JsonSerializer.Deserialize<StoredLauncherSession>(
                plaintext,
                SerializerOptions);
            return session?.Account is not null &&
                   !string.IsNullOrWhiteSpace(session.RefreshToken)
                ? session
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            Win32Exception or
            CryptographicException or
            InvalidDataException or
            ArgumentException)
        {
            // A transient DPAPI/file error must not delete the only durable
            // session. A later start or an explicit login can recover it.
            return null;
        }
    }

    private void ReplaceAtomically(string temporaryPath)
    {
        if (!File.Exists(_sessionPath))
        {
            File.Move(temporaryPath, _sessionPath);
            return;
        }

        try
        {
            File.Replace(
                temporaryPath,
                _sessionPath,
                _backupPath,
                ignoreMetadataErrors: true);
            return;
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (IOException)
        {
            // File.Replace is not available on every volume. The fallback
            // still replaces the file only after the complete temp file was
            // flushed, so a partial JSON payload cannot become current.
        }

        try
        {
            File.Copy(_sessionPath, _backupPath, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        File.Move(temporaryPath, _sessionPath, overwrite: true);
    }

    private static string GetDefaultSessionPath()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(applicationData, "Hechao", "Launcher", "session.dat");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Win32Exception)
        {
        }
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
