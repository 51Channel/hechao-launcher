[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramData\Hechao\StatusCollector",
    [string]$TaskName = 'Hechao Launcher Server Heartbeats'
)

$ErrorActionPreference = 'Stop'
$collector = Join-Path $InstallDirectory 'Hechao.StatusCollector.exe'
$configuration = Join-Path $InstallDirectory 'server-heartbeats.json'
$tokenPath = Join-Path $InstallDirectory 'heartbeat-token.dat'
if (-not (Test-Path -LiteralPath $collector) -or
    -not (Test-Path -LiteralPath $configuration) -or
    -not (Test-Path -LiteralPath $tokenPath)) {
    throw 'The collector executable, configuration, or protected token is missing.'
}

$actionParameters = @{
    Execute = $collector
    Argument = "--config `"$configuration`""
    WorkingDirectory = $InstallDirectory
}
$action = New-ScheduledTaskAction @actionParameters

$triggerParameters = @{
    Once = $true
    At = (Get-Date).AddMinutes(1)
    RepetitionInterval = (New-TimeSpan -Minutes 1)
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
    ExecutionTimeLimit = (New-TimeSpan -Seconds 45)
}
$settings = New-ScheduledTaskSettingsSet @settingsParameters

$registrationParameters = @{
    TaskName = $TaskName
    Action = $action
    Trigger = $trigger
    Principal = $principal
    Settings = $settings
    Description = 'Read-only Minecraft status heartbeat collection for the Hechao launcher API.'
    Force = $true
}
Register-ScheduledTask @registrationParameters | Out-Null

Write-Output "scheduled_task=$TaskName"
