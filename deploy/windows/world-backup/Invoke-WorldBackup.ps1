[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$ServerId,

    [Parameter(Mandatory = $true)]
    [string]$ServerDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$WorldFolders,

    [string]$BackupDirectory = 'E:\backups',

    [ValidateRange(1, 30)]
    [int]$RetentionCount = 1,

    [ValidateRange(268435456, 17179869184)]
    [long]$ReserveBytes = 1GB,

    [ValidateRange(1, 120)]
    [int]$LockWaitMinutes = 30,

    [string]$LogDirectory = "$env:ProgramData\Hechao\WorldBackup\logs"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-BackupLog {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('INFO', 'WARN', 'ERROR')]
        [string]$Level,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $logPath = Join-Path $LogDirectory "$ServerId.log"
    $timestamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffK')
    Add-Content -LiteralPath $logPath -Value "$timestamp [$Level] $Message" -Encoding UTF8
}

function Get-FreeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $root = [System.IO.Path]::GetPathRoot($LiteralPath)
    if ($root -notmatch '^[A-Za-z]:\\$') {
        throw "BackupDirectory must be on a local drive: $LiteralPath"
    }

    $deviceId = $root.Substring(0, 2)
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$deviceId'"
    if ($null -eq $disk) {
        throw "Unable to read free space for $deviceId"
    }

    return [long]$disk.FreeSpace
}

