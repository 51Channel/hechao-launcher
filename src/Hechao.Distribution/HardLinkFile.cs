using System.Runtime.InteropServices;

namespace Hechao.Distribution;

internal static class HardLinkFile
{
    public static bool TryCreate(string newFileName, string existingFileName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return CreateHardLink(newFileName, existingFileName, IntPtr.Zero);
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes);
}
