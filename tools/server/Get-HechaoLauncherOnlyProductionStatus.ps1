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

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
    throw "SSH identity file does not exist: $IdentityFile"
}

$remoteScript = @'
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $line = Get-Content -LiteralPath $Path -Encoding utf8 |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Name))\s*=" } |
        Select-Object -Last 1

    if ($null -eq $line) {
        return $null
    }

    return ($line -split "=", 2)[1].Trim()
}

function Get-JarEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            exists = $false
            path = $Path
            size = $null
            sha256 = $null
        }
    }

    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        exists = $true
        path = $item.FullName
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

$velocityRoot = "E:\Velocity"
$lobbyRoot = "E:\LobbyServer"
$authorizerConfig = Join-Path $velocityRoot "plugins\hechao-velocity-authorizer\config.properties"
$lobbyProperties = Join-Path $lobbyRoot "server.properties"
$lobbyWhitelist = Join-Path $lobbyRoot "whitelist.json"

$legacyProxyPlugins = @(
    Get-ChildItem -LiteralPath (Join-Path $velocityRoot "plugins") -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^(HubCommand|ViaVersion|ViaBackwards).*\.jar$" } |
        Select-Object -ExpandProperty Name
)

$activeBackendHubScripts = @()
foreach ($root in @(
    "E:\Survival1",
    "E:\Survival2",
    "E:\DollNight",
    "E:\ActivityLocal",
    "E:\ActivityServer",
    "E:\MonsterActivity"
)) {
    $scriptPath = Join-Path $root "plugins\Skript\scripts\hub.sk"
    if (Test-Path -LiteralPath $scriptPath -PathType Leaf) {
        $activeBackendHubScripts += $scriptPath
    }
}

$whitelistEntries = if (Test-Path -LiteralPath $lobbyWhitelist -PathType Leaf) {
    @((Get-Content -Raw -LiteralPath $lobbyWhitelist -Encoding utf8 | ConvertFrom-Json)).Count
} else {
    $null
}

$velocityListeners = @(
    Get-NetTCPConnection -State Listen -LocalPort 25577 -ErrorAction SilentlyContinue |
        Select-Object LocalAddress, LocalPort, OwningProcess
)
$lobbyListeners = @(
    Get-NetTCPConnection -State Listen -LocalPort 25566 -ErrorAction SilentlyContinue |
        Select-Object LocalAddress, LocalPort, OwningProcess
)

$result = [ordered]@{
    checkedAtUtc = [DateTimeOffset]::UtcNow
    computerName = $env:COMPUTERNAME
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    authorizer = Get-JarEvidence -Path (
        Join-Path $velocityRoot "plugins\HechaoVelocityAuthorizer-0.4.0.jar"
    )
    authorizerMode = Get-PropertyValue -Path $authorizerConfig -Name "mode"
    infrastructureTargets = Get-PropertyValue `
        -Path $authorizerConfig `
        -Name "infrastructure-targets"
    velocityListeners = $velocityListeners
    legacyProxyPlugins = $legacyProxyPlugins
    lobbyGuard = Get-JarEvidence -Path (
        Join-Path $lobbyRoot "plugins\HechaoLobbyGuard-0.1.0.jar"
    )
    lobbyListeners = $lobbyListeners
    lobbyServerIp = Get-PropertyValue -Path $lobbyProperties -Name "server-ip"
    lobbyWhitelistEnabled = Get-PropertyValue -Path $lobbyProperties -Name "white-list"
    lobbyEnforceWhitelist = Get-PropertyValue `
        -Path $lobbyProperties `
        -Name "enforce-whitelist"
    lobbyWhitelistEntries = $whitelistEntries
    activeBackendHubScripts = $activeBackendHubScripts
    luckPermsTierAgentPresent = Test-Path -LiteralPath (
        Join-Path $lobbyRoot "plugins\HechaoLuckPermsTierAgent-0.1.0.jar"
    )
    serverMetricsAgentPresent = Test-Path -LiteralPath (
        Join-Path $lobbyRoot "plugins\HechaoServerMetrics-0.1.0.jar"
    )
}

$result | ConvertTo-Json -Depth 8 -Compress
'@

$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($remoteScript)
)
$sshArguments = @(
    "-i", (Resolve-Path -LiteralPath $IdentityFile).Path,
    "-p", $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
    "-o", "BatchMode=yes",
    "-o", "StrictHostKeyChecking=yes",
    "$UserName@$HostName",
    "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -EncodedCommand $encodedCommand"
)

$output = & ssh.exe @sshArguments
if ($LASTEXITCODE -ne 0) {
    throw "Remote launcher-only production audit failed with exit code $LASTEXITCODE."
}

if ($AsJson) {
    $output
    return
}

$output | ConvertFrom-Json
