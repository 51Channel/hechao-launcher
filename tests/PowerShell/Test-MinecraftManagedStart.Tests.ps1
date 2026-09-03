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
$managedJavaHome = Join-Path $temporaryRoot 'managed java'
$managedJavaBin = Join-Path $managedJavaHome 'bin'
$fallbackJavaHome = Join-Path $temporaryRoot 'fallback java'
$fallbackJavaBin = Join-Path $fallbackJavaHome 'bin'
$startScript = Join-Path $serverDirectory 'start-probe.bat'
$serverId = 'managed-start-probe'

try {
    [void][System.IO.Directory]::CreateDirectory($serverDirectory)
    [void][System.IO.Directory]::CreateDirectory($runtimeDirectory)
    [void][System.IO.Directory]::CreateDirectory($logDirectory)
    [void][System.IO.Directory]::CreateDirectory($managedJavaBin)
    [void][System.IO.Directory]::CreateDirectory($fallbackJavaBin)
    Copy-Item `
        -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') `
        -Destination (Join-Path $managedJavaBin 'java.exe')
    Copy-Item `
        -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') `
        -Destination (Join-Path $fallbackJavaBin 'java.exe')
    [System.IO.File]::WriteAllText(
        (Join-Path $serverDirectory '.hechao-deployment.json'),
        '{"schemaVersion":1,"javaMajorVersion":8}',
        [System.Text.UTF8Encoding]::new($false)
    )
    [System.IO.File]::WriteAllText(
        $startScript,
        (@(
            '@echo off'
            'if not defined HECHAO_MANAGED_START pause'
            'echo managed-stdout'
            'echo managed-stderr 1>&2'
            'java.exe /d /s /c "echo managed-java-home"'
            'echo JAVA_HOME=%JAVA_HOME%'
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
    $startInfo.Environment['HECHAO_JAVA_HOME'] = $fallbackJavaHome
    $startInfo.Environment['HECHAO_JAVA_8_HOME'] = $managedJavaHome
    $startInfo.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
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
    if ($consoleLog -notmatch 'managed-java-home') {
        throw 'Managed Java was not resolved through HECHAO_JAVA_8_HOME.'
    }
    if ($consoleLog -notmatch [regex]::Escape("JAVA_HOME=$managedJavaHome")) {
        throw 'JAVA_HOME was not propagated to the managed start script.'
    }

    if (-not (Test-Path -LiteralPath $previousLogPath -PathType Leaf) -or
        (Get-Item -LiteralPath $previousLogPath).Length -ne 64MB) {
        throw 'The 64 MiB console log was not rotated before launch.'
    }

    $markerPath = Join-Path $runtimeDirectory "$serverId.json"
    if (Test-Path -LiteralPath $markerPath) {
        throw 'The managed runtime marker was not removed after process exit.'
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $serverDirectory '.hechao-deployment.json'),
        '{"schemaVersion":1,"javaMajorVersion":30}',
        [System.Text.UTF8Encoding]::new($false)
    )
    Remove-Item -LiteralPath $consoleLogPath -Force

    $missingRuntimeStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $missingRuntimeStartInfo.FileName = (Get-Command pwsh).Source
    $missingRuntimeStartInfo.UseShellExecute = $false
    $missingRuntimeStartInfo.RedirectStandardError = $true
    $missingRuntimeStartInfo.RedirectStandardOutput = $true
    $missingRuntimeStartInfo.Environment['HECHAO_JAVA_HOME'] = $fallbackJavaHome
    $missingRuntimeStartInfo.Environment['HECHAO_JAVA_30_HOME'] = Join-Path `
        $temporaryRoot `
        'missing java 30'
    $missingRuntimeStartInfo.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
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
        [void]$missingRuntimeStartInfo.ArgumentList.Add($argument)
    }

    $missingRuntimeProcess = [System.Diagnostics.Process]::Start(
        $missingRuntimeStartInfo
    )
    $missingRuntimeStandardError = $missingRuntimeProcess.StandardError.ReadToEnd()
    $null = $missingRuntimeProcess.StandardOutput.ReadToEnd()
    $missingRuntimeProcess.WaitForExit()
    if ($missingRuntimeProcess.ExitCode -eq 0 -or
        $missingRuntimeProcess.ExitCode -eq 7) {
        throw 'A package-specific missing Java runtime did not fail closed.'
    }
    if ($missingRuntimeStandardError -notmatch 'HECHAO_JAVA_30_HOME') {
        throw 'The failed start did not identify the required package Java runtime.'
    }
    if (Test-Path -LiteralPath $consoleLogPath -PathType Leaf) {
        throw 'The start script ran after package-specific Java validation failed.'
    }
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        throw 'Runtime state was created before package-specific Java validation passed.'
    }

    Remove-Item `
        -LiteralPath (Join-Path $serverDirectory '.hechao-deployment.json') `
        -Force

    $legacyStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $legacyStartInfo.FileName = (Get-Command pwsh).Source
    $legacyStartInfo.UseShellExecute = $false
    $legacyStartInfo.Environment['HECHAO_JAVA_HOME'] = $fallbackJavaHome
    $legacyStartInfo.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
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
        [void]$legacyStartInfo.ArgumentList.Add($argument)
    }

    $legacyProcess = [System.Diagnostics.Process]::Start($legacyStartInfo)
    $legacyProcess.WaitForExit()
    if ($legacyProcess.ExitCode -ne 7) {
        throw "Legacy managed runner returned $($legacyProcess.ExitCode), expected 7."
    }

    $legacyConsoleLog = [System.IO.File]::ReadAllText($consoleLogPath)
    if ($legacyConsoleLog -notmatch [regex]::Escape("JAVA_HOME=$fallbackJavaHome")) {
        throw 'Legacy deployments did not fall back to HECHAO_JAVA_HOME.'
    }

    [pscustomobject]@{
        passed = 12
        status = 'passed'
    } | ConvertTo-Json
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
