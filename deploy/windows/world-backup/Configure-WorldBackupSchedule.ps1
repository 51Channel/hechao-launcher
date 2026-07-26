[CmdletBinding()]
param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$BackupRoot = 'E:\manual-backups',
    [string]$Survival1Directory = 'E:\Survival1',
    [string]$Survival2Directory = 'E:\Survival2',
    [string]$LobbyDirectory = 'E:\LobbyServer'
)

$ErrorActionPreference = 'Stop'
$resolvedSourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$resolvedBackupRoot = [System.IO.Path]::GetFullPath($BackupRoot)
$resolvedSurvival1Directory = [System.IO.Path]::GetFullPath($Survival1Directory)
$resolvedSurvival2Directory = [System.IO.Path]::GetFullPath($Survival2Directory)
$resolvedLobbyDirectory = [System.IO.Path]::GetFullPath($LobbyDirectory)
$essentialsConfig = Join-Path $resolvedLobbyDirectory 'plugins\Essentials\config.yml'
$deployments = @(
    [pscustomobject]@{
        Name = 'Survival1'
        Source = Join-Path $resolvedSourceDirectory 'Survival1.daily-backup.sk'
        Destination = Join-Path $resolvedSurvival1Directory 'plugins\Skript\scripts\daily-backup.sk'
    }
    [pscustomobject]@{
        Name = 'Survival2'
        Source = Join-Path $resolvedSourceDirectory 'Survival2.daily-backup.sk'
        Destination = Join-Path $resolvedSurvival2Directory 'plugins\Skript\scripts\daily-backup.sk'
    }
    [pscustomobject]@{
        Name = 'Lobby'
        Source = Join-Path $resolvedSourceDirectory 'Lobby.daily-backup.sk'
        Destination = Join-Path $resolvedLobbyDirectory 'plugins\Skript\scripts\daily-backup.sk'
    }
)

foreach ($deployment in $deployments) {
    if (-not (Test-Path -LiteralPath $deployment.Source -PathType Leaf)) {
        throw "Schedule source is missing: $($deployment.Source)"
    }
    if (-not (Test-Path -LiteralPath $deployment.Destination -PathType Leaf)) {
        throw "Existing schedule is missing: $($deployment.Destination)"
    }
}
if (-not (Test-Path -LiteralPath $essentialsConfig -PathType Leaf)) {
    throw "Lobby Essentials configuration is missing: $essentialsConfig"
}

$configText = [System.IO.File]::ReadAllText($essentialsConfig)
$backupHeader = [regex]::Match($configText, '(?m)^backup:[ \t]*\r?$')
if (-not $backupHeader.Success) {
    throw 'The Essentials backup block could not be located.'
}
$backupBlockStart = $backupHeader.Index + $backupHeader.Length
$remainingConfig = $configText.Substring($backupBlockStart)
$nextTopLevel = [regex]::Match(
    $remainingConfig,
    '(?m)^(?![ \t\r\n])\S.*$')
$backupBlockLength = if ($nextTopLevel.Success) {
    $nextTopLevel.Index
}
else {
    $remainingConfig.Length
}
$backupBlock = $remainingConfig.Substring(0, $backupBlockLength)

$intervalMatches = [regex]::Matches(
    $backupBlock,
    '(?m)^(?<prefix>[ \t]+interval:[ \t]*)\d+(?<suffix>[ \t]*(?:#.*)?)(?<cr>\r?)$')
if ($intervalMatches.Count -ne 1) {
    throw "Expected one Essentials backup interval, found $($intervalMatches.Count)."
}

$updatedBlock = [regex]::Replace(
    $backupBlock,
    '(?m)^([ \t]+interval:[ \t]*)\d+([ \t]*(?:#.*)?)(\r?)$',
    '${1}0${2}${3}')
$updatedConfig = $configText.Substring(0, $backupBlockStart) +
    $updatedBlock +
    $configText.Substring($backupBlockStart + $backupBlockLength)

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ')
$backupDirectory = Join-Path $resolvedBackupRoot "world-backup-schedule-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
foreach ($deployment in $deployments) {
    Copy-Item -LiteralPath $deployment.Destination `
        -Destination (Join-Path $backupDirectory "$($deployment.Name).daily-backup.sk") `
        -Force
}
$essentialsBackup = Join-Path $backupDirectory 'Lobby.Essentials.config.yml'
Copy-Item -LiteralPath $essentialsConfig -Destination $essentialsBackup -Force

$configStaging = "$essentialsConfig.uploading"
try {
    foreach ($deployment in $deployments) {
        $stagingPath = "$($deployment.Destination).uploading"
        Copy-Item -LiteralPath $deployment.Source -Destination $stagingPath -Force
        if ((Get-FileHash -LiteralPath $deployment.Source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $stagingPath -Algorithm SHA256).Hash) {
            throw "The staged schedule checksum does not match: $($deployment.Source)"
        }
    }

    [System.IO.File]::WriteAllText(
        $configStaging,
        $updatedConfig,
        (New-Object System.Text.UTF8Encoding($false)))
    $stagedConfig = [System.IO.File]::ReadAllText($configStaging)
    if ($stagedConfig -notmatch '(?m)^[ \t]+interval:[ \t]*0[ \t]*(?:#.*)?\r?$') {
        throw 'The staged Essentials configuration does not disable its backup interval.'
    }

    foreach ($deployment in $deployments) {
        Move-Item -LiteralPath "$($deployment.Destination).uploading" `
            -Destination $deployment.Destination -Force
    }
    Move-Item -LiteralPath $configStaging -Destination $essentialsConfig -Force
}
catch {
    foreach ($deployment in $deployments) {
        $stagingPath = "$($deployment.Destination).uploading"
        if (Test-Path -LiteralPath $stagingPath) {
            Remove-Item -LiteralPath $stagingPath -Force -ErrorAction SilentlyContinue
        }
        $scheduleBackup = Join-Path $backupDirectory "$($deployment.Name).daily-backup.sk"
        if (Test-Path -LiteralPath $scheduleBackup -PathType Leaf) {
            Copy-Item -LiteralPath $scheduleBackup `
                -Destination $deployment.Destination -Force -ErrorAction SilentlyContinue
        }
    }
    if (Test-Path -LiteralPath $configStaging) {
        Remove-Item -LiteralPath $configStaging -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $essentialsBackup -PathType Leaf) {
        Copy-Item -LiteralPath $essentialsBackup `
            -Destination $essentialsConfig -Force -ErrorAction SilentlyContinue
    }
    throw
}

[pscustomobject]@{
    BackupDirectory = $backupDirectory
    Schedules = @($deployments.Destination)
    LobbyEssentialsConfig = $essentialsConfig
    ConfigurationReloadRequired = $true
    ServerRestartPerformed = $false
}
