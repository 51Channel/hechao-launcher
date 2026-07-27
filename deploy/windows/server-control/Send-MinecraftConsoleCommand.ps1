[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,

    [Parameter(Mandatory)]
    [ValidateScript({
        if ([string]::IsNullOrWhiteSpace($_)) {
            throw 'Command cannot be empty.'
        }
        if ($_.Length -gt 256) {
            throw 'Command cannot exceed 256 characters.'
        }
        if ($_ -match "[`r`n`0]") {
            throw 'Command cannot contain CR, LF, or NUL characters.'
        }
        $true
    })]
    [string]$Command
)

$ErrorActionPreference = 'Stop'

$process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"
if ($null -eq $process) {
    throw "Process $ProcessId does not exist."
}

if ([System.IO.Path]::GetFileName($process.ExecutablePath) -ine 'java.exe') {
    throw "Process $ProcessId is not java.exe."
}

if (-not ('Hechao.ServerControl.ConsoleBridge' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Hechao.ServerControl
{
    public static class ConsoleBridge
    {
        private const short KeyEvent = 0x0001;
        private const ushort VirtualKeyReturn = 0x0D;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct KeyEventRecord
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool KeyDown;
            public ushort RepeatCount;
            public ushort VirtualKeyCode;
            public ushort VirtualScanCode;
            public char UnicodeChar;
            public uint ControlKeyState;
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        private struct InputRecord
        {
            [FieldOffset(0)]
            public short EventType;

            [FieldOffset(4)]
            public KeyEventRecord KeyEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteConsoleInputW(
            IntPtr consoleInput,
            InputRecord[] buffer,
            uint length,
            out uint eventsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public static int Send(uint processId, string command)
        {
            FreeConsole();

            if (!AttachConsole(processId))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    "Unable to attach to the target console (Win32 " + error + ")");
            }

            IntPtr input = new IntPtr(-1);
            try
            {
                input = CreateFileW(
                    "CONIN$",
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);

                if (input == new IntPtr(-1))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(
                        error,
                        "Unable to open the target console input buffer (Win32 " + error + ")");
                }

                string line = command + "\r";
                InputRecord[] records = new InputRecord[line.Length * 2];
                int index = 0;

                foreach (char value in line)
                {
                    ushort virtualKey = value == '\r' ? VirtualKeyReturn : (ushort)0;
                    records[index++] = CreateKeyRecord(true, value, virtualKey);
                    records[index++] = CreateKeyRecord(false, value, virtualKey);
                }

                uint written;
                if (!WriteConsoleInputW(input, records, (uint)records.Length, out written))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(
                        error,
                        "Unable to write to the target console input buffer (Win32 " + error + ")");
                }

                if (written != records.Length)
                {
                    throw new InvalidOperationException(
                        "Only " + written + " of " + records.Length + " console events were written.");
                }

                return records.Length;
            }
            finally
            {
                if (input != new IntPtr(-1))
                {
                    CloseHandle(input);
                }
                FreeConsole();
            }
        }

        private static InputRecord CreateKeyRecord(
            bool keyDown,
            char value,
            ushort virtualKey)
        {
            return new InputRecord
            {
                EventType = KeyEvent,
                KeyEvent = new KeyEventRecord
                {
                    KeyDown = keyDown,
                    RepeatCount = 1,
                    VirtualKeyCode = virtualKey,
                    VirtualScanCode = 0,
                    UnicodeChar = value,
                    ControlKeyState = 0
                }
            };
        }
    }
}
'@
}

$eventsWritten = [Hechao.ServerControl.ConsoleBridge]::Send(
    [uint32]$ProcessId,
    $Command)

[pscustomobject]@{
    process_id = $ProcessId
    executable = $process.ExecutablePath
    command = $Command
    console_events_written = $eventsWritten
    sent_at_utc = (Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Compress
