[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentJar,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [string[]]$ServerDirectories = @(
        'E:\LobbyServer',
        'E:\Survival2',
        'E:\Survival1'
    ),

    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path -LiteralPath $AgentJar).Path
$actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
if ($actualSha256 -ne $ExpectedSha256.ToUpperInvariant()) {
    throw "Agent JAR SHA-256 mismatch. Expected $ExpectedSha256, got $actualSha256."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($source)
try {
    $pluginDescriptor = $archive.Entries |
        Where-Object FullName -eq 'plugin.yml' |
        Select-Object -First 1
    if ($null -eq $pluginDescriptor) {
        throw 'The agent JAR does not contain plugin.yml.'
    }

    $reader = [System.IO.StreamReader]::new($pluginDescriptor.Open())
    try {
        $descriptor = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    if ($descriptor -notmatch '(?m)^name:\s*HechaoServerMetrics\s*$' -or
        $descriptor -notmatch '(?m)^version:\s*''?0\.1\.0''?\s*$') {
        throw 'The agent JAR plugin descriptor is not HechaoServerMetrics 0.1.0.'
    }
}
finally {
    $archive.Dispose()
}

$resolvedServers = foreach ($serverDirectory in $ServerDirectories) {
    $resolved = (Resolve-Path -LiteralPath $serverDirectory).Path
    $plugins = Join-Path $resolved 'plugins'
    if (-not (Test-Path -LiteralPath $plugins -PathType Container)) {
        throw "Plugin directory is missing: $plugins"
    }

    [pscustomobject]@{
        Server = $resolved
        Plugins = $plugins
    }
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "server-metrics-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

foreach ($server in $resolvedServers) {
    $serverName = Split-Path -Leaf $server.Server
    $serverBackup = Join-Path $backupDirectory $serverName
    New-Item -ItemType Directory -Path $serverBackup -Force | Out-Null

    $existing = Get-ChildItem -LiteralPath $server.Plugins -File |
        Where-Object Name -Like 'HechaoServerMetrics*.jar'
    foreach ($item in $existing) {
        Move-Item -LiteralPath $item.FullName -Destination $serverBackup
    }

    $destination = Join-Path $server.Plugins 'HechaoServerMetrics-0.1.0.jar'
    Copy-Item -LiteralPath $source -Destination $destination
    $deployedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash
    if ($deployedSha256 -ne $actualSha256) {
        throw "Deployed JAR verification failed: $destination"
    }

    Write-Output "deployed=$destination"
}

Write-Output "backup=$backupDirectory"
Write-Output "sha256=$actualSha256"
Write-Output 'server_restart=not_performed'
