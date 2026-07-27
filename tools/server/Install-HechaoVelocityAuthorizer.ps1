[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedSha256,

    [string]$VelocityRoot = 'E:\Velocity',
    [string]$TaskName = 'Codex-Velocity-Live',
    [int]$Port = 25577,
    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'
$pluginsDirectory = Join-Path $VelocityRoot 'plugins'
$incomingPath = Join-Path $pluginsDirectory "HechaoVelocityAuthorizer-$Version.jar.incoming"
$destinationPath = Join-Path $pluginsDirectory "HechaoVelocityAuthorizer-$Version.jar"
$configurationPath = Join-Path $pluginsDirectory 'hechao-velocity-authorizer\config.properties'
$velocityConfigurationPath = Join-Path $VelocityRoot 'velocity.toml'

function Wait-ForPortState {
    param(
        [bool]$Listening,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $isListening = @(
            Get-NetTCPConnection `
                -LocalPort $Port `
                -State Listen `
                -ErrorAction SilentlyContinue
        ).Count -gt 0
        if ($isListening -eq $Listening) {
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

if (-not (Test-Path -LiteralPath $incomingPath -PathType Leaf)) {
    throw "Incoming JAR not found: $incomingPath"
}
if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $velocityConfigurationPath -PathType Leaf)) {
    throw 'Velocity configuration files are incomplete.'
}

$incomingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $incomingPath).Hash
if ($incomingHash -ne $ExpectedSha256.ToUpperInvariant()) {
    throw 'Incoming JAR checksum mismatch.'
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

$existingJars = @(
    Get-ChildItem `
        -LiteralPath $pluginsDirectory `
        -Filter 'HechaoVelocityAuthorizer-*.jar' `
        -File
)
if ($existingJars.Count -ne 1) {
    throw "Expected exactly one active authorizer JAR, found $($existingJars.Count)."
}
$previousJar = $existingJars[0]

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "VelocityAuthorizer-$Version-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
Copy-Item -LiteralPath $previousJar.FullName -Destination $backupDirectory
Copy-Item -LiteralPath $configurationPath -Destination $backupDirectory
Copy-Item -LiteralPath $velocityConfigurationPath -Destination $backupDirectory

$backupManifest = @(
    Get-ChildItem -LiteralPath $backupDirectory -File |
        ForEach-Object {
            $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
            "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
        }
)
Set-Content `
    -LiteralPath (Join-Path $backupDirectory 'manifest.sha256') `
    -Value $backupManifest `
    -Encoding ASCII

$replacementActivated = $false
try {
    Stop-ScheduledTask -TaskName $TaskName
    Wait-ForPortState -Listening $false

    Remove-Item -LiteralPath $previousJar.FullName
    Move-Item -LiteralPath $incomingPath -Destination $destinationPath
    $replacementActivated = $true

    Start-Velocity
    $latestLogPath = Join-Path $VelocityRoot 'logs\latest.log'
    $deadline = (Get-Date).AddSeconds(30)
    $loaded = $false
    do {
        $logText = Get-Content -LiteralPath $latestLogPath -Raw -ErrorAction SilentlyContinue
        $loaded = $logText -match "hechao-velocity-authorizer $([regex]::Escape($Version))" -and
            $logText -match 'Hechao authorization initialized'
        if (-not $loaded) {
            Start-Sleep -Milliseconds 500
        }
    } while (-not $loaded -and (Get-Date) -lt $deadline)
    if (-not $loaded) {
        throw "Velocity log did not confirm authorizer $Version initialization."
    }
}
catch {
    if ($replacementActivated) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        try {
            Wait-ForPortState -Listening $false -TimeoutSeconds 30
        }
        catch {
        }
        Remove-Item -LiteralPath $destinationPath -Force -ErrorAction SilentlyContinue
        Copy-Item `
            -LiteralPath (Join-Path $backupDirectory $previousJar.Name) `
            -Destination $previousJar.FullName
        Start-Velocity
    }
    throw
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen |
    Select-Object -First 1
$mode = (
    Get-Content -LiteralPath $configurationPath |
        Select-String -Pattern '^mode=' |
        Select-Object -First 1
).Line.Split('=', 2)[1]

[pscustomobject]@{
    Version = $Version
    Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash
    BackupDirectory = $backupDirectory
    Mode = $mode
    Port = $Port
    ProcessId = $listener.OwningProcess
    EstablishedConnections = @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Established `
            -ErrorAction SilentlyContinue
    ).Count
} | ConvertTo-Json -Compress
