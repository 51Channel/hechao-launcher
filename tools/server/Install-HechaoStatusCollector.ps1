#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^[A-Za-z0-9.-]+$")]
    [string]$HostName,

    [ValidateRange(1, 65535)]
    [int]$Port = 22,

    [string]$UserName = "administrator",

    [Parameter(Mandatory)]
    [string]$IdentityFile,

    [Parameter(Mandatory)]
    [string]$KnownHostsFile,

    [Parameter(Mandatory)]
    [string]$ArtifactPath,

    [ValidatePattern("^[a-z0-9][a-z0-9._-]{0,63}$")]
    [string]$AllowStaleMetricsWhenEmptyTarget,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$identity = (Resolve-Path -LiteralPath $IdentityFile).Path
$knownHosts = (Resolve-Path -LiteralPath $KnownHostsFile).Path
$artifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
$artifactItem = Get-Item -LiteralPath $artifact
if ($artifactItem.Extension -ne ".exe") {
    throw "ArtifactPath must point to the collector executable."
}

$artifactHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
$artifactVersion = $artifactItem.VersionInfo.FileVersion
$stagingName = ".staging-$([guid]::NewGuid().ToString('N')).exe"
$remoteStagingWindows =
    "C:\ProgramData\Hechao\StatusCollector\$stagingName"
$remoteStagingScp =
    "C:/ProgramData/Hechao/StatusCollector/$stagingName"

$commonSshArguments = @(
    "-i", $identity,
    "-o", "BatchMode=yes",
    "-o", "StrictHostKeyChecking=yes",
    "-o", "UserKnownHostsFile=$knownHosts",
    "-o", "ConnectTimeout=15"
)

$scpArguments = @(
    "-P", $Port.ToString(
        [Globalization.CultureInfo]::InvariantCulture
    )
) + $commonSshArguments + @(
    $artifact,
    "${UserName}@${HostName}:$remoteStagingScp"
)
& scp.exe @scpArguments
if ($LASTEXITCODE -ne 0) {
    throw "Collector upload failed with exit code $LASTEXITCODE."
}

$targetLiteral = if (
    [string]::IsNullOrWhiteSpace($AllowStaleMetricsWhenEmptyTarget)
) {
    "''"
} else {
    "'$AllowStaleMetricsWhenEmptyTarget'"
}

$remoteScript = @"
#requires -Version 7.4
Set-StrictMode -Version Latest
`$ErrorActionPreference = "Stop"
`$ProgressPreference = "SilentlyContinue"