$resolvedServerDirectory = [System.IO.Path]::GetFullPath($ServerDirectory).TrimEnd('\')
$resolvedBackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath $resolvedServerDirectory -PathType Container)) {
    throw "ServerDirectory does not exist: $resolvedServerDirectory"
}
if ($resolvedBackupDirectory.StartsWith(
        "$resolvedServerDirectory\",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'BackupDirectory cannot be inside ServerDirectory.'
}

$sourceFolders = @()
foreach ($folder in $WorldFolders | Select-Object -Unique) {
    if ($folder -notmatch '^[A-Za-z0-9._-]+$') {
        throw "World folder is not a single safe path component: $folder"
    }

    $sourcePath = [System.IO.Path]::GetFullPath(
        (Join-Path $resolvedServerDirectory $folder))
    if (-not $sourcePath.StartsWith(
            "$resolvedServerDirectory\",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "World folder escapes ServerDirectory: $folder"
    }
    if (Test-Path -LiteralPath $sourcePath -PathType Container) {
        $sourceFolders += [pscustomobject]@{
            Name = $folder
            Path = $sourcePath
        }
    }
}
if ($sourceFolders.Count -eq 0) {
    throw 'None of the configured world folders exist.'
}

New-Item -ItemType Directory -Path $resolvedBackupDirectory -Force | Out-Null
$mutex = New-Object System.Threading.Mutex($false, 'Global\HechaoWorldBackup')
$lockTaken = $false
$partialPath = $null
$checksumPartialPath = $null
$completedArchivePath = $null
$completedChecksumPath = $null

try {
    try {
        $lockTaken = $mutex.WaitOne([TimeSpan]::FromMinutes($LockWaitMinutes))
    }
    catch [System.Threading.AbandonedMutexException] {
        $lockTaken = $true
        Write-BackupLog -Level WARN -Message 'Recovered an abandoned global backup lock.'
    }
    if (-not $lockTaken) {
        throw "Timed out waiting for the global backup lock after $LockWaitMinutes minute(s)."
    }

    $sourceFiles = @()
    foreach ($sourceFolder in $sourceFolders) {
        $sourceFiles += Get-ChildItem -LiteralPath $sourceFolder.Path -File -Recurse -Force |
            Where-Object { $_.Name -ine 'session.lock' } |
            ForEach-Object {
                [pscustomobject]@{
                    File = $_
                    EntryName = "$($sourceFolder.Name)/" +
                        $_.FullName.Substring($sourceFolder.Path.Length + 1).Replace('\', '/')
                }
            }
    }
    if ($sourceFiles.Count -eq 0) {
        throw 'The configured world folders contain no backup files.'
    }

    $sourceBytes = [long](($sourceFiles.File |
            Measure-Object -Property Length -Sum).Sum)
    $archivePattern = "$ServerId-backup-*.zip"
    # Region and media files may already be compressed. Reserve for a near
    # worst-case ZIP instead of assuming a favorable compression ratio.
    $estimatedArchiveBytes = [Math]::Max(
        [long][Math]::Ceiling($sourceBytes * 1.02),
        512MB)
    $requiredFreeBytes = $estimatedArchiveBytes + $ReserveBytes
    $freeBytes = Get-FreeBytes -LiteralPath $resolvedBackupDirectory
    if ($freeBytes -lt $requiredFreeBytes) {
        throw ("Insufficient backup space. Free={0} EstimatedArchive={1} Reserve={2}" -f
            $freeBytes,
            $estimatedArchiveBytes,
            $ReserveBytes)
    }

    $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $archiveName = "$ServerId-backup-$timestamp.zip"
    $archivePath = Join-Path $resolvedBackupDirectory $archiveName
    $partialPath = "$archivePath.partial"
    $checksumPath = "$archivePath.sha256"
    $checksumPartialPath = "$checksumPath.partial"
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }
    if (Test-Path -LiteralPath $checksumPartialPath) {
        Remove-Item -LiteralPath $checksumPartialPath -Force
    }

    Write-BackupLog -Level INFO -Message (
        "Starting backup. Files=$($sourceFiles.Count) SourceBytes=$sourceBytes FreeBytes=$freeBytes")
    $archive = [System.IO.Compression.ZipFile]::Open(
        $partialPath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($sourceFile in $sourceFiles) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $sourceFile.File.FullName,
                $sourceFile.EntryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    $validationArchive = [System.IO.Compression.ZipFile]::OpenRead($partialPath)
    try {
        if ($validationArchive.Entries.Count -ne $sourceFiles.Count) {
            throw ("Archive entry count mismatch. Expected={0} Actual={1}" -f
                $sourceFiles.Count,
                $validationArchive.Entries.Count)
        }
    }
    finally {
        $validationArchive.Dispose()
    }

    $archiveHash = (Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText(
        $checksumPartialPath,
        "$archiveHash *$archiveName`r`n",
        (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $partialPath -Destination $archivePath
    $completedArchivePath = $archivePath
    $partialPath = $null
    Move-Item -LiteralPath $checksumPartialPath -Destination $checksumPath
    $completedChecksumPath = $checksumPath
    $checksumPartialPath = $null

    $expiredArchives = Get-ChildItem -LiteralPath $resolvedBackupDirectory `
            -Filter $archivePattern -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $RetentionCount
    foreach ($expiredArchive in $expiredArchives) {
        $resolvedExpiredPath = [System.IO.Path]::GetFullPath($expiredArchive.FullName)
        if (-not $resolvedExpiredPath.StartsWith(
                "$resolvedBackupDirectory\",
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an archive outside BackupDirectory: $resolvedExpiredPath"
        }

        Remove-Item -LiteralPath $resolvedExpiredPath -Force
        $checksumPath = "$resolvedExpiredPath.sha256"
        if (Test-Path -LiteralPath $checksumPath) {
            Remove-Item -LiteralPath $checksumPath -Force
        }
    }

    $archiveInfo = Get-Item -LiteralPath $archivePath
    $freeBytesAfter = Get-FreeBytes -LiteralPath $resolvedBackupDirectory
    Write-BackupLog -Level INFO -Message (
        "Backup completed. Archive=$archiveName Bytes=$($archiveInfo.Length) " +
        "SHA256=$archiveHash FreeBytes=$freeBytesAfter")
    [pscustomobject]@{
        ServerId = $ServerId
        Archive = $archivePath
        Files = $sourceFiles.Count
        SourceBytes = $sourceBytes
        ArchiveBytes = [long]$archiveInfo.Length
        Sha256 = $archiveHash
        RetentionCount = $RetentionCount
        FreeBytesAfter = $freeBytesAfter
    }
}
catch {
    if ($null -ne $partialPath -and (Test-Path -LiteralPath $partialPath)) {
        $resolvedPartialPath = [System.IO.Path]::GetFullPath($partialPath)
        if ($resolvedPartialPath.StartsWith(
                "$resolvedBackupDirectory\",
                [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedPartialPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne $checksumPartialPath -and
        (Test-Path -LiteralPath $checksumPartialPath)) {
        Remove-Item -LiteralPath $checksumPartialPath -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $completedArchivePath -and
        $null -eq $completedChecksumPath -and
        (Test-Path -LiteralPath $completedArchivePath)) {
        Remove-Item -LiteralPath $completedArchivePath -Force -ErrorAction SilentlyContinue
    }
    Write-BackupLog -Level ERROR -Message $_.Exception.Message
    throw
}
finally {
    if ($lockTaken) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
