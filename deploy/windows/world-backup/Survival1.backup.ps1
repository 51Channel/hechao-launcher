$ErrorActionPreference = 'Stop'
& "$env:ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1" `
    -ServerId 'survival1' `
    -ServerDirectory 'E:\Survival1' `
    -WorldFolders @('world', 'world_nether', 'world_the_end', 'lobby') `
    -BackupDirectory 'E:\backups' `
    -RetentionCount 1 `
    -ReserveBytes 1GB
