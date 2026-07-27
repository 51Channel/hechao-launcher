[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ServerName,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [string]$StartScript = 'start.bat',

    [string]$TaskPrefix = 'Hechao-Server',

    [string]$RunAsUser = 'Administrator',

    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'

$resolvedDirectory = (Resolve-Path -LiteralPath $ServerDirectory).Path
$resolvedStartScript = (Resolve-Path -LiteralPath (
    Join-Path $resolvedDirectory $StartScript
)).Path
$runnerScript = Join-Path $PSScriptRoot 'Run-MinecraftServer.ps1'

if ([System.IO.Path]::GetExtension($resolvedStartScript) -ine '.bat') {
    throw "Start script must be a .bat file: $resolvedStartScript"
}
if (-not (Test-Path -LiteralPath $runnerScript -PathType Leaf)) {
    throw "Managed server runner is missing: $runnerScript"
}

$startScriptText = [System.IO.File]::ReadAllText($resolvedStartScript)
if ($startScriptText -notmatch '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*(?:\r)?$') {
    throw "Start script is not managed-start aware: $resolvedStartScript"
}

$taskName = "$TaskPrefix-$ServerName"
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "server-control-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    Export-ScheduledTask -TaskName $taskName |
        Set-Content -LiteralPath (
            Join-Path $backupDirectory "$taskName.xml"
        ) -Encoding UTF8
}

$escapedDirectory = $resolvedDirectory.Replace('"', '""')
$escapedStartScript = $resolvedStartScript.Replace('"', '""')
$escapedRunner = $runnerScript.Replace('"', '""')
$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument (
        '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ' +
        "-File `"$escapedRunner`" " +
        "-ServerDirectory `"$escapedDirectory`" " +
        "-StartScript `"$escapedStartScript`""
    ) `
    -WorkingDirectory $resolvedDirectory
$principal = New-ScheduledTaskPrincipal `
    -UserId $RunAsUser `
    -LogonType Interactive `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Principal $principal `
    -Settings $settings `
    -Description "On-demand launcher for Hechao Minecraft server $ServerName." `
    -Force |
    Out-Null

$installed = Get-ScheduledTask -TaskName $taskName
[pscustomobject]@{
    task_name = $installed.TaskName
    task_state = [string]$installed.State
    run_as_user = $installed.Principal.UserId
    logon_type = [string]$installed.Principal.LogonType
    server_directory = $resolvedDirectory
    start_script = $resolvedStartScript
    runner_script = $runnerScript
    backup_directory = $backupDirectory
    server_started = $false
} | ConvertTo-Json -Compress
