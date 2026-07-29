using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hechao.Launcher.Services;

internal static class ProfileRuntimePathResolver
{
    public static string GetRuntimeLaunchRoot(string runtimeRoot, string aliasKey)
    {
        var fullRuntimeRoot = Path.GetFullPath(runtimeRoot);
        Directory.CreateDirectory(fullRuntimeRoot);
        if (!ContainsFormatCharacters(fullRuntimeRoot))
        {
            return fullRuntimeRoot;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "A Java runtime path containing invisible format characters is not supported.");
        }

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fullRuntimeRoot.ToUpperInvariant())));
        var safeKey = new string(aliasKey
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(48)
            .ToArray());
        if (safeKey.Length == 0)
        {
            safeKey = "profile";
        }

        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var aliasRoot = Path.Combine(
            localApplicationData,
            "Hechao",
            "Launcher",
            "runtime-links",
            $"{safeKey}-{digest[..16]}");
        return WindowsJunction.Ensure(aliasRoot, fullRuntimeRoot);
    }

    public static string GetGameLaunchRoot(string gameRoot)
    {
        var fullGameRoot = Path.GetFullPath(gameRoot);
        Directory.CreateDirectory(fullGameRoot);
        if (!ContainsFormatCharacters(fullGameRoot))
        {
            return fullGameRoot;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "A game path containing invisible format characters is not supported.");
        }

        return TryGetSafeShortPath(fullGameRoot) ??
            throw new IOException(
                "The game path requires an NTFS 8.3 short name because directory links " +
                "are incompatible with Java CREATE_NEW file operations.");
    }

    internal static string? TryGetSafeShortPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        var buffer = new StringBuilder(WindowsMaximumPath);
        var length = GetShortPathName(
            fullPath,
            buffer,
            checked((uint)buffer.Capacity));
        if (length == 0 || length >= buffer.Capacity)
        {
            return null;
        }

        // Path.GetFullPath expands an 8.3 path back to its long form on .NET 10.
        // Keep the fully qualified value returned by Windows so format characters stay absent.
        var shortPath = buffer.ToString();
        return Directory.Exists(shortPath) && !ContainsFormatCharacters(shortPath)
            ? shortPath
            : null;
    }

    internal static bool ContainsFormatCharacters(string path) =>
        path.Any(character =>
            char.GetUnicodeCategory(character) == UnicodeCategory.Format);

    private const int WindowsMaximumPath = 32_768;

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetShortPathNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetShortPathName(
        string longPath,
        StringBuilder shortPath,
        uint bufferLength);

    private static class WindowsJunction
    {
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint IoReparseTagMountPoint = 0xA0000003;

        public static string Ensure(string aliasPath, string targetPath)
        {
            var fullAliasPath = Path.GetFullPath(aliasPath);
            var fullTargetPath = Path.GetFullPath(targetPath);
            var alias = new DirectoryInfo(fullAliasPath);
            if (alias.Exists)
            {
                Verify(alias, fullTargetPath);
                return fullAliasPath;
            }

            Directory.CreateDirectory(alias.Parent!.FullName);
            Directory.CreateDirectory(fullAliasPath);
            try
            {
                SetJunction(fullAliasPath, fullTargetPath);
                Verify(new DirectoryInfo(fullAliasPath), fullTargetPath);
                return fullAliasPath;
            }
            catch
            {
                try
                {
                    Directory.Delete(fullAliasPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                throw;
            }
        }

        private static void Verify(DirectoryInfo alias, string targetPath)
        {
            if ((alias.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                throw new IOException("The launcher compatibility path is not a directory link.");
            }

            var resolved = alias.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is null ||
                !string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved.FullName)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The launcher compatibility path points to another directory.");
            }
        }

        private static void SetJunction(string aliasPath, string targetPath)
        {
            var substituteName = targetPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\??\UNC\" + targetPath.TrimStart('\\')
                : @"\??\" + targetPath;
            var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
            var printBytes = Encoding.Unicode.GetBytes(targetPath);
            var pathBufferLength = checked(
                substituteBytes.Length + sizeof(char) + printBytes.Length + sizeof(char));
            var reparseDataLength = checked((ushort)(8 + pathBufferLength));
            var buffer = new byte[8 + reparseDataLength];

            BinaryPrimitives.WriteUInt32LittleEndian(
                buffer.AsSpan(0, sizeof(uint)),
                IoReparseTagMountPoint);
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(4, sizeof(ushort)),
                reparseDataLength);
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(8, sizeof(ushort)),
                0);
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(10, sizeof(ushort)),
                checked((ushort)substituteBytes.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(12, sizeof(ushort)),
                checked((ushort)(substituteBytes.Length + sizeof(char))));
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(14, sizeof(ushort)),
                checked((ushort)printBytes.Length));
            substituteBytes.CopyTo(buffer, 16);
            printBytes.CopyTo(buffer, 16 + substituteBytes.Length + sizeof(char));

            using var handle = CreateFile(
                aliasPath,
                GenericWrite,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new IOException(
                    "Unable to open the launcher compatibility path.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            if (!DeviceIoControl(
                    handle,
                    FsctlSetReparsePoint,
                    buffer,
                    buffer.Length,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                throw new IOException(
                    "Unable to create the launcher compatibility path.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            byte[] inputBuffer,
            int inputBufferSize,
            IntPtr outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
