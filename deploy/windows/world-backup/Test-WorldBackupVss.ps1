[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnginePath,

    [Parameter(Mandatory = $true)]
    [string]$ServerDirectory,

    [Parameter(Mandatory = $true)]
    [string[]]$WorldFolders,

    [Parameter(Mandatory = $true)]
    [string]$WorkingDirectory,

    [ValidateRange(30, 900)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$resolvedEnginePath = [System.IO.Path]::GetFullPath($EnginePath)
$resolvedWorkingDirectory = [System.IO.Path]::GetFullPath(
    $WorkingDirectory).TrimEnd('\')
$resultPath = Join-Path $resolvedWorkingDirectory 'result.json'
$stateDirectory = Join-Path $resolvedWorkingDirectory 'state'
$backupDirectory = Join-Path $resolvedWorkingDirectory 'output'
$logDirectory = Join-Path $resolvedWorkingDirectory 'logs'

function Write-TestResult {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    New-Item -ItemType Directory -Path $resolvedWorkingDirectory -Force |
        Out-Null
    [System.IO.File]::WriteAllText(
        $resultPath,
        ($Value | ConvertTo-Json -Depth 8 -Compress),
        $utf8NoBom)
}

try {
    if (-not (Test-Path -LiteralPath $resolvedEnginePath -PathType Leaf)) {
        throw "Backup engine is missing: $resolvedEnginePath"
    }
    New-Item -ItemType Directory -Path $resolvedWorkingDirectory -Force |
        Out-Null

    $queued = & $resolvedEnginePath `
        -ServerId 'vss-smoke' `
        -ServerDirectory $ServerDirectory `
        -WorldFolders $WorldFolders `
        -BackupDirectory $backupDirectory `
        -RetentionCount 1 `
        -ReserveBytes 268435456 `
        -WorkerStartTimeoutSeconds 30 `
        -StateDirectory $stateDirectory `
        -LogDirectory $logDirectory

    $statusPath = Join-Path $stateDirectory 'vss-smoke.status.json'
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $status = $null
    do {
        Start-Sleep -Milliseconds 250
        if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
            $status = [System.IO.File]::ReadAllText($statusPath) |
                ConvertFrom-Json
            if ($status.State -in @('Completed', 'Failed')) {
                break
            }
        }
    } while ((Get-Date) -lt $deadline)

    if ($null -eq $status -or
        $status.State -notin @('Completed', 'Failed')) {
        throw "Timed out waiting for the VSS smoke backup after $TimeoutSeconds seconds."
    }
    if ($status.State -ne 'Completed') {
        throw "The VSS smoke backup failed: $($status.Error)"
    }

    $archive = Get-Item -LiteralPath ([string]$status.Archive)
    $checksumPath = "$($archive.FullName).sha256"
    if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw 'The VSS smoke backup checksum sidecar is missing.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive.FullName)
    try {
        $entryCount = $zip.Entries.Count
        if ($entryCount -le 0) {
            throw 'The VSS smoke backup archive is empty.'
        }
    }
    finally {
        $zip.Dispose()
    }

    $sha256 = (Get-FileHash -LiteralPath $archive.FullName `
            -Algorithm SHA256).Hash
    $sidecar = [System.IO.File]::ReadAllText($checksumPath).Trim()
    if ($sidecar -notlike "$sha256 *") {
        throw 'The VSS smoke backup checksum sidecar does not match.'
    }
    if (Test-Path -LiteralPath (Join-Path $stateDirectory 'active.json')) {
        throw 'The VSS smoke backup left an active state file.'
    }
    $shadowExists = $null -ne (Get-CimInstance Win32_ShadowCopy |
            Where-Object { $_.ID -eq [string]$queued.SnapshotId } |
            Select-Object -First 1)
    if ($shadowExists) {
        throw 'The VSS smoke backup left its shadow copy mounted.'
    }

    Write-TestResult -Value ([ordered]@{
            State = 'Passed'
            CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            Coordinator = $queued
            Archive = $archive.FullName
            ArchiveBytes = [long]$archive.Length
            EntryCount = $entryCount
            Sha256 = $sha256
            ChecksumPath = $checksumPath
            ActiveStateRemoved = $true
            ShadowCopyRemoved = $true
        })
}
catch {
    Write-TestResult -Value ([ordered]@{
            State = 'Failed'
            FailedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            Error = $_.Exception.Message
            ScriptStackTrace = $_.ScriptStackTrace
        })
    throw
}
