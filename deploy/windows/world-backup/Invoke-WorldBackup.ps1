[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$ServerId,

    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')]
    [string]$ServerDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')]
    [ValidateNotNullOrEmpty()]
    [string[]]$WorldFolders,

    [Parameter(ParameterSetName = 'Capture')]
    [string]$BackupDirectory = 'E:\backups',

    [Parameter(ParameterSetName = 'Capture')]
    [ValidateRange(1, 30)]
    [int]$RetentionCount = 1,

    [Parameter(ParameterSetName = 'Capture')]
    [ValidateRange(268435456, 17179869184)]
    [long]$ReserveBytes = 1GB,

    [Parameter(ParameterSetName = 'Capture')]
    [ValidateRange(5, 120)]
    [int]$WorkerStartTimeoutSeconds = 30,

    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')]
    [string]$WorkerJobPath,

    [string]$StateDirectory = "$env:ProgramData\Hechao\WorldBackup\state",

    [string]$LogDirectory = "$env:ProgramData\Hechao\WorldBackup\logs"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:CurrentServerId = if ($ServerId) { $ServerId } else { 'worker' }
$script:CurrentLogDirectory = $LogDirectory
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Join-NativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$ChildPath
    )

    return $BasePath.TrimEnd('\') + '\' + $ChildPath.TrimStart('\')
}

function Write-BackupLog {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('INFO', 'WARN', 'ERROR')]
        [string]$Level,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    New-Item -ItemType Directory -Path $script:CurrentLogDirectory -Force |
        Out-Null
    $logPath = Join-Path $script:CurrentLogDirectory "$($script:CurrentServerId).log"
    $timestamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffK')
    Add-Content -LiteralPath $logPath -Value "$timestamp [$Level] $Message" `
        -Encoding UTF8
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $parent = Split-Path -Parent $LiteralPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporaryPath = "$LiteralPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            ($Value | ConvertTo-Json -Depth 10 -Compress),
            $script:Utf8NoBom)
        Move-Item -LiteralPath $temporaryPath -Destination $LiteralPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function New-AtomicJsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $bytes = $script:Utf8NoBom.GetBytes(
        ($Value | ConvertTo-Json -Depth 10 -Compress))
    $stream = New-Object System.IO.FileStream(
        $LiteralPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Write-MarkerFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    [System.IO.File]::WriteAllText(
        $LiteralPath,
        $Token,
        $script:Utf8NoBom)
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    return [System.IO.File]::ReadAllText($LiteralPath) | ConvertFrom-Json
}

function Get-FreeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $root = [System.IO.Path]::GetPathRoot($LiteralPath)
    if ($root -notmatch '^[A-Za-z]:\\$') {
        throw "Path must be on a local drive: $LiteralPath"
    }

    $deviceId = $root.Substring(0, 2)
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$deviceId'"
    if ($null -eq $disk) {
        throw "Unable to read free space for $deviceId"
    }

    return [long]$disk.FreeSpace
}

function Get-SourceEntries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceServerDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Folders
    )

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($folder in $Folders | Select-Object -Unique) {
        if ($folder -notmatch '^[A-Za-z0-9._-]+$') {
            throw "World folder is not a single safe path component: $folder"
        }

        $sourcePath = Join-NativePath -BasePath $SourceServerDirectory `
            -ChildPath $folder
        if (-not [System.IO.Directory]::Exists($sourcePath)) {
            continue
        }

        foreach ($filePath in [System.IO.Directory]::EnumerateFiles(
                $sourcePath,
                '*',
                [System.IO.SearchOption]::AllDirectories)) {
            if ([System.IO.Path]::GetFileName($filePath) -ieq 'session.lock') {
                continue
            }

            $file = New-Object System.IO.FileInfo($filePath)
            $relativePath = $filePath.Substring($sourcePath.Length).
                TrimStart('\').Replace('\', '/')
            $entries.Add([pscustomobject]@{
                    File = $file
                    EntryName = "$folder/$relativePath"
                })
        }
    }

    if ($entries.Count -eq 0) {
        throw 'The configured world folders contain no backup files.'
    }

    return $entries.ToArray()
}

