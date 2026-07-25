[CmdletBinding()]
param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = "$env:ProgramData\Hechao\WorldBackup",
    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'
$resolvedSourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$resolvedBackupRoot = [System.IO.Path]::GetFullPath($BackupRoot)
$engineSource = Join-Path $resolvedSourceDirectory 'Invoke-WorldBackup.ps1'
if (-not (Test-Path -LiteralPath $engineSource -PathType Leaf)) {
    throw "Backup engine is missing: $engineSource"
}

$deployments = @(
    [pscustomobject]@{
        Source = Join-Path $resolvedSourceDirectory 'Survival1.backup.ps1'
        Destination = 'E:\Survival1\backup.ps1'
    }
    [pscustomobject]@{
        Source = Join-Path $resolvedSourceDirectory 'Survival2.backup.ps1'
        Destination = 'E:\Survival2\backup.ps1'
    }
    [pscustomobject]@{
        Source = Join-Path $resolvedSourceDirectory 'Lobby.backup.ps1'
        Destination = 'E:\LobbyServer\backup.ps1'
    }
)
foreach ($deployment in $deployments) {
    if (-not (Test-Path -LiteralPath $deployment.Source -PathType Leaf)) {
        throw "Backup wrapper is missing: $($deployment.Source)"
    }
    $destinationParent = Split-Path -Parent $deployment.Destination
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
        throw "Server directory is missing: $destinationParent"
    }
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $resolvedBackupRoot "world-backup-scripts-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedInstallDirectory -Force | Out-Null

$engineDestination = Join-Path $resolvedInstallDirectory 'Invoke-WorldBackup.ps1'
if (Test-Path -LiteralPath $engineDestination -PathType Leaf) {
    Copy-Item -LiteralPath $engineDestination `
        -Destination (Join-Path $backupDirectory 'Invoke-WorldBackup.ps1') -Force
}
$engineStaging = "$engineDestination.uploading"
Copy-Item -LiteralPath $engineSource -Destination $engineStaging -Force
if ((Get-FileHash -LiteralPath $engineSource -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $engineStaging -Algorithm SHA256).Hash) {
    throw 'The staged backup engine checksum does not match the source.'
}
Move-Item -LiteralPath $engineStaging -Destination $engineDestination -Force

foreach ($deployment in $deployments) {
    if (Test-Path -LiteralPath $deployment.Destination -PathType Leaf) {
        $backupName = (Split-Path -Leaf (Split-Path -Parent $deployment.Destination)) +
            '.backup.ps1'
        Copy-Item -LiteralPath $deployment.Destination `
            -Destination (Join-Path $backupDirectory $backupName) -Force
    }

    $stagingPath = "$($deployment.Destination).uploading"
    Copy-Item -LiteralPath $deployment.Source -Destination $stagingPath -Force
    if ((Get-FileHash -LiteralPath $deployment.Source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $stagingPath -Algorithm SHA256).Hash) {
        throw "The staged wrapper checksum does not match: $($deployment.Source)"
    }
    Move-Item -LiteralPath $stagingPath -Destination $deployment.Destination -Force
}

[pscustomobject]@{
    Engine = $engineDestination
    EngineSha256 = (Get-FileHash -LiteralPath $engineDestination -Algorithm SHA256).Hash
    Wrappers = @($deployments.Destination)
    BackupDirectory = $backupDirectory
    ServerRestartPerformed = $false
}
