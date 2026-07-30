#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$HostName,

    [ValidateRange(1, 65535)]
    [int]$Port = 22,

    [string]$UserName = "administrator",

    [Parameter(Mandatory)]
    [string]$IdentityFile,

    [Parameter(Mandatory)]
    [ValidateSet("monitor", "enforce")]
    [string]$DesiredMode,

    [string]$PreflightEvidencePath,

    [string]$VelocityRoot = "E:\Velocity",

    [string]$TaskName = "Codex-Velocity-Live",

    [ValidateRange(1, 65535)]
    [int]$VelocityPort = 25577,

    [string]$BackupRoot = "E:\manual-backups",

    [string]$OutputPath,

    [switch]$Apply,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
    throw "SSH identity file does not exist: $IdentityFile"
}
if ($HostName -notmatch "^[A-Za-z0-9.-]+$" -or
    $UserName -notmatch "^[A-Za-z0-9._-]+$") {
    throw "SSH host or user name contains unsupported characters."
}
foreach ($remotePath in @($VelocityRoot, $BackupRoot)) {
    if ($remotePath -notmatch "^[A-Za-z]:\\[A-Za-z0-9 ._\\-]+$") {
        throw "Remote path contains unsupported characters: $remotePath"
    }
}
if ($TaskName -notmatch "^[A-Za-z0-9 ._-]+$") {
    throw "TaskName contains unsupported characters."
}
if ($Apply -and [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw "OutputPath is required when Apply is specified."
}

$gateResult = $null
if ($DesiredMode -eq "enforce") {
    if ([string]::IsNullOrWhiteSpace($PreflightEvidencePath) -or
        -not (Test-Path -LiteralPath $PreflightEvidencePath -PathType Leaf)) {
        throw "Passing gray-pilot evidence is required for enforce mode."
    }

    $gateScript = Join-Path (
        Split-Path -Parent (Split-Path -Parent $PSCommandPath)
    ) "acceptance\Test-HechaoAuthorizerEnforceGate.ps1"
    $gateOutput = & (Join-Path $PSHOME "pwsh.exe") `
        -NoLogo `
        -NoProfile `
        -File $gateScript `
        -EvidencePath $PreflightEvidencePath `
        -AsJson
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Gray-pilot evidence failed the enforce gate. " +
            "Production remains in monitor mode."
        )
    }
    $gateResult = ($gateOutput -join [Environment]::NewLine) |
        ConvertFrom-Json
}

$statusTool = Join-Path $PSScriptRoot (
    "Get-HechaoLauncherOnlyProductionStatus.ps1"
)
$beforeStatus = & $statusTool `
    -HostName $HostName `
    -Port $Port `
    -UserName $UserName `
    -IdentityFile $IdentityFile

if ($beforeStatus.authorizerMode -notin @("monitor", "enforce")) {
    throw "Current Authorizer mode is missing or unsupported."
}
if (-not [bool]$beforeStatus.authorizer.exists -or
    @($beforeStatus.velocityListeners).Count -ne 1) {
    throw "Velocity Authorizer or its listener is not healthy."
}
if ($beforeStatus.infrastructureTargets -notmatch (
    "(^|,)\s*lobby\s*(,|$)"
)) {
    throw "Lobby is not configured as an infrastructure target."
}
if ($beforeStatus.lobbyServerIp -ne "127.0.0.1" -or
    $beforeStatus.lobbyWhitelistEnabled -ne "true" -or
    $beforeStatus.lobbyEnforceWhitelist -ne "true" -or
    [int]$beforeStatus.lobbyWhitelistEntries -ne 0) {
    throw "Lobby isolation is not healthy; refusing mode change."
}

$changed = $false
$remoteResult = $null
$remoteScript = @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = "Stop"
`$ProgressPreference = "SilentlyContinue"

`$desiredMode = "$DesiredMode"
`$velocityRoot = "$VelocityRoot"
`$taskName = "$TaskName"
`$velocityPort = $VelocityPort
`$backupRoot = "$BackupRoot"
`$configurationPath = Join-Path `$velocityRoot (
    "plugins\hechao-velocity-authorizer\config.properties"
)

function Get-Mode {
    `$modeLine = Get-Content -LiteralPath `$configurationPath -Encoding utf8 |
        Where-Object { `$_ -match "^\s*mode\s*=" } |
        Select-Object -Last 1
    if (`$null -eq `$modeLine) {
        throw "Authorizer mode is missing."
    }
    return (`$modeLine -split "=", 2)[1].Trim().ToLowerInvariant()
}

function Set-Mode {
    param([Parameter(Mandatory)][string]`$Mode)

    `$lines = @(Get-Content -LiteralPath `$configurationPath -Encoding utf8)
    `$matched = 0
    `$updated = @(foreach (`$line in `$lines) {
        if (`$line -match "^\s*mode\s*=") {
            `$matched++
            if (`$matched -eq 1) {
                "mode=`$Mode"
            }
        } else {
            `$line
        }
    })
    if (`$matched -ne 1) {
        throw "Authorizer mode property must appear exactly once."
    }

    `$temporaryPath = "`$configurationPath.`$(
        [guid]::NewGuid().ToString("N")
    ).tmp"
    try {
        [IO.File]::WriteAllLines(
            `$temporaryPath,
            `$updated,
            [Text.UTF8Encoding]::new(`$false)
        )
        Move-Item ``
            -LiteralPath `$temporaryPath ``
            -Destination `$configurationPath ``
            -Force
    } finally {
        Remove-Item ``
            -LiteralPath `$temporaryPath ``
            -Force ``
            -ErrorAction SilentlyContinue
    }
}

function Wait-Port {
    param(
        [Parameter(Mandatory)][bool]`$Listening,
        [int]`$TimeoutSeconds = 60
    )

    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(`$TimeoutSeconds)
    do {
        `$present = @(
            Get-NetTCPConnection ``
                -LocalPort `$velocityPort ``
                -State Listen ``
                -ErrorAction SilentlyContinue
        ).Count -gt 0
        if (`$present -eq `$Listening) {
            return
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt `$deadline)

    throw "Velocity listener did not reach listening=`$Listening."
}

