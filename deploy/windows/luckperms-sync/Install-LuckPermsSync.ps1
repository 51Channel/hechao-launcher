[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramData\Hechao\LauncherBridge",
    [string]$TaskName = 'Hechao Launcher LuckPerms Sync'
)

$ErrorActionPreference = 'Stop'
$syncScript = Join-Path $InstallDirectory 'Sync-LuckPerms.ps1'
$tokenPath = Join-Path $InstallDirectory 'sync-token.dat'
if (-not (Test-Path -LiteralPath $syncScript) -or -not (Test-Path -LiteralPath $tokenPath)) {
    throw 'The sync script or protected token is missing.'
}

$powerShell = Join-Path $PSHOME 'powershell.exe'
$actionParameters = @{
    Execute = $powerShell
    Argument = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$syncScript`""
}
$action = New-ScheduledTaskAction @actionParameters

$triggerParameters = @{
    Once = $true
    At = (Get-Date).AddMinutes(1)
    RepetitionInterval = (New-TimeSpan -Minutes 5)
    RepetitionDuration = (New-TimeSpan -Days 3650)
}
$trigger = New-ScheduledTaskTrigger @triggerParameters

$principalParameters = @{
    UserId = 'SYSTEM'
    LogonType = 'ServiceAccount'
    RunLevel = 'Highest'
}
$principal = New-ScheduledTaskPrincipal @principalParameters

$settingsParameters = @{
    MultipleInstances = 'IgnoreNew'
    StartWhenAvailable = $true
    ExecutionTimeLimit = (New-TimeSpan -Minutes 2)
}
$settings = New-ScheduledTaskSettingsSet @settingsParameters

$registrationParameters = @{
    TaskName = $TaskName
    Action = $action
    Trigger = $trigger
    Principal = $principal
    Settings = $settings
    Description = 'Read-only LuckPerms group snapshot sync for the Hechao launcher API.'
    Force = $true
}
Register-ScheduledTask @registrationParameters | Out-Null

Write-Output "scheduled_task=$TaskName"
