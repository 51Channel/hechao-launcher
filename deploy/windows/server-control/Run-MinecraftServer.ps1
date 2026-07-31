[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$ServerId,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [Parameter(Mandatory)]
    [string]$StartScript,

    [string]$RuntimeMarkerDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent\runtime",

    [string]$ConsoleLogDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent\logs"
)

$ErrorActionPreference = 'Stop'

if (-not ('Hechao.ServerControl.ManagedConsoleMode' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Hechao.ServerControl
{
    public static class ManagedConsoleMode
    {
        private const int StandardInputHandle = -10;
        private const uint EnableQuickEditMode = 0x0040;
        private const uint EnableExtendedFlags = 0x0080;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

        public static bool DisableQuickEdit()
        {
            IntPtr input = GetStdHandle(StandardInputHandle);
            if (input == IntPtr.Zero || input == new IntPtr(-1) ||
                !GetConsoleMode(input, out uint currentMode))
            {
                return false;
            }

            uint updatedMode =
                (currentMode | EnableExtendedFlags) & ~EnableQuickEditMode;
            return updatedMode == currentMode || SetConsoleMode(input, updatedMode);
        }
    }
}
'@
}

[void][Hechao.ServerControl.ManagedConsoleMode]::DisableQuickEdit()

$resolvedDirectory = (Resolve-Path -LiteralPath $ServerDirectory).Path
$resolvedStartScript = (Resolve-Path -LiteralPath $StartScript).Path
if (-not $resolvedStartScript.StartsWith(
        "$resolvedDirectory\",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'StartScript must be inside ServerDirectory.'
}

$scriptText = [System.IO.File]::ReadAllText($resolvedStartScript)
if ($scriptText -notmatch '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*(?:\r)?$') {
    throw 'StartScript is not configured for a managed start.'
}

$resolvedMarkerDirectory = [System.IO.Path]::GetFullPath(
    $RuntimeMarkerDirectory
)
[System.IO.Directory]::CreateDirectory($resolvedMarkerDirectory) | Out-Null
$resolvedConsoleLogDirectory = [System.IO.Path]::GetFullPath(
    $ConsoleLogDirectory
)
[System.IO.Directory]::CreateDirectory($resolvedConsoleLogDirectory) | Out-Null
$consoleLogPath = Join-Path $resolvedConsoleLogDirectory "$ServerId-console.log"
$previousConsoleLogPath = Join-Path `
    $resolvedConsoleLogDirectory `
    "$ServerId-console.previous.log"
if ((Test-Path -LiteralPath $consoleLogPath -PathType Leaf) -and
    (Get-Item -LiteralPath $consoleLogPath).Length -ge 64MB) {
    Move-Item `
        -LiteralPath $consoleLogPath `
        -Destination $previousConsoleLogPath `
        -Force
}
$markerPath = Join-Path $resolvedMarkerDirectory "$ServerId.json"
$temporaryMarkerPath = Join-Path $resolvedMarkerDirectory (
    ".$ServerId-$([Guid]::NewGuid().ToString('N')).tmp"
)
$runId = [Guid]::NewGuid().ToString('N')
$runner = Get-Process -Id $PID
$marker = [ordered]@{
    schemaVersion = 1
    serverId = $ServerId
    runId = $runId
    runnerProcessId = $PID
    runnerStartedAtUtcTicks = $runner.StartTime.ToUniversalTime().Ticks
    serverDirectory = $resolvedDirectory
    startedAt = (Get-Date).ToUniversalTime().ToString('o')
}
[System.IO.File]::WriteAllText(
    $temporaryMarkerPath,
    ($marker | ConvertTo-Json -Compress),
    [System.Text.UTF8Encoding]::new($false)
)
[System.IO.File]::Move($temporaryMarkerPath, $markerPath, $true)

$exitCode = 1
try {
    $env:HECHAO_MANAGED_START = '1'
    Set-Location -LiteralPath $resolvedDirectory
    $commandInterpreter = $env:ComSpec
    if ([string]::IsNullOrWhiteSpace($commandInterpreter)) {
        $commandInterpreter = Join-Path $env:SystemRoot 'System32\cmd.exe'
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $commandInterpreter
    $startInfo.WorkingDirectory = $resolvedDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = '/d /s /c ""{0}" >> "{1}" 2>&1"' -f
        $resolvedStartScript,
        $consoleLogPath

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryMarkerPath) {
        Remove-Item -LiteralPath $temporaryMarkerPath -Force
    }
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $currentMarker = Get-Content -Raw -LiteralPath $markerPath |
                ConvertFrom-Json
            if ([string]$currentMarker.runId -eq $runId) {
                Remove-Item -LiteralPath $markerPath -Force
            }
        }
        catch {
            # Keep an unrecognized marker so the agent fails closed.
        }
    }
}

exit $exitCode
