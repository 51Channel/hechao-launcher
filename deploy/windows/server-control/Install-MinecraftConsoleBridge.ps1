[CmdletBinding()]
param(
    [string]$InstallDirectory = 'C:\ProgramData\Hechao\ServerControl',

    [string]$TaskName = 'Hechao-MinecraftConsoleBridge',

    [string]$RunAsUser = 'Administrator',

    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source

$workerScript = Join-Path $InstallDirectory 'Invoke-MinecraftConsoleRequest.ps1'
if (-not (Test-Path -LiteralPath $workerScript -PathType Leaf)) {
    throw "Console request worker is missing: $workerScript"
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "server-control-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    Export-ScheduledTask -TaskName $TaskName |
        Set-Content -LiteralPath (
            Join-Path $backupDirectory "$TaskName.xml"
        ) -Encoding UTF8
}

$action = New-ScheduledTaskAction `
    -Execute $pwsh `
    -Argument (
        '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ' +
        "-File `"$workerScript`""
    ) `
    -WorkingDirectory $InstallDirectory
$principal = New-ScheduledTaskPrincipal `
    -UserId $RunAsUser `
    -LogonType S4U `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::FromMinutes(2)) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Principal $principal `
    -Settings $settings `
    -Description 'Processes audited Minecraft console command requests.' `
    -Force |
    Out-Null

$installed = Get-ScheduledTask -TaskName $TaskName
if ($installed.Principal.LogonType -ne 'S4U') {
    throw "Minecraft console bridge must use unattended S4U logon: $TaskName"
}
[pscustomobject]@{
    task_name = $installed.TaskName
    task_state = [string]$installed.State
    run_as_user = $installed.Principal.UserId
    logon_type = [string]$installed.Principal.LogonType
    worker_script = $workerScript
    backup_directory = $backupDirectory
    server_action = 'none'
} | ConvertTo-Json -Compress
