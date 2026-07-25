$ErrorActionPreference = 'Stop'
& "$env:ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1" `
    -ServerId 'lobby' `
    -ServerDirectory 'E:\LobbyServer' `
    -WorldFolders @('world') `
    -BackupDirectory 'E:\backups-lobby' `
    -RetentionCount 7 `
    -ReserveBytes 512MB
