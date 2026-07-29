using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Hechao.Launcher.Services;

internal static class NativeLibraryRunDirectory
{
    private const string LwjglLibraryName = "lwjgl.dll";

    public static async Task<string> PrepareAsync(
        string extractedSourceDirectory,
        string profileId,
        string versionId,
        CancellationToken cancellationToken = default,
        string? runRootOverride = null)
    {
        var sourceDirectory = Path.GetFullPath(extractedSourceDirectory);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                "The Minecraft native source directory is unavailable.");
        }

        var runRoot = GetValidatedRunRoot(runRootOverride);
        Directory.CreateDirectory(runRoot);
        var cacheKey = BuildCacheKey(profileId, versionId);
        var canonicalDirectory = Path.Combine(runRoot, cacheKey);
        var destinationDirectory = canonicalDirectory;

        if (Directory.Exists(canonicalDirectory))
        {
            try
            {
                Directory.Delete(canonicalDirectory, recursive: true);
            }
            catch (IOException)
            {
                destinationDirectory =
                    $"{canonicalDirectory}-recovery-{Guid.NewGuid():N}";
            }
            catch (UnauthorizedAccessException)
            {
                destinationDirectory =
                    $"{canonicalDirectory}-recovery-{Guid.NewGuid():N}";
            }
        }

        var temporaryDirectory = Path.Combine(
            runRoot,
            $".tmp-{cacheKey}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var destinationCreated = false;
        try
        {
            foreach (var sourcePath in Directory
                         .EnumerateFiles(
                             sourceDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = new FileInfo(sourcePath);
                if (source.Length <= 0 ||
                    (source.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    IsGeneratedRuntimeFile(source.Name))
                {
                    continue;
                }

                var destinationPath = Path.Combine(
                    temporaryDirectory,
                    source.Name);
                await CopyAndVerifyAsync(
                    source.FullName,
                    destinationPath,
                    cancellationToken);
            }

            VerifyWritableDirectory(temporaryDirectory);
            Directory.Move(temporaryDirectory, destinationDirectory);
            destinationCreated = true;
            await EnsureExistingPrimaryLibraryLoadsAsync(
                destinationDirectory,
                cancellationToken);
            return destinationDirectory;
        }
        catch
        {
            DeleteDirectoryBestEffort(temporaryDirectory);
            if (destinationCreated)
            {
                DeleteDirectoryBestEffort(destinationDirectory);
            }

            throw;
        }
    }

    private static bool IsGeneratedRuntimeFile(string name) =>
        name.StartsWith("jna", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    private static async Task CopyAndVerifyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceHash = await ComputeSha256Async(
            sourcePath,
            cancellationToken);
        await using (var source = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read | FileShare.Delete,
                         bufferSize: 64 * 1024,
                         options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var destination = new FileStream(
                         destinationPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 64 * 1024,
                         options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        var destinationHash = await ComputeSha256Async(
            destinationPath,
            cancellationToken);
        if (!string.Equals(
                sourceHash,
                destinationHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A staged Minecraft native library failed integrity verification.");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void VerifyWritableDirectory(string directory)
    {
        var probePath = Path.Combine(
            directory,
            $".write-probe-{Guid.NewGuid():N}");
        File.WriteAllText(probePath, "ready");
        File.Delete(probePath);
    }

    private static async Task EnsureExistingPrimaryLibraryLoadsAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var libraryPath = Path.Combine(directory, LwjglLibraryName);
        if (!File.Exists(libraryPath))
        {
            return;
        }

        Exception? lastException = null;
        foreach (var delay in new[]
                 {
                     TimeSpan.Zero,
                     TimeSpan.FromMilliseconds(250),
                     TimeSpan.FromMilliseconds(750)
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            nint handle = 0;
            try
            {
                handle = NativeLibrary.Load(libraryPath);
                return;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or
                BadImageFormatException or
                FileLoadException)
            {
                lastException = exception;
            }
            finally
            {
                if (handle != 0)
                {
                    NativeLibrary.Free(handle);
                }
            }
        }

        throw new IOException(
            "The staged lwjgl.dll or one of its Windows dependencies cannot be loaded.",
            lastException);
    }

    private static string BuildCacheKey(string profileId, string versionId)
    {
        var rawKey = $"{profileId}-{versionId}";
        var safeKey = new string(rawKey
            .Where(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_')
            .Take(72)
            .ToArray());
        return safeKey.Length == 0
            ? "minecraft-natives"
            : safeKey;
    }

    private static string GetValidatedRunRoot(string? runRootOverride)
    {
        var runRoot = Path.GetFullPath(
            runRootOverride ?? GetDefaultRunRoot());
        if (ProfileRuntimePathResolver.ContainsFormatCharacters(runRoot))
        {
            throw new IOException(
                "The Minecraft native run directory contains unsupported Unicode format characters.");
        }

        return runRoot;
    }

    private static string GetDefaultRunRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localApplicationData,
            "Hechao",
            "Launcher",
            "native-runs");
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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
