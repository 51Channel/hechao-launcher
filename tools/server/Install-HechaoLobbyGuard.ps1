[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedSha256,

    [string]$LobbyRoot = 'E:\LobbyServer',
    [string]$VelocityRoot = 'E:\Velocity',
    [string]$TaskName = 'Hechao-Server-Lobby',
    [int]$Port = 25566,
    [string]$BackupRoot = 'E:\manual-backups',
    [string]$ConsoleBridge =
        'C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1',
    [int]$ShutdownTimeoutSeconds = 90,
    [int]$StartupTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$resolvedLobbyRoot = [System.IO.Path]::GetFullPath($LobbyRoot)
$resolvedVelocityRoot = [System.IO.Path]::GetFullPath($VelocityRoot)
$resolvedBackupRoot = [System.IO.Path]::GetFullPath($BackupRoot)
$pluginsDirectory = Join-Path $resolvedLobbyRoot 'plugins'
$incomingPath =
    Join-Path $pluginsDirectory "HechaoLobbyGuard-$Version.jar.incoming"
$destinationPath =
    Join-Path $pluginsDirectory "HechaoLobbyGuard-$Version.jar"
$serverPropertiesPath = Join-Path $resolvedLobbyRoot 'server.properties'
$whitelistPath = Join-Path $resolvedLobbyRoot 'whitelist.json'
$velocityConfigurationPath =
    Join-Path $resolvedVelocityRoot 'velocity.toml'
$latestLogPath = Join-Path $resolvedLobbyRoot 'logs\latest.log'

function Read-SharedText {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ''
    }
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        (
            [System.IO.FileShare]::ReadWrite -bor
            [System.IO.FileShare]::Delete
        ))
    try {
        $reader = New-Object System.IO.StreamReader(
            $stream,
            [System.Text.Encoding]::UTF8,
            $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-Listener {
    return @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Listen `
            -ErrorAction SilentlyContinue
    )
}

function Wait-ForListener {
    param(
        [Parameter(Mandatory = $true)][bool]$Present,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $listeners = @(Get-Listener)
        if (($listeners.Count -gt 0) -eq $Present) {
            return $listeners
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Lobby port $Port did not reach listening=$Present."
}

function Send-LobbyCommand {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Command
    )

    & $ConsoleBridge `
        -ProcessId $ProcessId `
        -Command $Command `
        -TimeoutSeconds 30 | Out-Null
}

function Wait-ForTaskStopped {
    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($ShutdownTimeoutSeconds)
    do {
        $task = Get-ScheduledTask `
            -TaskName $TaskName `
            -ErrorAction SilentlyContinue
        if ($null -eq $task -or $task.State -ne 'Running') {
            return
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Lobby task $TaskName did not stop."
}

function Stop-LobbyGracefully {
    $listeners = @(Get-Listener)
    if ($listeners.Count -eq 0) {
        return
    }
    if ($listeners.Count -ne 1) {
        throw "Expected one Lobby listener, found $($listeners.Count)."
    }

    $processId = [int]$listeners[0].OwningProcess
    $beforeLog = Read-SharedText -Path $latestLogPath
    Send-LobbyCommand -ProcessId $processId -Command 'save-all flush'

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($ShutdownTimeoutSeconds)
    $saved = $false
    do {
        Start-Sleep -Milliseconds 500
        $currentLog = Read-SharedText -Path $latestLogPath
        $newText = if ($currentLog.StartsWith($beforeLog)) {
            $currentLog.Substring($beforeLog.Length)
        }
        else {
            $currentLog
        }
        $saved = $newText -match '(?i)Saved the game|Saved all worlds'
    } while (-not $saved -and [DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $saved) {
        throw 'Lobby did not confirm save-all flush.'
    }

    Send-LobbyCommand -ProcessId $processId -Command 'stop'
    Wait-ForListener `
        -Present $false `
        -TimeoutSeconds $ShutdownTimeoutSeconds | Out-Null
    Wait-ForTaskStopped
}

function Stop-LobbyForRollback {
    $listeners = @(Get-Listener)
    if ($listeners.Count -eq 1) {
        try {
            Send-LobbyCommand `
                -ProcessId ([int]$listeners[0].OwningProcess) `
                -Command 'stop'
            Wait-ForListener `
                -Present $false `
                -TimeoutSeconds $ShutdownTimeoutSeconds | Out-Null
            return
        }
        catch {
        }
    }

    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Wait-ForListener `
        -Present $false `
        -TimeoutSeconds $ShutdownTimeoutSeconds | Out-Null
    Wait-ForTaskStopped
}

function Start-LobbyAndValidate {
    param([Parameter(Mandatory = $true)][bool]$RequireGuard)

    $startedAt = [DateTimeOffset]::UtcNow
    Start-ScheduledTask -TaskName $TaskName
    $listeners = @(
        Wait-ForListener `
            -Present $true `
            -TimeoutSeconds $StartupTimeoutSeconds
    )
    if ($listeners.Count -ne 1) {
        throw "Expected one Lobby listener, found $($listeners.Count)."
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $ready = $false
    $guardLoaded = -not $RequireGuard
    $logText = ''
    do {
        Start-Sleep -Milliseconds 500
        $logText = Read-SharedText -Path $latestLogPath
        $logFile = Get-Item `
            -LiteralPath $latestLogPath `
            -ErrorAction SilentlyContinue
        $freshLog = $null -ne $logFile -and
            $logFile.LastWriteTimeUtc -ge $startedAt.UtcDateTime.AddSeconds(-2)
        $ready = $freshLog -and $logText -match 'Done \('
        if ($RequireGuard) {
            $guardLoaded =
                $logText -match (
                    '\[HechaoLobbyGuard\].*' +
                    'Player admission is disabled for this infrastructure Lobby'
                )
        }
    } while (
        (-not $ready -or -not $guardLoaded) -and
        [DateTimeOffset]::UtcNow -lt $deadline
    )

    $fatalPattern = (
        '(?im)Failed to start the minecraft server|' +
        'Encountered an unexpected exception|' +
        'FatalStartupException|' +
        'UnsupportedClassVersionError|' +
        'Unable to access jarfile|' +
        'Error loading plugin|' +
        'Invalid plugin'
    )
    if (-not $ready -or $logText -match $fatalPattern) {
        throw 'Lobby did not complete a clean startup.'
    }
    if (-not $guardLoaded) {
        throw "Lobby Guard $Version was not confirmed in the startup log."
    }

    $listener = @(Get-Listener)[0]
    if ($RequireGuard -and
        $listener.LocalAddress -notin @('127.0.0.1', '::1')) {
        throw "Lobby listener is not private: $($listener.LocalAddress)."
    }
    return $listener
}

function Set-ServerProperties {
    $required = [ordered]@{
        'server-ip' = '127.0.0.1'
        'white-list' = 'true'
        'enforce-whitelist' = 'true'
    }
    $lines = @(
        [System.IO.File]::ReadAllLines(
            $serverPropertiesPath,
            [System.Text.Encoding]::UTF8)
    )
    $seen = @{}
    $updated = @(foreach ($line in $lines) {
        $matched = $false
        foreach ($name in $required.Keys) {
            if ($line -match "^$([regex]::Escape($name))=") {
                if (-not $seen.ContainsKey($name)) {
                    "$name=$($required[$name])"
                    $seen[$name] = $true
                }
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            $line
        }
    })
    foreach ($name in $required.Keys) {
        if (-not $seen.ContainsKey($name)) {
            $updated += "$name=$($required[$name])"
        }
    }

    $temporaryPath =
        "$serverPropertiesPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllLines(
            $temporaryPath,
            $updated,
            (New-Object System.Text.UTF8Encoding($false)))
        Move-Item `
            -LiteralPath $temporaryPath `
            -Destination $serverPropertiesPath `
            -Force
    }
    finally {
        Remove-Item `
            -LiteralPath $temporaryPath `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

foreach ($path in @(
        $resolvedLobbyRoot,
        $resolvedVelocityRoot,
        $pluginsDirectory,
        $resolvedBackupRoot
    )) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Required directory does not exist: $path"
    }
}
foreach ($path in @(
        $incomingPath,
        $serverPropertiesPath,
        $velocityConfigurationPath,
        $ConsoleBridge
    )) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file does not exist: $path"
    }
}
Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop | Out-Null

$velocityConfiguration =
    [System.IO.File]::ReadAllText($velocityConfigurationPath)
$expectedBackend = [regex]::Escape("127.0.0.1:$Port")
if ($velocityConfiguration -notmatch (
        "(?m)^\s*lobby\s*=\s*`"$expectedBackend`"\s*$"
    )) {
    throw 'Velocity Lobby target is not the expected private backend.'
}

$incomingHash =
    (Get-FileHash -LiteralPath $incomingPath -Algorithm SHA256).Hash
if ($incomingHash -ne $ExpectedSha256.ToUpperInvariant()) {
    throw 'Incoming Lobby Guard checksum mismatch.'
}

$listeners = @(Get-Listener)
if ($listeners.Count -ne 1) {
    throw "Expected one running Lobby listener, found $($listeners.Count)."
}
$connections = @(
    Get-NetTCPConnection `
        -LocalPort $Port `
        -State Established `
        -ErrorAction SilentlyContinue
)
if ($connections.Count -ne 0) {
    throw "Lobby has $($connections.Count) established player connection(s)."
}

$existingJars = @(
    Get-ChildItem `
        -LiteralPath $pluginsDirectory `
        -Filter 'HechaoLobbyGuard-*.jar' `
        -File
)
if ($existingJars.Count -gt 1) {
    throw "Expected at most one active Lobby Guard, found $($existingJars.Count)."
}

$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupDirectory =
    Join-Path $resolvedBackupRoot "LobbyGuard-$Version-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
Copy-Item -LiteralPath $serverPropertiesPath -Destination $backupDirectory
if (Test-Path -LiteralPath $whitelistPath -PathType Leaf) {
    Copy-Item -LiteralPath $whitelistPath -Destination $backupDirectory
}
foreach ($jar in $existingJars) {
    Copy-Item -LiteralPath $jar.FullName -Destination $backupDirectory
}
$backupManifest = @(
    Get-ChildItem -LiteralPath $backupDirectory -File |
        ForEach-Object {
            $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
        }
)
[System.IO.File]::WriteAllLines(
    (Join-Path $backupDirectory 'manifest.sha256'),
    $backupManifest,
    [System.Text.Encoding]::ASCII)

$originalWhitelistExisted =
    Test-Path -LiteralPath $whitelistPath -PathType Leaf
$mutationStarted = $false
$rollbackSucceeded = $false
try {
    Stop-LobbyGracefully
    $mutationStarted = $true

    foreach ($jar in $existingJars) {
        Remove-Item -LiteralPath $jar.FullName -Force
    }
    Move-Item -LiteralPath $incomingPath -Destination $destinationPath
    Set-ServerProperties
    [System.IO.File]::WriteAllText(
        $whitelistPath,
        "[]`n",
        (New-Object System.Text.UTF8Encoding($false)))

    $listener = Start-LobbyAndValidate -RequireGuard $true
    $connectionsAfter = @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Established `
            -ErrorAction SilentlyContinue
    )
    if ($connectionsAfter.Count -ne 0) {
        throw 'Lobby accepted a player connection during deployment.'
    }

    [pscustomobject]@{
        Version = $Version
        Sha256 = (
            Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256
        ).Hash
        BackupDirectory = $backupDirectory
        ListenerAddress = $listener.LocalAddress
        Port = $Port
        ProcessId = $listener.OwningProcess
        WhitelistEntries = 0
        EstablishedConnections = $connectionsAfter.Count
        RollbackPerformed = $false
    } | ConvertTo-Json -Compress
}
catch {
    $deploymentError = $_.Exception
    if ($mutationStarted) {
        try {
            Stop-LobbyForRollback
            Remove-Item `
                -LiteralPath $destinationPath `
                -Force `
                -ErrorAction SilentlyContinue
            Copy-Item `
                -LiteralPath (
                    Join-Path $backupDirectory 'server.properties'
                ) `
                -Destination $serverPropertiesPath `
                -Force
            if ($originalWhitelistExisted) {
                Copy-Item `
                    -LiteralPath (Join-Path $backupDirectory 'whitelist.json') `
                    -Destination $whitelistPath `
                    -Force
            }
            else {
                Remove-Item `
                    -LiteralPath $whitelistPath `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
            foreach ($jar in $existingJars) {
                Copy-Item `
                    -LiteralPath (Join-Path $backupDirectory $jar.Name) `
                    -Destination $jar.FullName `
                    -Force
            }
            Start-LobbyAndValidate `
                -RequireGuard ($existingJars.Count -eq 1) | Out-Null
            $rollbackSucceeded = $true
        }
        catch {
            throw [System.AggregateException]::new(
                'Lobby Guard deployment and automatic rollback both failed.',
                @($deploymentError, $_.Exception))
        }
    }
    if ($rollbackSucceeded) {
        throw [System.InvalidOperationException]::new(
            'Lobby Guard deployment failed; the previous Lobby state was restored.',
            $deploymentError)
    }
    throw
}
