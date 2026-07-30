[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ServerName,

    [string]$ServerId,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [string]$StartScript = 'start.bat',

    [string]$TaskPrefix = 'Hechao-Server',

    [string]$RunAsUser = 'Administrator',

    [string]$BackupRoot = 'E:\manual-backups',

    [string]$RuntimeMarkerDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent\runtime"
)

$ErrorActionPreference = 'Stop'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
if ([string]::IsNullOrWhiteSpace($ServerId)) {
    $ServerId = $ServerName.ToLowerInvariant()
}
if ($ServerId -notmatch '^[a-z0-9][a-z0-9._-]{1,63}$') {
    throw "ServerId is invalid: $ServerId"
}

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
$resolvedMarkerDirectory = [System.IO.Path]::GetFullPath(
    $RuntimeMarkerDirectory
)
[System.IO.Directory]::CreateDirectory($resolvedMarkerDirectory) | Out-Null
$escapedMarkerDirectory = $resolvedMarkerDirectory.Replace('"', '""')
$action = New-ScheduledTaskAction `
    -Execute $pwsh `
    -Argument (
        '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ' +
        "-File `"$escapedRunner`" " +
        "-ServerId `"$ServerId`" " +
        "-ServerDirectory `"$escapedDirectory`" " +
        "-StartScript `"$escapedStartScript`" " +
        "-RuntimeMarkerDirectory `"$escapedMarkerDirectory`""
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
    server_id = $ServerId
    start_script = $resolvedStartScript
    runner_script = $runnerScript
    runtime_marker_directory = $resolvedMarkerDirectory
    backup_directory = $backupDirectory
    server_started = $false
} | ConvertTo-Json -Compress
