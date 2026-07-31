[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerScript = Join-Path `
    $repositoryRoot `
    'deploy\windows\server-control\Run-MinecraftServer.ps1'
$temporaryRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "hechao-managed-start-$([Guid]::NewGuid().ToString('N'))"
$serverDirectory = Join-Path $temporaryRoot 'server with spaces'
$runtimeDirectory = Join-Path $temporaryRoot 'runtime'
$logDirectory = Join-Path $temporaryRoot 'logs'
$startScript = Join-Path $serverDirectory 'start-probe.bat'
$serverId = 'managed-start-probe'

try {
    [void][System.IO.Directory]::CreateDirectory($serverDirectory)
    [void][System.IO.Directory]::CreateDirectory($runtimeDirectory)
    [void][System.IO.Directory]::CreateDirectory($logDirectory)
    [System.IO.File]::WriteAllText(
        $startScript,
        (@(
            '@echo off'
            'if not defined HECHAO_MANAGED_START pause'
            'echo managed-stdout'
            'echo managed-stderr 1>&2'
            'exit /b 7'
        ) -join "`r`n"),
        [System.Text.ASCIIEncoding]::new()
    )

    $consoleLogPath = Join-Path $logDirectory "$serverId-console.log"
    $previousLogPath = Join-Path `
        $logDirectory `
        "$serverId-console.previous.log"
    $seedLog = [System.IO.File]::Create($consoleLogPath)
    try {
        $seedLog.SetLength(64MB)
    }
    finally {
        $seedLog.Dispose()
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command pwsh).Source
    $startInfo.UseShellExecute = $false
    foreach ($argument in @(
        '-NoLogo',
        '-NoProfile',
        '-File', $runnerScript,
        '-ServerId', $serverId,
        '-ServerDirectory', $serverDirectory,
        '-StartScript', $startScript,
        '-RuntimeMarkerDirectory', $runtimeDirectory,
        '-ConsoleLogDirectory', $logDirectory
    )) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 7) {
        throw "Managed runner returned $($process.ExitCode), expected 7."
    }

    $consoleLog = [System.IO.File]::ReadAllText($consoleLogPath)
    if ($consoleLog -notmatch 'managed-stdout' -or
        $consoleLog -notmatch 'managed-stderr') {
        throw 'Managed stdout and stderr were not both written to the console log.'
    }

    if (-not (Test-Path -LiteralPath $previousLogPath -PathType Leaf) -or
        (Get-Item -LiteralPath $previousLogPath).Length -ne 64MB) {
        throw 'The 64 MiB console log was not rotated before launch.'
    }

    $markerPath = Join-Path $runtimeDirectory "$serverId.json"
    if (Test-Path -LiteralPath $markerPath) {
        throw 'The managed runtime marker was not removed after process exit.'
    }

    [pscustomobject]@{
        passed = 4
        status = 'passed'
    } | ConvertTo-Json
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