function Assert-BackupPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedServerDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedBackupDirectory
    )

    if (-not (Test-Path -LiteralPath $ResolvedServerDirectory -PathType Container)) {
        throw "ServerDirectory does not exist: $ResolvedServerDirectory"
    }
    if ($ResolvedBackupDirectory.StartsWith(
            "$ResolvedServerDirectory\",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'BackupDirectory cannot be inside ServerDirectory.'
    }

    $serverRoot = [System.IO.Path]::GetPathRoot($ResolvedServerDirectory)
    $backupRoot = [System.IO.Path]::GetPathRoot($ResolvedBackupDirectory)
    if ($serverRoot -notmatch '^[A-Za-z]:\\$') {
        throw 'ServerDirectory must be on a local drive.'
    }
    if ($backupRoot -notmatch '^[A-Za-z]:\\$') {
        throw 'BackupDirectory must be on a local drive.'
    }
}

function Assert-FreeSpace {
    param(
        [Parameter(Mandatory = $true)]
        [long]$SourceBytes,

        [Parameter(Mandatory = $true)]
        [long]$RequiredReserveBytes,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedBackupDirectory
    )

    $estimatedArchiveBytes = [Math]::Max(
        [long][Math]::Ceiling($SourceBytes * 1.02),
        512MB)
    $requiredFreeBytes = $estimatedArchiveBytes + $RequiredReserveBytes
    $freeBytes = Get-FreeBytes -LiteralPath $ResolvedBackupDirectory
    if ($freeBytes -lt $requiredFreeBytes) {
        throw ("Insufficient backup space. Free={0} EstimatedArchive={1} Reserve={2}" -f
            $freeBytes,
            $estimatedArchiveBytes,
            $RequiredReserveBytes)
    }

    return [pscustomobject]@{
        FreeBytes = $freeBytes
        EstimatedArchiveBytes = $estimatedArchiveBytes
        RequiredFreeBytes = $requiredFreeBytes
    }
}