`$installDirectory = "C:\ProgramData\Hechao\StatusCollector"
`$executablePath = Join-Path `$installDirectory "Hechao.StatusCollector.exe"
`$configurationPath = Join-Path `$installDirectory "server-heartbeats.json"
`$stagingPath = "$remoteStagingWindows"
`$expectedHash = "$artifactHash"
`$pausedTarget = $targetLiteral
`$taskName = "Hechao Launcher Server Heartbeats"
`$backupDirectory = Join-Path (
    Join-Path `$installDirectory "backups"
) ("collector-0.2.2-" + (Get-Date).ToUniversalTime().ToString(
    "yyyyMMddTHHmmssZ"
))
`$replacementPath = Join-Path `$installDirectory (
    ".replacement-" + [guid]::NewGuid().ToString("N") + ".exe"
)
`$configurationReplacementPath = "`$configurationPath.new"
`$taskWasEnabled = `$false
`$backupCreated = `$false

function Get-JavaProcessIds {
    return @(
        Get-CimInstance Win32_Process -Filter "Name = 'java.exe'" |
            Sort-Object ProcessId |
            ForEach-Object { [int]`$_.ProcessId }
    )
}

try {
    if (
        -not (Test-Path -LiteralPath `$executablePath) -or
        -not (Test-Path -LiteralPath `$configurationPath) -or
        -not (Test-Path -LiteralPath `$stagingPath)
    ) {
        throw "Collector installation, configuration, or staging file is missing."
    }

    `$actualStagingHash = (
        Get-FileHash -LiteralPath `$stagingPath -Algorithm SHA256
    ).Hash
    if (`$actualStagingHash -ne `$expectedHash) {
        throw "Uploaded collector SHA-256 does not match."
    }

    `$javaBefore = Get-JavaProcessIds
    `$task = Get-ScheduledTask -TaskName `$taskName -ErrorAction Stop
    `$taskWasEnabled = `$task.State -ne "Disabled"
    Disable-ScheduledTask -TaskName `$taskName | Out-Null

    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        `$task = Get-ScheduledTask -TaskName `$taskName
        if (`$task.State -ne "Running") {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt `$deadline)
    if (`$task.State -eq "Running") {
        throw "Collector task did not become idle within 30 seconds."
    }

    New-Item -ItemType Directory -Path `$backupDirectory -Force |
        Out-Null
    Copy-Item -LiteralPath `$executablePath -Destination (
        Join-Path `$backupDirectory "Hechao.StatusCollector.exe"
    )
    Copy-Item -LiteralPath `$configurationPath -Destination (
        Join-Path `$backupDirectory "server-heartbeats.json"
    )
    `$backupCreated = `$true

    Copy-Item -LiteralPath `$stagingPath -Destination `$replacementPath
    if ((
        Get-FileHash -LiteralPath `$replacementPath -Algorithm SHA256
    ).Hash -ne `$expectedHash) {
        throw "Same-volume collector replacement SHA-256 does not match."
    }

    if (-not [string]::IsNullOrWhiteSpace(`$pausedTarget)) {
        `$configuration = Get-Content -LiteralPath `$configurationPath -Raw |
            ConvertFrom-Json
        `$matches = @(
            `$configuration.servers |
                Where-Object velocityTarget -EQ `$pausedTarget
        )
        if (`$matches.Count -ne 1) {
            throw "Paused-metrics target must match exactly one server."
        }
        `$matches[0] | Add-Member -NotePropertyName allowStaleMetricsWhenEmpty -NotePropertyValue `$true -Force
        `$configurationAcl = Get-Acl -LiteralPath `$configurationPath
        [IO.File]::WriteAllText(
            `$configurationReplacementPath,
            ((`$configuration | ConvertTo-Json -Depth 20) + [Environment]::NewLine),
            [Text.UTF8Encoding]::new(`$false)
        )
        Set-Acl -LiteralPath `$configurationReplacementPath -AclObject `$configurationAcl
        Move-Item -LiteralPath `$configurationReplacementPath -Destination `$configurationPath -Force
    }

    Move-Item -LiteralPath `$replacementPath -Destination `$executablePath -Force
    `$manualOutput = @(
        & `$executablePath --config `$configurationPath 2>&1
    )
    `$manualExitCode = `$LASTEXITCODE
    if (`$manualExitCode -ne 0) {
        throw "Collector manual verification failed: `$(
            `$manualOutput -join ' '
        )"
    }

    if (`$taskWasEnabled) {
        Enable-ScheduledTask -TaskName `$taskName | Out-Null
        `$lastRunBefore = (
            Get-ScheduledTaskInfo -TaskName `$taskName
        ).LastRunTime
        Start-ScheduledTask -TaskName `$taskName
        `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
        do {
            Start-Sleep -Milliseconds 500
            `$task = Get-ScheduledTask -TaskName `$taskName
            `$taskInfo = Get-ScheduledTaskInfo -TaskName `$taskName
            if (
                `$task.State -ne "Running" -and
                `$taskInfo.LastRunTime -gt `$lastRunBefore
            ) {
                break
            }
        } while ([DateTimeOffset]::UtcNow -lt `$deadline)
        if (
            `$task.State -eq "Running" -or
            `$taskInfo.LastRunTime -le `$lastRunBefore -or
            `$taskInfo.LastTaskResult -ne 0
        ) {
            throw "Collector scheduled-task verification failed."
        }
    }

    `$javaAfter = Get-JavaProcessIds
    if (
        (ConvertTo-Json @(`$javaBefore) -Compress) -ne
        (ConvertTo-Json @(`$javaAfter) -Compress)
    ) {
        throw "Java process IDs changed during collector deployment."
    }

    `$configurationAfter = Get-Content -LiteralPath `$configurationPath -Raw |
        ConvertFrom-Json
    `$configuredPausedTargets = @(
        `$configurationAfter.servers |
            Where-Object {
                `$null -ne `$_.PSObject.Properties[
                    "allowStaleMetricsWhenEmpty"
                ] -and `$_.allowStaleMetricsWhenEmpty -eq `$true
            } |
            ForEach-Object velocityTarget
    )
    [ordered]@{
        status = "deployed"
        host = `$env:COMPUTERNAME
        version = (
            Get-Item -LiteralPath `$executablePath
        ).VersionInfo.FileVersion
        sha256 = (
            Get-FileHash -LiteralPath `$executablePath -Algorithm SHA256
        ).Hash
        backupPath = `$backupDirectory
        manualExitCode = `$manualExitCode
        taskEnabled = `$taskWasEnabled
        taskLastResult = if (`$taskWasEnabled) {
            `$taskInfo.LastTaskResult
        } else {
            `$null
        }
        allowStaleMetricsWhenEmptyTargets = `$configuredPausedTargets
        javaProcessIdsBefore = @(`$javaBefore)
        javaProcessIdsAfter = @(`$javaAfter)
        gameServerRestartPerformed = `$false
    } | ConvertTo-Json -Depth 8 -Compress
} catch {
    `$failure = `$_.Exception.Message
    if (`$backupCreated) {
        Copy-Item -LiteralPath (
            Join-Path `$backupDirectory "Hechao.StatusCollector.exe"
        ) -Destination `$executablePath -Force
        Copy-Item -LiteralPath (
            Join-Path `$backupDirectory "server-heartbeats.json"
        ) -Destination `$configurationPath -Force
    }
    if (`$taskWasEnabled) {
        Enable-ScheduledTask -TaskName `$taskName | Out-Null
        Start-ScheduledTask -TaskName `$taskName
    }
    throw "Collector deployment rolled back: `$failure"
} finally {
    foreach (`$cleanupPath in @(
        `$stagingPath,
        `$replacementPath,
        `$configurationReplacementPath
    )) {
        if (Test-Path -LiteralPath `$cleanupPath) {
            Remove-Item -LiteralPath `$cleanupPath -Force
        }
    }
}
"@

$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($remoteScript)
)
$sshArguments = @(
    "-p", $Port.ToString(
        [Globalization.CultureInfo]::InvariantCulture
    )
) + $commonSshArguments + @(
    "$UserName@$HostName",
    "pwsh.exe -NoLogo -NoProfile -NonInteractive " +
        "-EncodedCommand $encodedCommand"
)

try {
    $remoteOutput = @(& ssh.exe @sshArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Remote deployment failed with exit code $LASTEXITCODE."
    }
    $result = ($remoteOutput -join [Environment]::NewLine).Trim() |
        ConvertFrom-Json
    if ($result.status -ne "deployed" -or $result.sha256 -ne $artifactHash) {
        throw "Remote deployment result did not match the reviewed artifact."
    }
    $summary = [ordered]@{
        status = $result.status
        endpoint = "$HostName`:$Port"
        localArtifact = [ordered]@{
            path = $artifact
            version = $artifactVersion
            sizeBytes = $artifactItem.Length
            sha256 = $artifactHash
        }
        remote = $result
    }
    if ($AsJson) {
        $summary | ConvertTo-Json -Depth 10
    } else {
        [pscustomobject]$summary
    }
} catch {
    $cleanupScript = @"
Remove-Item -LiteralPath "$remoteStagingWindows" -Force -ErrorAction SilentlyContinue
"@
    $cleanupEncoded = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($cleanupScript)
    )
    & ssh.exe @(
        "-p", $Port.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        )
    ) @commonSshArguments "$UserName@$HostName" (
        "pwsh.exe -NoLogo -NoProfile -NonInteractive " +
        "-EncodedCommand $cleanupEncoded"
    ) | Out-Null
    throw
}