function Start-And-Verify {
    param([Parameter(Mandatory)][string]`$ExpectedMode)

    `$startedAt = [DateTimeOffset]::UtcNow
    Start-ScheduledTask -TaskName `$taskName
    Wait-Port -Listening `$true
    `$latestLog = Join-Path `$velocityRoot "logs\latest.log"
    `$pattern = "Hechao authorization initialized in `$ExpectedMode mode"
    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        `$logItem = Get-Item ``
            -LiteralPath `$latestLog ``
            -ErrorAction SilentlyContinue
        `$logText = Get-Content ``
            -LiteralPath `$latestLog ``
            -Raw ``
            -ErrorAction SilentlyContinue
        if (`$null -ne `$logItem -and
            `$logItem.LastWriteTimeUtc -ge `$startedAt.UtcDateTime -and
            `$logText -match [regex]::Escape(`$pattern)) {
            return
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt `$deadline)

    throw "Velocity log did not confirm `$ExpectedMode mode."
}

if (-not (Test-Path -LiteralPath `$configurationPath -PathType Leaf)) {
    throw "Authorizer configuration is missing."
}
`$currentMode = Get-Mode
if (`$currentMode -notin @("monitor", "enforce")) {
    throw "Current Authorizer mode is unsupported."
}
if (`$currentMode -eq `$desiredMode) {
    [ordered]@{
        changed = `$false
        previousMode = `$currentMode
        currentMode = `$currentMode
        backupDirectory = `$null
        processId = (
            Get-NetTCPConnection ``
                -LocalPort `$velocityPort ``
                -State Listen |
                Select-Object -First 1 -ExpandProperty OwningProcess
        )
    } | ConvertTo-Json -Compress
    exit 0
}

`$establishedConnections = @(
    Get-NetTCPConnection ``
        -LocalPort `$velocityPort ``
        -State Established ``
        -ErrorAction SilentlyContinue
).Count
if (`$establishedConnections -ne 0) {
    throw (
        "Velocity has `$establishedConnections established connection(s); " +
        "refusing restart."
    )
}

`$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
`$backupDirectory = Join-Path `$backupRoot (
    "VelocityAuthorizerMode-`$currentMode-to-`$desiredMode-`$timestamp"
)
[IO.Directory]::CreateDirectory(`$backupDirectory) | Out-Null
`$backupConfig = Join-Path `$backupDirectory "config.properties"
Copy-Item -LiteralPath `$configurationPath -Destination `$backupConfig
`$backupHash = (
    Get-FileHash -LiteralPath `$backupConfig -Algorithm SHA256
).Hash
[IO.File]::WriteAllText(
    (Join-Path `$backupDirectory "manifest.sha256"),
    "`$(`$backupHash.ToLowerInvariant())  config.properties`r`n",
    [Text.Encoding]::ASCII
)