function New-ShadowCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VolumeRoot
    )

    $result = Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create `
        -Arguments @{
        Volume = $VolumeRoot
        Context = 'ClientAccessible'
    }
    if ([int]$result.ReturnValue -ne 0) {
        throw "Win32_ShadowCopy.Create failed with code $($result.ReturnValue)."
    }

    $shadowId = [string]$result.ShadowID
    $shadow = Get-CimInstance Win32_ShadowCopy |
        Where-Object { $_.ID -eq $shadowId } |
        Select-Object -First 1
    if ($null -eq $shadow) {
        throw 'The created shadow copy could not be located.'
    }

    return [pscustomobject]@{
        Id = $shadowId
        DeviceObject = [string]$shadow.DeviceObject
        VolumeName = [string]$shadow.VolumeName
    }
}

function Remove-ShadowCopy {
    param(
        [Parameter(Mandatory = $true)]
        [ValidatePattern('^\{[0-9A-Fa-f-]{36}\}$')]
        [string]$ShadowId
    )

    $existing = Get-CimInstance Win32_ShadowCopy |
        Where-Object { $_.ID -eq $ShadowId } |
        Select-Object -First 1
    if ($null -eq $existing) {
        return $true
    }

    $vssAdmin = Join-Path $env:SystemRoot 'System32\vssadmin.exe'
    $output = & $vssAdmin delete shadows "/Shadow=$ShadowId" /Quiet 2>&1
    $exitCode = $LASTEXITCODE
    $remaining = Get-CimInstance Win32_ShadowCopy |
        Where-Object { $_.ID -eq $ShadowId } |
        Select-Object -First 1
    if ($exitCode -ne 0 -or $null -ne $remaining) {
        $detail = ($output | Out-String).Trim()
        throw "Unable to delete shadow copy $ShadowId. ExitCode=$exitCode $detail"
    }

    return $true
}

function Test-ProcessAlive {
    param(
        [object]$ProcessId
    )

    $parsedId = 0
    if ($null -eq $ProcessId -or
        -not [int]::TryParse([string]$ProcessId, [ref]$parsedId) -or
        $parsedId -le 0) {
        return $false
    }

    return $null -ne (Get-Process -Id $parsedId -ErrorAction SilentlyContinue)
}

function Test-ChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ParentPath,

        [Parameter(Mandatory = $true)]
        [string]$CandidatePath
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\')
    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    return $resolvedCandidate.StartsWith(
        "$resolvedParent\",
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Remove-StateArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedStateDirectory,

        [object]$CandidatePath
    )

    if ([string]::IsNullOrWhiteSpace([string]$CandidatePath)) {
        return
    }

    $path = [string]$CandidatePath
    if (Test-ChildPath -ParentPath $ResolvedStateDirectory -CandidatePath $path) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}

function Remove-OwnedActiveState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ActivePath,

        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    if (-not (Test-Path -LiteralPath $ActivePath -PathType Leaf)) {
        return
    }

    try {
        $active = Read-JsonFile -LiteralPath $ActivePath
        if ([string]$active.Token -eq $Token) {
            Remove-Item -LiteralPath $ActivePath -Force
        }
    }
    catch {
        Write-BackupLog -Level WARN -Message (
            "Unable to inspect active state during cleanup: $($_.Exception.Message)")
    }
}

function Repair-StaleActiveState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedStateDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ActivePath
    )

    if (-not (Test-Path -LiteralPath $ActivePath -PathType Leaf)) {
        return
    }

    $active = $null
    try {
        $active = Read-JsonFile -LiteralPath $ActivePath
    }
    catch {
        throw "The world backup active state is unreadable: $ActivePath"
    }

    if ((Test-ProcessAlive -ProcessId $active.CoordinatorPid) -or
        (Test-ProcessAlive -ProcessId $active.WorkerPid)) {
        throw "Another world backup is active for $($active.ServerId)."
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$active.SnapshotId)) {
        Remove-ShadowCopy -ShadowId ([string]$active.SnapshotId) | Out-Null
    }

    foreach ($propertyName in @(
            'JobPath',
            'GatePath',
            'StartedPath',
            'AcknowledgedPath')) {
        Remove-StateArtifact -ResolvedStateDirectory $ResolvedStateDirectory `
            -CandidatePath $active.$propertyName
    }
    Remove-Item -LiteralPath $ActivePath -Force
    Write-BackupLog -Level WARN -Message (
        "Recovered stale backup state for $($active.ServerId).")
}

function Write-BackupStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedStateDirectory,

        [Parameter(Mandatory = $true)]
        [string]$StatusServerId,

        [Parameter(Mandatory = $true)]
        [object]$Status
    )

    if ($StatusServerId -notmatch '^[a-z0-9][a-z0-9._-]{1,63}$') {
        throw 'Status server ID is invalid.'
    }

    $statusPath = Join-Path $ResolvedStateDirectory "$StatusServerId.status.json"
    Write-JsonFile -LiteralPath $statusPath -Value $Status
}

