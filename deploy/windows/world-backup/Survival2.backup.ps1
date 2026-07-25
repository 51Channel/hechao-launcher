$ErrorActionPreference = 'Stop'
& "$env:ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1" `
    -ServerId 'survival2' `
    -ServerDirectory 'E:\Survival2' `
    -WorldFolders @('world', 'world_nether', 'world_the_end') `
    -BackupDirectory 'E:\backups' `
    -RetentionCount 1 `
    -ReserveBytes 1GB