`$configurationChanged = `$false
try {
    Set-Mode -Mode `$desiredMode
    `$configurationChanged = `$true
    if ((Get-Mode) -ne `$desiredMode) {
        throw "Authorizer mode write verification failed."
    }

    Stop-ScheduledTask -TaskName `$taskName
    Wait-Port -Listening `$false
    Start-And-Verify -ExpectedMode `$desiredMode
} catch {
    if (`$configurationChanged) {
        Stop-ScheduledTask -TaskName `$taskName -ErrorAction SilentlyContinue
        try {
            Wait-Port -Listening `$false -TimeoutSeconds 30
        } catch {
        }
        Copy-Item ``
            -LiteralPath `$backupConfig ``
            -Destination `$configurationPath ``
            -Force
        Start-And-Verify -ExpectedMode `$currentMode
    }
    throw
}

`$listener = Get-NetTCPConnection ``
    -LocalPort `$velocityPort ``
    -State Listen |
    Select-Object -First 1
[ordered]@{
    changed = `$true
    previousMode = `$currentMode
    currentMode = Get-Mode
    backupDirectory = `$backupDirectory
    backupSha256 = `$backupHash
    processId = `$listener.OwningProcess
    establishedConnections = @(
        Get-NetTCPConnection ``
            -LocalPort `$velocityPort ``
            -State Established ``
            -ErrorAction SilentlyContinue
    ).Count
} | ConvertTo-Json -Compress
"@
$remoteTokens = $null
$remoteParseErrors = $null
[Management.Automation.Language.Parser]::ParseInput(
    $remoteScript,
    [ref]$remoteTokens,
    [ref]$remoteParseErrors
) | Out-Null
if ($remoteParseErrors.Count -gt 0) {
    $parseSummary = @(
        $remoteParseErrors |
            ForEach-Object {
                "line {0}: {1}" -f
                    $_.Extent.StartLineNumber,
                    (
                        "{0} [{1}]" -f
                            $_.Message,
                            $_.Extent.Text
                    )
            }
    ) -join "; "
    $remoteLines = @($remoteScript -split "\r?\n")
    $firstErrorLine = [int]$remoteParseErrors[0].Extent.StartLineNumber
    $contextStart = [math]::Max(0, $firstErrorLine - 4)
    $contextEnd = [math]::Min(
        $remoteLines.Count - 1,
        $firstErrorLine + 4
    )
    $parseContext = @(
        for ($index = $contextStart; $index -le $contextEnd; $index++) {
            "{0}: {1}" -f ($index + 1), $remoteLines[$index]
        }
    ) -join [Environment]::NewLine
    throw (
        "Generated remote Authorizer transaction is not valid " +
        "PowerShell: $parseSummary$([Environment]::NewLine)$parseContext"
    )
}

if ($Apply -and $beforeStatus.authorizerMode -ne $DesiredMode) {
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($remoteScript)
    )
    $sshArguments = @(
        "-i", (Resolve-Path -LiteralPath $IdentityFile).Path,
        "-p", $Port.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        ),
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes",
        "$UserName@$HostName",
        (
            "C:\Progra~1\PowerShell\7\pwsh.exe " +
            "-NoLogo -NoProfile -EncodedCommand $encodedCommand"
        )
    )
    $remoteOutput = & ssh.exe @sshArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Remote Authorizer mode transaction failed. " +
            "The remote rollback path was invoked."
        )
    }
    $remoteResult = ($remoteOutput -join [Environment]::NewLine) |
        ConvertFrom-Json
    $changed = [bool]$remoteResult.changed
}

$afterStatus = if ($Apply) {
    & $statusTool `
        -HostName $HostName `
        -Port $Port `
        -UserName $UserName `
        -IdentityFile $IdentityFile
} else {
    $beforeStatus
}

if ($Apply -and $afterStatus.authorizerMode -ne $DesiredMode) {
    throw "Post-change Authorizer mode verification failed."
}

$result = [ordered]@{
    schemaVersion = 1
    status = if (-not $Apply) {
        "eligible-dry-run"
    } elseif ($changed) {
        "changed"
    } else {
        "already-in-desired-mode"
    }
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    host = $HostName
    desiredMode = $DesiredMode
    previousMode = [string]$beforeStatus.authorizerMode
    currentMode = [string]$afterStatus.authorizerMode
    applied = [bool]$Apply
    changed = $changed
    preflightGatePassed = if ($DesiredMode -eq "enforce") {
        [bool]$gateResult.passed
    } else {
        $null
    }
    backupDirectory = if ($null -eq $remoteResult) {
        $null
    } else {
        $remoteResult.backupDirectory
    }
    backupSha256 = if ($null -eq $remoteResult) {
        $null
    } else {
        $remoteResult.backupSha256
    }
    processId = if ($null -eq $remoteResult) {
        @($afterStatus.velocityListeners)[0].OwningProcess
    } else {
        $remoteResult.processId
    }
    rollback = (
        "Restore config.properties from backup and restart only " +
        "$TaskName; the transaction does this automatically on failure."
    )
}

if ($Apply) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory(
        (Split-Path -Parent $resolvedOutput)
    ) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedOutput,
        ($result | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 6 -Compress
} else {
    [pscustomobject]$result
}