function ConvertTo-QuotedProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-BackupWorker {
    $resolvedStateDirectory = [System.IO.Path]::GetFullPath(
        $StateDirectory).TrimEnd('\')
    $resolvedJobPath = [System.IO.Path]::GetFullPath($WorkerJobPath)
    if (-not (Test-ChildPath -ParentPath $resolvedStateDirectory `
            -CandidatePath $resolvedJobPath)) {
        throw 'WorkerJobPath must be inside StateDirectory.'
    }
    if (-not (Test-Path -LiteralPath $resolvedJobPath -PathType Leaf)) {
        throw "Worker job is missing: $resolvedJobPath"
    }

    $job = Read-JsonFile -LiteralPath $resolvedJobPath
    $script:CurrentServerId = [string]$job.ServerId
    $script:CurrentLogDirectory = [string]$job.LogDirectory
    if ($script:CurrentServerId -notmatch '^[a-z0-9][a-z0-9._-]{1,63}$') {
        throw 'Worker job contains an invalid server ID.'
    }
    if ([string]$job.Token -notmatch '^[0-9a-f]{32}$') {
        throw 'Worker job contains an invalid token.'
    }
    if ([string]$job.SnapshotId -notmatch '^\{[0-9A-Fa-f-]{36}\}$') {
        throw 'Worker job contains an invalid shadow copy ID.'
    }
    if ([string]$job.SnapshotDeviceObject -notmatch
        '^\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+$') {
        throw 'Worker job contains an invalid shadow copy device path.'
    }

    $activePath = Join-Path $resolvedStateDirectory 'active.json'
    $active = Read-JsonFile -LiteralPath $activePath
    if ([string]$active.Token -ne [string]$job.Token) {
        throw 'Worker job does not own the active backup state.'
    }

    $gatePath = [string]$job.GatePath
    $startedPath = [string]$job.StartedPath
    $acknowledgedPath = [string]$job.AcknowledgedPath
    foreach ($path in @($gatePath, $startedPath, $acknowledgedPath)) {
        if (-not (Test-ChildPath -ParentPath $resolvedStateDirectory `
                -CandidatePath $path)) {
            throw 'Worker handshake path escapes StateDirectory.'
        }
    }

    $gateDeadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $gatePath -PathType Leaf)) {
        if ((Get-Date) -ge $gateDeadline) {
            throw 'Timed out waiting for the coordinator gate.'
        }
        Start-Sleep -Milliseconds 100
    }

    $mutex = New-Object System.Threading.Mutex(
        $false,
        'Global\HechaoWorldBackupWorker')
    $lockTaken = $false
    $partialPath = $null
    $checksumPartialPath = $null
    $completedArchivePath = $null
    $completedChecksumPath = $null
    $shadowRemoved = $false
    $cleanupState = $false
    $archiveResult = $null

    try {
        try {
            $lockTaken = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
        }
        catch [System.Threading.AbandonedMutexException] {
            $lockTaken = $true
            Write-BackupLog -Level WARN -Message (
                'Recovered an abandoned background backup mutex.')
        }
        if (-not $lockTaken) {
            throw 'Timed out waiting for the background backup mutex.'
        }

        $active.WorkerPid = $PID
        $active.Phase = 'SnapshotReady'
        $active.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-JsonFile -LiteralPath $activePath -Value $active
        Write-MarkerFile -LiteralPath $startedPath -Token ([string]$job.Token)

        $ackDeadline = (Get-Date).AddSeconds(30)
        while (-not (Test-Path -LiteralPath $acknowledgedPath -PathType Leaf)) {
            if ((Get-Date) -ge $ackDeadline) {
                throw 'Timed out waiting for coordinator acknowledgement.'
            }
            Start-Sleep -Milliseconds 100
        }

        $ackToken = [System.IO.File]::ReadAllText($acknowledgedPath).Trim()
        if ($ackToken -ne [string]$job.Token) {
            throw 'Coordinator acknowledgement token is invalid.'
        }

        $active.Phase = 'Compressing'
        $active.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-JsonFile -LiteralPath $activePath -Value $active
        Write-BackupStatus -ResolvedStateDirectory $resolvedStateDirectory `
            -StatusServerId $script:CurrentServerId -Status ([ordered]@{
                ServerId = $script:CurrentServerId
                State = 'Compressing'
                Token = [string]$job.Token
                StartedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                SnapshotId = [string]$job.SnapshotId
            })

        $snapshotServerDirectory = Join-NativePath `
            -BasePath ([string]$job.SnapshotDeviceObject) `
            -ChildPath ([string]$job.RelativeServerDirectory)
        if (-not [System.IO.Directory]::Exists($snapshotServerDirectory)) {
            throw "Snapshot server directory is missing: $snapshotServerDirectory"
        }

        $sourceEntries = @(Get-SourceEntries `
                -SourceServerDirectory $snapshotServerDirectory `
                -Folders @($job.WorldFolders))
        $sourceBytes = [long](($sourceEntries.File |
                Measure-Object -Property Length -Sum).Sum)
        $resolvedBackupDirectory = [System.IO.Path]::GetFullPath(
            [string]$job.BackupDirectory).TrimEnd('\')
        New-Item -ItemType Directory -Path $resolvedBackupDirectory -Force |
            Out-Null
        $space = Assert-FreeSpace -SourceBytes $sourceBytes `
            -RequiredReserveBytes ([long]$job.ReserveBytes) `
            -ResolvedBackupDirectory $resolvedBackupDirectory

        $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
        $archiveName = "$($script:CurrentServerId)-backup-$timestamp.zip"
        $archivePath = Join-Path $resolvedBackupDirectory $archiveName
        $partialPath = "$archivePath.partial"
        $checksumPath = "$archivePath.sha256"
        $checksumPartialPath = "$checksumPath.partial"
        foreach ($path in @($partialPath, $checksumPartialPath)) {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Force
            }
        }

        Write-BackupLog -Level INFO -Message (
            "Compressing snapshot. Files=$($sourceEntries.Count) " +
            "SourceBytes=$sourceBytes FreeBytes=$($space.FreeBytes) " +
            "SnapshotId=$($job.SnapshotId)")
        $archive = [System.IO.Compression.ZipFile]::Open(
            $partialPath,
            [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($sourceEntry in $sourceEntries) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $sourceEntry.File.FullName,
                    $sourceEntry.EntryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) |
                    Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }

        $validationArchive = [System.IO.Compression.ZipFile]::OpenRead(
            $partialPath)
        try {
            if ($validationArchive.Entries.Count -ne $sourceEntries.Count) {
                throw ("Archive entry count mismatch. Expected={0} Actual={1}" -f
                    $sourceEntries.Count,
                    $validationArchive.Entries.Count)
            }
        }
        finally {
            $validationArchive.Dispose()
        }

        $archiveHash = (Get-FileHash -LiteralPath $partialPath `
                -Algorithm SHA256).Hash
        [System.IO.File]::WriteAllText(
            $checksumPartialPath,
            "$archiveHash *$archiveName`r`n",
            $script:Utf8NoBom)
        Move-Item -LiteralPath $partialPath -Destination $archivePath
        $completedArchivePath = $archivePath
        $partialPath = $null
        Move-Item -LiteralPath $checksumPartialPath -Destination $checksumPath
        $completedChecksumPath = $checksumPath
        $checksumPartialPath = $null

        $archivePattern = "$($script:CurrentServerId)-backup-*.zip"
        $expiredArchives = Get-ChildItem -LiteralPath $resolvedBackupDirectory `
                -Filter $archivePattern -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -Skip ([int]$job.RetentionCount)
        foreach ($expiredArchive in $expiredArchives) {
            $resolvedExpiredPath = [System.IO.Path]::GetFullPath(
                $expiredArchive.FullName)
            if (-not $resolvedExpiredPath.StartsWith(
                    "$resolvedBackupDirectory\",
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove archive outside BackupDirectory."
            }

            Remove-Item -LiteralPath $resolvedExpiredPath -Force
            $expiredChecksumPath = "$resolvedExpiredPath.sha256"
            if (Test-Path -LiteralPath $expiredChecksumPath) {
                Remove-Item -LiteralPath $expiredChecksumPath -Force
            }
        }

        $archiveInfo = Get-Item -LiteralPath $archivePath
        $freeBytesAfter = Get-FreeBytes -LiteralPath $resolvedBackupDirectory
        $archiveResult = [ordered]@{
            ServerId = $script:CurrentServerId
            State = 'Completed'
            Token = [string]$job.Token
            CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            Archive = $archivePath
            Files = $sourceEntries.Count
            SourceBytes = $sourceBytes
            ArchiveBytes = [long]$archiveInfo.Length
            Sha256 = $archiveHash
            RetentionCount = [int]$job.RetentionCount
            FreeBytesAfter = $freeBytesAfter
        }
        Write-BackupLog -Level INFO -Message (
            "Backup completed. Archive=$archiveName " +
            "Bytes=$($archiveInfo.Length) SHA256=$archiveHash " +
            "FreeBytes=$freeBytesAfter")
    }
    catch {
        if ($null -ne $partialPath -and
            (Test-Path -LiteralPath $partialPath)) {
            Remove-Item -LiteralPath $partialPath -Force `
                -ErrorAction SilentlyContinue
        }
        if ($null -ne $checksumPartialPath -and
            (Test-Path -LiteralPath $checksumPartialPath)) {
            Remove-Item -LiteralPath $checksumPartialPath -Force `
                -ErrorAction SilentlyContinue
        }
        if ($null -ne $completedArchivePath -and
            $null -eq $completedChecksumPath -and
            (Test-Path -LiteralPath $completedArchivePath)) {
            Remove-Item -LiteralPath $completedArchivePath -Force `
                -ErrorAction SilentlyContinue
        }

        Write-BackupLog -Level ERROR -Message $_.Exception.Message
        try {
            Write-BackupStatus -ResolvedStateDirectory $resolvedStateDirectory `
                -StatusServerId $script:CurrentServerId -Status ([ordered]@{
                    ServerId = $script:CurrentServerId
                    State = 'Failed'
                    Token = [string]$job.Token
                    FailedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                    Error = $_.Exception.Message
                    SnapshotId = [string]$job.SnapshotId
                })
        }
        catch {
            Write-BackupLog -Level ERROR -Message (
                "Unable to write failure status: $($_.Exception.Message)")
        }
        throw
    }
    finally {
        try {
            Remove-ShadowCopy -ShadowId ([string]$job.SnapshotId) | Out-Null
            $shadowRemoved = $true
        }
        catch {
            Write-BackupLog -Level ERROR -Message (
                "Shadow cleanup failed: $($_.Exception.Message)")
        }

        if ($shadowRemoved) {
            foreach ($path in @(
                    $gatePath,
                    $startedPath,
                    $acknowledgedPath,
                    $resolvedJobPath)) {
                Remove-StateArtifact `
                    -ResolvedStateDirectory $resolvedStateDirectory `
                    -CandidatePath $path
            }
            Remove-OwnedActiveState -ActivePath $activePath `
                -Token ([string]$job.Token)
            $cleanupState = $true
        }

        if ($lockTaken) {
            $mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }

    if (-not $cleanupState) {
        throw 'Backup data completed, but shadow cleanup requires recovery.'
    }

    if ($null -ne $archiveResult) {
        Write-BackupStatus -ResolvedStateDirectory $resolvedStateDirectory `
            -StatusServerId $script:CurrentServerId -Status $archiveResult
        [pscustomobject]$archiveResult
    }
}

function Invoke-BackupCapture {
    $resolvedServerDirectory = [System.IO.Path]::GetFullPath(
        $ServerDirectory).TrimEnd('\')
    $resolvedBackupDirectory = [System.IO.Path]::GetFullPath(
        $BackupDirectory).TrimEnd('\')
    $resolvedStateDirectory = [System.IO.Path]::GetFullPath(
        $StateDirectory).TrimEnd('\')
    Assert-BackupPaths -ResolvedServerDirectory $resolvedServerDirectory `
        -ResolvedBackupDirectory $resolvedBackupDirectory
    New-Item -ItemType Directory -Path $resolvedBackupDirectory -Force |
        Out-Null
    New-Item -ItemType Directory -Path $resolvedStateDirectory -Force |
        Out-Null

    $sourceEntries = @(Get-SourceEntries `
            -SourceServerDirectory $resolvedServerDirectory `
            -Folders $WorldFolders)
    $sourceBytes = [long](($sourceEntries.File |
            Measure-Object -Property Length -Sum).Sum)
    $space = Assert-FreeSpace -SourceBytes $sourceBytes `
        -RequiredReserveBytes $ReserveBytes `
        -ResolvedBackupDirectory $resolvedBackupDirectory

    $activePath = Join-Path $resolvedStateDirectory 'active.json'
    Repair-StaleActiveState -ResolvedStateDirectory $resolvedStateDirectory `
        -ActivePath $activePath

    $token = [guid]::NewGuid().ToString('N')
    $jobPath = Join-Path $resolvedStateDirectory "job-$token.json"
    $gatePath = Join-Path $resolvedStateDirectory "job-$token.ready"
    $startedPath = Join-Path $resolvedStateDirectory "job-$token.started"
    $acknowledgedPath = Join-Path $resolvedStateDirectory "job-$token.ack"
    $active = [ordered]@{
        Token = $token
        ServerId = $ServerId
        Phase = 'PreparingSnapshot'
        CoordinatorPid = $PID
        WorkerPid = 0
        SnapshotId = $null
        JobPath = $jobPath
        GatePath = $gatePath
        StartedPath = $startedPath
        AcknowledgedPath = $acknowledgedPath
        CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    try {
        New-AtomicJsonFile -LiteralPath $activePath -Value $active
    }
    catch [System.IO.IOException] {
        throw 'Another world backup acquired the active state first.'
    }

    $shadow = $null
    $worker = $null
    $handoffComplete = $false
    try {
        $volumeRoot = [System.IO.Path]::GetPathRoot(
            $resolvedServerDirectory)
        $relativeServerDirectory = $resolvedServerDirectory.Substring(
            $volumeRoot.Length).TrimStart('\')
        if ([string]::IsNullOrWhiteSpace($relativeServerDirectory) -or
            $relativeServerDirectory.Contains('..')) {
            throw 'ServerDirectory must be below the volume root.'
        }

        Write-BackupLog -Level INFO -Message (
            "Creating VSS snapshot. Files=$($sourceEntries.Count) " +
            "SourceBytes=$sourceBytes FreeBytes=$($space.FreeBytes)")
        $shadow = New-ShadowCopy -VolumeRoot $volumeRoot
        $active.SnapshotId = $shadow.Id
        $active.Phase = 'SnapshotCreated'
        $active.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-JsonFile -LiteralPath $activePath -Value $active

        $snapshotServerDirectory = Join-NativePath `
            -BasePath $shadow.DeviceObject `
            -ChildPath $relativeServerDirectory
        $snapshotEntries = @(Get-SourceEntries `
                -SourceServerDirectory $snapshotServerDirectory `
                -Folders $WorldFolders)
        if ($snapshotEntries.Count -eq 0) {
            throw 'The VSS snapshot contains no configured world files.'
        }

        $job = [ordered]@{
            SchemaVersion = 1
            Token = $token
            ServerId = $ServerId
            SnapshotId = $shadow.Id
            SnapshotDeviceObject = $shadow.DeviceObject
            RelativeServerDirectory = $relativeServerDirectory
            WorldFolders = @($WorldFolders | Select-Object -Unique)
            BackupDirectory = $resolvedBackupDirectory
            RetentionCount = $RetentionCount
            ReserveBytes = $ReserveBytes
            LogDirectory = [System.IO.Path]::GetFullPath($LogDirectory)
            GatePath = $gatePath
            StartedPath = $startedPath
            AcknowledgedPath = $acknowledgedPath
            CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }
        Write-JsonFile -LiteralPath $jobPath -Value $job
        Write-BackupStatus -ResolvedStateDirectory $resolvedStateDirectory `
            -StatusServerId $ServerId -Status ([ordered]@{
                ServerId = $ServerId
                State = 'SnapshotCreated'
                Token = $token
                QueuedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                SnapshotId = $shadow.Id
                SourceFiles = $sourceEntries.Count
                SourceBytes = $sourceBytes
            })

        $powerShell = (Get-Process -Id $PID).Path
        $argumentList = @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            (ConvertTo-QuotedProcessArgument -Value $PSCommandPath),
            '-WorkerJobPath',
            (ConvertTo-QuotedProcessArgument -Value $jobPath),
            '-StateDirectory',
            (ConvertTo-QuotedProcessArgument -Value $resolvedStateDirectory)
        )
        $worker = Start-Process -FilePath $powerShell `
            -ArgumentList $argumentList -WindowStyle Hidden -PassThru
        $active.WorkerPid = $worker.Id
        $active.Phase = 'StartingWorker'
        $active.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-JsonFile -LiteralPath $activePath -Value $active
        Write-MarkerFile -LiteralPath $gatePath -Token $token

        $deadline = (Get-Date).AddSeconds($WorkerStartTimeoutSeconds)
        while (-not (Test-Path -LiteralPath $startedPath -PathType Leaf)) {
            if ($worker.HasExited) {
                throw "Background backup worker exited with code $($worker.ExitCode)."
            }
            if ((Get-Date) -ge $deadline) {
                throw 'Timed out waiting for the background backup worker.'
            }
            Start-Sleep -Milliseconds 100
            $worker.Refresh()
        }

        $startedToken = [System.IO.File]::ReadAllText($startedPath).Trim()
        if ($startedToken -ne $token) {
            throw 'Background backup worker returned an invalid token.'
        }
        Write-MarkerFile -LiteralPath $acknowledgedPath -Token $token
        $handoffComplete = $true
        Write-BackupLog -Level INFO -Message (
            "Snapshot handed to worker PID=$($worker.Id) " +
            "SnapshotId=$($shadow.Id). Live saving may resume.")

        [pscustomobject]@{
            ServerId = $ServerId
            State = 'Queued'
            WorkerPid = $worker.Id
            SnapshotId = $shadow.Id
            SourceFiles = $sourceEntries.Count
            SourceBytes = $sourceBytes
            EstimatedArchiveBytes = $space.EstimatedArchiveBytes
            BackupDirectory = $resolvedBackupDirectory
        }
    }
    catch {
        if ($null -ne $worker -and -not $handoffComplete) {
            try {
                if (-not $worker.HasExited) {
                    Stop-Process -Id $worker.Id -Force
                    $worker.WaitForExit(5000) | Out-Null
                }
            }
            catch {
                Write-BackupLog -Level WARN -Message (
                    "Unable to stop failed worker: $($_.Exception.Message)")
            }
        }
        if ($null -ne $shadow -and -not $handoffComplete) {
            try {
                Remove-ShadowCopy -ShadowId $shadow.Id | Out-Null
            }
            catch {
                Write-BackupLog -Level ERROR -Message (
                    "Coordinator shadow cleanup failed: $($_.Exception.Message)")
            }
        }
        if (-not $handoffComplete) {
            foreach ($path in @(
                    $jobPath,
                    $gatePath,
                    $startedPath,
                    $acknowledgedPath)) {
                Remove-StateArtifact `
                    -ResolvedStateDirectory $resolvedStateDirectory `
                    -CandidatePath $path
            }
            Remove-OwnedActiveState -ActivePath $activePath -Token $token
        }
        Write-BackupLog -Level ERROR -Message $_.Exception.Message
        try {
            Write-BackupStatus -ResolvedStateDirectory $resolvedStateDirectory `
                -StatusServerId $ServerId -Status ([ordered]@{
                    ServerId = $ServerId
                    State = 'Failed'
                    Token = $token
                    FailedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                    Error = $_.Exception.Message
                    SnapshotId = if ($null -ne $shadow) { $shadow.Id } else { $null }
                })
        }
        catch {
            Write-BackupLog -Level ERROR -Message (
                "Unable to write coordinator failure status: $($_.Exception.Message)")
        }
        throw
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Worker') {
    Invoke-BackupWorker
    return
}

Invoke-BackupCapture
