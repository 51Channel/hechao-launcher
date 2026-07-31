#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$HostName,

    [ValidateRange(1, 65535)]
    [int]$Port = 22,

    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$UserName = 'administrator',

    [Parameter(Mandatory)]
    [string]$IdentityFile,

    [Parameter(Mandatory)]
    [string]$KnownHostsFile,

    [Parameter(Mandatory)]
    [string]$ConfigurationPath,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$identity = (Resolve-Path -LiteralPath $IdentityFile).Path
$knownHosts = (Resolve-Path -LiteralPath $KnownHostsFile).Path
$configuration = (Resolve-Path -LiteralPath $ConfigurationPath).Path
$configurationObject = Get-Content -Raw -LiteralPath $configuration |
    ConvertFrom-Json -Depth 16

if ($configurationObject.apiEndpoint -notmatch '^https://') {
    throw 'Status collector apiEndpoint must use HTTPS.'
}

if ([string]::IsNullOrWhiteSpace([string]$configurationObject.collectorInstance) -or
    $configurationObject.servers.Count -lt 1) {
    throw 'Status collector configuration is missing collectorInstance or servers.'
}

$expectedHash = (Get-FileHash -LiteralPath $configuration -Algorithm SHA256).Hash
$stagingName = ".server-heartbeats-$([guid]::NewGuid().ToString('N')).json"
$remoteStagingPath = "C:\ProgramData\Hechao\StatusCollector\$stagingName"
$remoteScpPath = "C:/ProgramData/Hechao/StatusCollector/$stagingName"
$commonArguments = @(
    '-i', $identity,
    '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=$knownHosts",
    '-o', 'ConnectTimeout=15'
)

& scp.exe @commonArguments -P $Port.ToString(
    [Globalization.CultureInfo]::InvariantCulture
) $configuration "${UserName}@${HostName}:$remoteScpPath"
if ($LASTEXITCODE -ne 0) {
    throw "Status collector configuration upload failed with exit code $LASTEXITCODE."
}

$remoteScript = @"
#requires -Version 7.4
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'

`$installDirectory = 'C:\ProgramData\Hechao\StatusCollector'
`$configurationPath = Join-Path `$installDirectory 'server-heartbeats.json'
`$stagingPath = '$remoteStagingPath'
`$replacementPath = Join-Path `$installDirectory 'server-heartbeats.json.new'
`$taskName = 'Hechao Launcher Server Heartbeats'
`$expectedHash = '$expectedHash'
`$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
`$backupDirectory = Join-Path (Join-Path `$installDirectory 'backups') "configuration-`$timestamp"
`$backupPath = Join-Path `$backupDirectory 'server-heartbeats.json'
`$replaced = `$false

function Get-JavaProcessIds {
    @(
        Get-CimInstance Win32_Process -Filter "Name='java.exe'" |
            Select-Object -ExpandProperty ProcessId |
            Sort-Object
    )
}

function Wait-HeartbeatTask {
    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        `$task = Get-ScheduledTask -TaskName `$taskName
    } while (`$task.State -eq 'Running' -and
        [DateTimeOffset]::UtcNow -lt `$deadline)

    if (`$task.State -eq 'Running') {
        throw "Heartbeat task did not finish within 30 seconds."
    }

    `$taskInfo = Get-ScheduledTaskInfo -TaskName `$taskName
    if (`$taskInfo.LastTaskResult -ne 0) {
        throw "Heartbeat task failed with result `$(`$taskInfo.LastTaskResult)."
    }
}

`$javaBefore = Get-JavaProcessIds
try {
    if (-not (Test-Path -LiteralPath `$stagingPath -PathType Leaf)) {
        throw 'Uploaded status collector configuration is missing.'
    }

    `$stagingHash = (Get-FileHash -LiteralPath `$stagingPath -Algorithm SHA256).Hash
    if (`$stagingHash -ne `$expectedHash) {
        throw "Uploaded configuration hash mismatch: `$stagingHash."
    }

    `$candidate = Get-Content -Raw -LiteralPath `$stagingPath |
        ConvertFrom-Json -Depth 16
    if (`$candidate.apiEndpoint -notmatch '^https://' -or
        [string]::IsNullOrWhiteSpace([string]`$candidate.collectorInstance) -or
        `$candidate.servers.Count -lt 1) {
        throw 'Uploaded status collector configuration is invalid.'
    }

    New-Item -ItemType Directory -Path `$backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath `$configurationPath -Destination `$backupPath
    Copy-Item -LiteralPath `$stagingPath -Destination `$replacementPath -Force
    [System.IO.File]::Replace(`$replacementPath, `$configurationPath, `$null)
    `$replaced = `$true

    Start-ScheduledTask -TaskName `$taskName
    Wait-HeartbeatTask

    `$deployedHash = (Get-FileHash -LiteralPath `$configurationPath -Algorithm SHA256).Hash
    if (`$deployedHash -ne `$expectedHash) {
        throw "Deployed configuration hash mismatch: `$deployedHash."
    }

    `$javaAfter = Get-JavaProcessIds
    if (Compare-Object -ReferenceObject `$javaBefore -DifferenceObject `$javaAfter) {
        throw 'Minecraft Java process IDs changed during configuration deployment.'
    }

    [pscustomobject]@{
        host = `$env:COMPUTERNAME
        configurationSha256 = `$deployedHash
        collectorInstance = [string]`$candidate.collectorInstance
        targets = @(`$candidate.servers | ForEach-Object { [string]`$_.velocityTarget })
        backupPath = `$backupPath
        heartbeatTaskResult = 0
        minecraftProcessIds = `$javaAfter
    } | ConvertTo-Json -Depth 6 -Compress
}
catch {
    if (`$replaced -and (Test-Path -LiteralPath `$backupPath -PathType Leaf)) {
        Copy-Item -LiteralPath `$backupPath -Destination `$configurationPath -Force
        Start-ScheduledTask -TaskName `$taskName
    }
    throw
}
finally {
    Remove-Item -LiteralPath `$stagingPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath `$replacementPath -Force -ErrorAction SilentlyContinue
}
"@

$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($remoteScript)
)
$remoteCommand = "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -EncodedCommand $encodedCommand"
$output = & ssh.exe @commonArguments -p $Port.ToString(
    [Globalization.CultureInfo]::InvariantCulture
) "${UserName}@${HostName}" $remoteCommand
if ($LASTEXITCODE -ne 0) {
    throw "Status collector configuration deployment failed with exit code $LASTEXITCODE."
}

if ($AsJson) {
    $output | Select-Object -Last 1
}
else {
    $output
}
