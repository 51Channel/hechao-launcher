[CmdletBinding()]
param(
    [string]$VelocityRoot = 'E:\Velocity',
    [string]$TaskName = 'Codex-Velocity-Live',
    [int]$Port = 25577,
    [string]$BackupRoot = 'E:\manual-backups',
    [ValidateRange(15, 180)]
    [int]$StartupTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$pluginsDirectory = Join-Path $VelocityRoot 'plugins'
$velocityConfigurationPath = Join-Path $VelocityRoot 'velocity.toml'
$legacyPatterns = @(
    'HubCommand-*.jar',
    'ViaVersion-*.jar',
    'ViaBackwards-*.jar'
)

function Wait-ForPortState {
    param(
        [Parameter(Mandatory)]
        [bool]$Listening
    )

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    do {
        $current = @(
            Get-NetTCPConnection `
                -LocalPort $Port `
                -State Listen `
                -ErrorAction SilentlyContinue
        ).Count -gt 0
        if ($current -eq $Listening) {
            return
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Velocity port $Port did not reach listening=$Listening."
}

function Start-Velocity {
    Start-ScheduledTask -TaskName $TaskName
    Wait-ForPortState -Listening $true
}

if (-not (Test-Path -LiteralPath $pluginsDirectory -PathType Container) -or
    -not (Test-Path -LiteralPath $velocityConfigurationPath -PathType Leaf)) {
    throw 'Velocity installation is incomplete.'
}

$lobbyMapping = @(
    Get-Content -LiteralPath $velocityConfigurationPath |
        Where-Object { $_ -match '^\s*lobby\s*=\s*"127\.0\.0\.1:25566"\s*$' }
)
if ($lobbyMapping.Count -ne 1) {
    throw 'Velocity lobby must remain mapped exactly to 127.0.0.1:25566.'
}

$authorizerConfiguration = Join-Path $pluginsDirectory `
    'hechao-velocity-authorizer\config.properties'
$internalTargetLines = @(
    Get-Content -LiteralPath $authorizerConfiguration -ErrorAction Stop |
        Where-Object { $_ -match '^infrastructure-targets=' }
)
if ($internalTargetLines.Count -ne 1) {
    throw 'Authorizer infrastructure target configuration is missing or duplicated.'
}
$protectedTargets = @(
    $internalTargetLines[0].Split('=', 2)[1].Split(',') |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { $_ }
)
if ($protectedTargets -notcontains 'lobby') {
    throw 'Authorizer does not protect lobby as an infrastructure target.'
}

$establishedConnections = @(
    Get-NetTCPConnection `
        -LocalPort $Port `
        -State Established `
        -ErrorAction SilentlyContinue
).Count
if ($establishedConnections -ne 0) {
    throw "Velocity has $establishedConnections established connection(s)."
}

$legacyJars = @(
    foreach ($pattern in $legacyPatterns) {
        Get-ChildItem -LiteralPath $pluginsDirectory -Filter $pattern -File
    }
)
if ($legacyJars.Count -eq 0) {
    [pscustomobject]@{
        Changed = $false
        Removed = @()
        Port = $Port
        EstablishedConnections = $establishedConnections
    } | ConvertTo-Json -Compress
    return
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "LegacyLobbyRouting-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
Copy-Item -LiteralPath $velocityConfigurationPath -Destination $backupDirectory
foreach ($jar in $legacyJars) {
    Copy-Item -LiteralPath $jar.FullName -Destination $backupDirectory
}

$manifest = @(
    Get-ChildItem -LiteralPath $backupDirectory -File |
        ForEach-Object {
            $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
        }
)
Set-Content `
    -LiteralPath (Join-Path $backupDirectory 'manifest.sha256') `
    -Value $manifest `
    -Encoding ASCII

$removed = [Collections.Generic.List[string]]::new()
$mutationStarted = $false
try {
    Stop-ScheduledTask -TaskName $TaskName
    Wait-ForPortState -Listening $false
    $mutationStarted = $true

    foreach ($jar in $legacyJars) {
        Remove-Item -LiteralPath $jar.FullName
        $removed.Add($jar.Name)
    }

    Start-Velocity

    $latestLogPath = Join-Path $VelocityRoot 'logs\latest.log'
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $authorizerLoaded = $false
    do {
        $log = Get-Content -LiteralPath $latestLogPath -Raw -ErrorAction SilentlyContinue
        $authorizerLoaded = $log -match 'hechao-velocity-authorizer 0\.4\.0' -and
            $log -match 'Hechao authorization initialized'
        if (-not $authorizerLoaded) {
            Start-Sleep -Milliseconds 500
        }
    } while (-not $authorizerLoaded -and (Get-Date) -lt $deadline)
    if (-not $authorizerLoaded) {
        throw 'Velocity did not confirm Authorizer 0.4.0 after legacy routing removal.'
    }

    $remaining = @(
        foreach ($pattern in $legacyPatterns) {
            Get-ChildItem -LiteralPath $pluginsDirectory -Filter $pattern -File
        }
    )
    if ($remaining.Count -ne 0) {
        throw 'A legacy lobby-routing JAR remains active.'
    }
}
catch {
    if ($mutationStarted) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        try {
            Wait-ForPortState -Listening $false
        }
        catch {
        }
        foreach ($jar in $legacyJars) {
            Copy-Item `
                -LiteralPath (Join-Path $backupDirectory $jar.Name) `
                -Destination $jar.FullName `
                -Force
        }
        Start-Velocity
    }
    throw
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen |
    Select-Object -First 1
[pscustomobject]@{
    Changed = $true
    Removed = @($removed)
    BackupDirectory = $backupDirectory
    Port = $Port
    ProcessId = $listener.OwningProcess
    EstablishedConnections = @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Established `
            -ErrorAction SilentlyContinue
    ).Count
} | ConvertTo-Json -Compress
