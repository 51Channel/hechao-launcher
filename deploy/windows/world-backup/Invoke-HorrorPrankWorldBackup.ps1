[CmdletBinding()]
param(
    [ValidateRange(300, 14400)]
    [int]$CompletionTimeoutSeconds = 7200
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$serverId = 'horrorprank'
$serverDirectory = 'C:\mc\server'
$expectedJava = 'C:\mc\jre\jdk-21.0.11+10-jre\bin\java.exe'
$truePvpDirectory = 'E:\MinecraftServer'
$backupDirectory = 'E:\backups'
$backupEngine = 'C:\ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1'
$consoleSubmitter =
    'C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1'
$stateDirectory = 'C:\ProgramData\Hechao\WorldBackup\state'
$statusPath = Join-Path $stateDirectory "$serverId.status.json"
$activePath = Join-Path $stateDirectory 'active.json'
$acceptancePath = Join-Path $stateDirectory "$serverId.acceptance.json"
$latestLog = Join-Path $serverDirectory 'logs\latest.log'
$metricsPath =
    Join-Path $serverDirectory 'plugins\HechaoServerMetrics\metrics.json'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Write-AtomicJson {
    param(
        [Parameter(Mandatory)]
        [string]$LiteralPath,

        [Parameter(Mandatory)]
        [object]$Value
    )

    $parent = Split-Path -Parent $LiteralPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporaryPath = "$LiteralPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            ($Value | ConvertTo-Json -Depth 10 -Compress),
            $utf8NoBom)
        Move-Item -LiteralPath $temporaryPath -Destination $LiteralPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force `
                -ErrorAction SilentlyContinue
        }
    }
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string]$LiteralPath
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Required JSON file is missing: $LiteralPath"
    }

    return Get-Content -LiteralPath $LiteralPath -Raw | ConvertFrom-Json
}

function Read-AppendedLogText {
    param(
        [Parameter(Mandatory)]
        [long]$StartOffset
    )

    $stream = [System.IO.File]::Open(
        $latestLog,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor
            [System.IO.FileShare]::Delete)
    try {
        if ($StartOffset -gt $stream.Length) {
            $StartOffset = 0
        }
        $null = $stream.Seek($StartOffset, [System.IO.SeekOrigin]::Begin)
        $remaining = $stream.Length - $StartOffset
        if ($remaining -le 0) {
            return ''
        }
        if ($remaining -gt 4MB) {
            throw 'Minecraft appended more than 4 MiB before command proof.'
        }

        $bytes = [byte[]]::new([int]$remaining)
        $read = $stream.Read($bytes, 0, $bytes.Length)
        return [System.Text.Encoding]::UTF8.GetString($bytes, 0, $read)
    }
    finally {
        $stream.Dispose()
    }
}

function Send-ConsoleCommandAndWait {
    param(
        [Parameter(Mandatory)]
        [int]$ProcessId,

        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(Mandatory)]
        [string]$ProofPattern,

        [ValidateRange(5, 120)]
        [int]$TimeoutSeconds = 45
    )

    $startOffset = (Get-Item -LiteralPath $latestLog).Length
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
        -File $consoleSubmitter `
        -ProcessId $ProcessId `
        -Command $Command `
        -TimeoutSeconds $TimeoutSeconds | Out-Null

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = Read-AppendedLogText -StartOffset $startOffset
        if ($text -match $ProofPattern) {
            return $text
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Minecraft did not log proof for command '$Command'."
}

function Get-HorrorPrankProcess {
    $matches = @(
        Get-CimInstance Win32_Process -Filter "Name='java.exe'" |
            Where-Object {
                $_.ExecutablePath -ieq $expectedJava
            }
    )
    if ($matches.Count -ne 1) {
        throw (
            'Expected exactly one HorrorPrank Java process at {0}; found {1}.' -f
            $expectedJava,
            $matches.Count)
    }

    $truePvpProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name='java.exe'" |
            Where-Object {
                $_.ExecutablePath -like "$truePvpDirectory\*"
            }
    )
    if ($truePvpProcesses.Count -ne 0) {
        throw (
            'True PVP is running from E:\MinecraftServer. ' +
            'Refusing to operate while the shared port is ambiguous.')
    }

    $listeners = @(
        Get-NetTCPConnection -State Listen -LocalPort 25565 `
            -ErrorAction SilentlyContinue
    )
    if ($listeners.Count -ne 1 -or
        [int]$listeners[0].OwningProcess -ne
            [int]$matches[0].ProcessId) {
        throw (
            'Local port 25565 is not exclusively owned by the protected ' +
            'HorrorPrank Java process.')
    }

    return $matches[0]
}

function Assert-HorrorPrankLayout {
    foreach ($path in @(
            $serverDirectory,
            (Join-Path $serverDirectory 'start-headless.bat'),
            (Join-Path $serverDirectory 'world\level.dat'),
            (Join-Path $serverDirectory 'server.properties'),
            $expectedJava,
            $backupEngine,
            $consoleSubmitter,
            $latestLog,
            $metricsPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "HorrorPrank prerequisite is missing: $path"
        }
    }

    $properties = @{}
    foreach ($line in Get-Content -LiteralPath (
            Join-Path $serverDirectory 'server.properties')) {
        if ($line -match '^\s*([^#!][^=]*)=(.*)$') {
            $properties[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    if ($properties['server-port'] -ne '25565' -or
        $properties['level-name'] -ne 'world' -or
        $properties['online-mode'] -ne 'true') {
        throw (
            'HorrorPrank server.properties does not match the protected ' +
            '25565/world/online-mode=true identity.')
    }
}

function Wait-ForBackupCompletion {
    param(
        [Parameter(Mandatory)]
        [string]$Token
    )

    $deadline = (Get-Date).AddSeconds($CompletionTimeoutSeconds)
    $missingWorkerSince = $null
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
            $status = Read-JsonFile -LiteralPath $statusPath
            if ([string]$status.Token -eq $Token) {
                if ([string]$status.State -eq 'Completed') {
                    return $status
                }
                if ([string]$status.State -eq 'Failed') {
                    throw "World backup worker failed: $($status.Error)"
                }
            }
        }

        if (Test-Path -LiteralPath $activePath -PathType Leaf) {
            $active = Read-JsonFile -LiteralPath $activePath
            if ([string]$active.Token -eq $Token) {
                $worker = Get-Process -Id ([int]$active.WorkerPid) `
                    -ErrorAction SilentlyContinue
                if ($null -eq $worker) {
                    if ($null -eq $missingWorkerSince) {
                        $missingWorkerSince = Get-Date
                    }
                    elseif (((Get-Date) - $missingWorkerSince).TotalSeconds -gt
                        10) {
                        throw 'World backup worker exited without final status.'
                    }
                }
                else {
                    $missingWorkerSince = $null
                }
            }
        }

        Start-Sleep -Seconds 2
    }

    throw "World backup exceeded $CompletionTimeoutSeconds seconds."
}

