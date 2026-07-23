[CmdletBinding()]
param(
    [string]$LuckPermsConfig = 'E:\LobbyServer\plugins\LuckPerms\config.yml',
    [string]$MariaDbClient = 'E:\MariaDB\bin\mysql.exe',
    [string]$ApiEndpoint = 'https://launcher-api.hechao.world/v1/internal/luckperms/snapshot',
    [string]$StateDirectory = "$env:ProgramData\Hechao\LauncherBridge"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$mutex = New-Object Threading.Mutex($false, 'Global\HechaoLuckPermsSync')
if (-not $mutex.WaitOne(0)) {
    $mutex.Dispose()
    exit 0
}

function Write-SyncLog {
    param([string]$Message)

    New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    $logPath = Join-Path $StateDirectory 'sync.log'
    $line = '{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('O'), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8

    if ((Get-Item -LiteralPath $logPath).Length -gt 1MB) {
        $recent = Get-Content -LiteralPath $logPath -Tail 200
        Set-Content -LiteralPath $logPath -Value $recent -Encoding UTF8
    }
}

function Get-YamlScalar {
    param(
        [string[]]$Lines,
        [string]$Name,
        [switch]$TopLevel
    )

    $indent = if ($TopLevel) { '^' } else { '^\s{2}' }
    $pattern = $indent + [regex]::Escape($Name) + '\s*:\s*'
    $line = $Lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $line) {
        throw "LuckPerms config value is missing: $Name"
    }

    $value = ($line -replace $pattern, '').Trim()
    return $value.Trim([char[]]@([char]32, [char]39, [char]34))
}

function Read-ProtectedToken {
    param([string]$Path)

    Add-Type -AssemblyName System.Security
    $encrypted = [IO.File]::ReadAllBytes($Path)
    $plaintext = [Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    try {
        return [Text.Encoding]::UTF8.GetString($plaintext)
    }
    finally {
        [Array]::Clear($plaintext, 0, $plaintext.Length)
    }
}

try {
    if (-not (Test-Path -LiteralPath $LuckPermsConfig) -or
        -not (Test-Path -LiteralPath $MariaDbClient)) {
        throw 'LuckPerms config or MariaDB client is missing.'
    }

    $tokenPath = Join-Path $StateDirectory 'sync-token.dat'
    if (-not (Test-Path -LiteralPath $tokenPath)) {
        throw 'The protected LuckPerms sync token is missing.'
    }

    $lines = Get-Content -LiteralPath $LuckPermsConfig
    $storageMethod = Get-YamlScalar -Lines $lines -Name 'storage-method' -TopLevel
    if ($storageMethod -notin @('mysql', 'mariadb')) {
        throw 'LuckPerms is not using a supported SQL storage method.'
    }

    $address = Get-YamlScalar -Lines $lines -Name 'address'
    $database = Get-YamlScalar -Lines $lines -Name 'database'
    $username = Get-YamlScalar -Lines $lines -Name 'username'
    $password = Get-YamlScalar -Lines $lines -Name 'password'
    $tablePrefix = Get-YamlScalar -Lines $lines -Name 'table-prefix'
    if ($tablePrefix -notmatch '^[A-Za-z0-9_]+$') {
        throw 'LuckPerms table prefix is invalid.'
    }

    $hostName = $address
    $port = 3306
    if ($address -match '^(.+):(\d+)$') {
        $hostName = $Matches[1]
        $port = [int]$Matches[2]
    }

    $query = "SELECT uuid, username, primary_group FROM ``${tablePrefix}players`` ORDER BY uuid;"
    $arguments = @(
        '--batch',
        '--skip-column-names',
        '--raw',
        '--connect-timeout=5',
        '--host', $hostName,
        '--port', $port,
        '--user', $username,
        $database,
        '--execute', $query
    )

    $env:MYSQL_PWD = $password
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $rows = @(& $MariaDbClient @arguments 2>$null)
            $databaseExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if ($databaseExitCode -ne 0) {
            throw 'LuckPerms database query failed.'
        }
    }
    finally {
        Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
        $password = $null
    }

    $players = @()
    foreach ($row in $rows) {
        $parts = $row -split "`t", 3
        $parsedUuid = [Guid]::Empty
        if ($parts.Count -ne 3 -or
            -not [Guid]::TryParse($parts[0], [ref]$parsedUuid) -or
            $parts[1] -notmatch '^[A-Za-z0-9_]{3,16}$' -or
            $parts[2] -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
            throw 'LuckPerms returned an invalid player row.'
        }

        $players += [ordered]@{
            minecraftUuid = $parts[0]
            minecraftName = $parts[1]
            primaryGroup = $parts[2].ToLowerInvariant()
        }
    }

    if ($players.Count -eq 0 -or $players.Count -gt 5000) {
        throw 'LuckPerms returned an unexpected player count.'
    }

    $body = [ordered]@{
        capturedAt = [DateTimeOffset]::UtcNow.ToString('O')
        isFullSnapshot = $true
        players = $players
    } | ConvertTo-Json -Depth 4 -Compress

    $syncToken = Read-ProtectedToken -Path $tokenPath
    try {
        $requestParameters = @{
            Method = 'Post'
            Uri = $ApiEndpoint
            Headers = @{
                'X-Hechao-Sync-Token' = $syncToken
                'User-Agent' = 'Hechao-LuckPerms-Sync/0.3.0'
            }
            ContentType = 'application/json; charset=utf-8'
            Body = [Text.Encoding]::UTF8.GetBytes($body)
            TimeoutSec = 20
            UseBasicParsing = $true
        }
        $response = Invoke-RestMethod @requestParameters
    }
    finally {
        $syncToken = $null
    }

    if ($response.importedPlayers -ne $players.Count) {
        throw 'The launcher API acknowledged an unexpected player count.'
    }

    Write-SyncLog "status=ok players=$($players.Count) identities=$($response.updatedIdentities)"
}
catch {
    Write-SyncLog "status=failed type=$($_.Exception.GetType().Name)"
    throw
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