function Assert-CompletedArchive {
    param(
        [Parameter(Mandatory)]
        [object]$Status
    )

    $resolvedBackupDirectory = [System.IO.Path]::GetFullPath(
        $backupDirectory).TrimEnd('\')
    $resolvedArchive = [System.IO.Path]::GetFullPath(
        [string]$Status.Archive)
    if (-not $resolvedArchive.StartsWith(
            "$resolvedBackupDirectory\",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Completed archive escaped the HorrorPrank backup directory.'
    }
    if (-not (Test-Path -LiteralPath $resolvedArchive -PathType Leaf)) {
        throw "Completed archive is missing: $resolvedArchive"
    }

    $actualHash = (Get-FileHash -LiteralPath $resolvedArchive `
            -Algorithm SHA256).Hash
    if ($actualHash -ine [string]$Status.Sha256) {
        throw 'Completed archive SHA-256 does not match backup status.'
    }

    $sidecarPath = "$resolvedArchive.sha256"
    if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
        throw 'Completed archive checksum sidecar is missing.'
    }
    $sidecar = [System.IO.File]::ReadAllText($sidecarPath)
    if ($sidecar -notmatch [regex]::Escape($actualHash)) {
        throw 'Completed archive checksum sidecar does not match.'
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -ne [int]$Status.Files) {
            throw 'Completed archive entry count does not match backup status.'
        }
        $levelDat = @(
            $entries | Where-Object {
                $_.FullName -ieq 'world/level.dat'
            }
        )
        if ($levelDat.Count -ne 1) {
            throw 'Completed archive must contain exactly one world/level.dat.'
        }
        if (@(
                $entries | Where-Object {
                    $_.FullName -match '(^|/)session\.lock$'
                }
            ).Count -ne 0) {
            throw 'Completed archive unexpectedly contains session.lock.'
        }
    }
    finally {
        $archive.Dispose()
    }

    return [pscustomobject]@{
        Archive = $resolvedArchive
        Sidecar = $sidecarPath
        Sha256 = $actualHash
        Files = [int]$Status.Files
        ArchiveBytes = (Get-Item -LiteralPath $resolvedArchive).Length
    }
}

$startedAtUtc = (Get-Date).ToUniversalTime()
$savingDisabled = $false
$horrorProcess = $null
$token = $null
$snapshotId = $null
try {
    Assert-HorrorPrankLayout
    $horrorProcess = Get-HorrorPrankProcess
    $originalPid = [int]$horrorProcess.ProcessId

    $listProof = Send-ConsoleCommandAndWait `
        -ProcessId $originalPid `
        -Command 'list' `
        -ProofPattern 'There are \d+ of a max of \d+ players online'
    $playerMatch = [regex]::Match(
        $listProof,
        'There are (?<count>\d+) of a max of \d+ players online')
    if (-not $playerMatch.Success -or
        [int]$playerMatch.Groups['count'].Value -ne 0) {
        throw 'HorrorPrank has connected players; backup was not started.'
    }

    $stalePartials = @(
        Get-ChildItem -LiteralPath $backupDirectory `
            -Filter "$serverId-backup-*.partial" `
            -File `
            -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName
    )

    $null = Send-ConsoleCommandAndWait `
        -ProcessId $originalPid `
        -Command 'save-all flush' `
        -ProofPattern 'Saved the game'
    $null = Send-ConsoleCommandAndWait `
        -ProcessId $originalPid `
        -Command 'save-off' `
        -ProofPattern 'Automatic saving is now disabled'
    $savingDisabled = $true

    try {
        & $backupEngine `
            -ServerId $serverId `
            -ServerDirectory $serverDirectory `
            -WorldFolders 'world' `
            -BackupDirectory $backupDirectory `
            -RetentionCount 1 `
            -ReserveBytes 1GB | Out-Null

        $active = Read-JsonFile -LiteralPath $activePath
        if ([string]$active.ServerId -ne $serverId -or
            [string]$active.Token -notmatch '^[0-9a-f]{32}$') {
            throw 'Backup engine returned an invalid active state.'
        }
        $token = [string]$active.Token
        $snapshotId = [string]$active.SnapshotId
    }
    finally {
        if ($savingDisabled) {
            $null = Send-ConsoleCommandAndWait `
                -ProcessId $originalPid `
                -Command 'save-on' `
                -ProofPattern 'Automatic saving is now enabled'
            $savingDisabled = $false
        }
    }

    $status = Wait-ForBackupCompletion -Token $token
    $archiveProof = Assert-CompletedArchive -Status $status

    foreach ($partial in $stalePartials) {
        $resolvedPartial = [System.IO.Path]::GetFullPath($partial)
        if ($resolvedPartial.StartsWith(
                "$([System.IO.Path]::GetFullPath($backupDirectory).TrimEnd('\'))\",
                [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedPartial) -like
                "$serverId-backup-*.partial" -and
            (Test-Path -LiteralPath $resolvedPartial -PathType Leaf)) {
            Remove-Item -LiteralPath $resolvedPartial -Force
        }
    }

    if (Test-Path -LiteralPath $activePath) {
        throw 'Backup active state remains after successful completion.'
    }
    if (@(
            Get-CimInstance Win32_ShadowCopy -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.ID -eq $snapshotId
                }
        ).Count -ne 0) {
        throw 'Backup VSS snapshot remains after successful completion.'
    }

    $currentProcess = Get-HorrorPrankProcess
    if ([int]$currentProcess.ProcessId -ne $originalPid) {
        throw 'HorrorPrank restarted during the backup.'
    }
    $metricsAgeSeconds = (
        (Get-Date).ToUniversalTime() -
        (Get-Item -LiteralPath $metricsPath).LastWriteTimeUtc
    ).TotalSeconds
    if ($metricsAgeSeconds -gt 120) {
        throw 'HorrorPrank metrics are stale after the backup.'
    }

    $result = [ordered]@{
        SchemaVersion = 1
        ServerId = $serverId
        DisplayName = 'HorrorPrank'
        State = 'Completed'
        StartedAtUtc = $startedAtUtc.ToString('o')
        CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        ServerDirectory = $serverDirectory
        JavaExecutable = $expectedJava
        JavaPidBefore = $originalPid
        JavaPidAfter = [int]$currentProcess.ProcessId
        TruePvpDirectory = $truePvpDirectory
        TruePvpTouched = $false
        ServerRestarted = $false
        PlayersAtStart = 0
        Archive = $archiveProof.Archive
        Sidecar = $archiveProof.Sidecar
        Sha256 = $archiveProof.Sha256
        Files = $archiveProof.Files
        ArchiveBytes = $archiveProof.ArchiveBytes
        MetricsAgeSeconds = [math]::Round($metricsAgeSeconds, 3)
    }
    Write-AtomicJson -LiteralPath $acceptancePath -Value $result
    [pscustomobject]$result
}
catch {
    if ($savingDisabled -and $null -ne $horrorProcess) {
        try {
            $null = Send-ConsoleCommandAndWait `
                -ProcessId ([int]$horrorProcess.ProcessId) `
                -Command 'save-on' `
                -ProofPattern 'Automatic saving is now enabled'
            $savingDisabled = $false
        }
        catch {
            Write-Error (
                'CRITICAL: automatic saving could not be restored: ' +
                $_.Exception.Message)
        }
    }

    $failure = [ordered]@{
        SchemaVersion = 1
        ServerId = $serverId
        DisplayName = 'HorrorPrank'
        State = 'Failed'
        StartedAtUtc = $startedAtUtc.ToString('o')
        FailedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        ServerDirectory = $serverDirectory
        TruePvpDirectory = $truePvpDirectory
        TruePvpTouched = $false
        ServerRestarted = $false
        Error = $_.Exception.Message
    }
    Write-AtomicJson -LiteralPath $acceptancePath -Value $failure
    throw
}
