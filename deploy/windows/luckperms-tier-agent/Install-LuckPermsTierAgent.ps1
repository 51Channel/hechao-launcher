[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$JarPath,

    [string]$ProtectedTokenPath =
        "$env:ProgramData\Hechao\LauncherBridge\sync-token.dat",

    [string]$LobbyRoot = 'E:\LobbyServer',
    [string]$BackupRoot = 'E:\manual-backups',
    [string]$ApiBaseUrl = 'https://launcher-api.hechao.world/',
    [string]$AgentId = 'owl5-lobby',

    [ValidateRange(1, 30)]
    [int]$RequestTimeoutSeconds = 10,

    [ValidateRange(5, 300)]
    [int]$PollIntervalSeconds = 10,

    [ValidateRange(1, 20)]
    [int]$ClaimLimit = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

function Set-RestrictedAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $item = Get-Item -LiteralPath $LiteralPath
    $acl = if ($item.PSIsContainer) {
        New-Object System.Security.AccessControl.DirectorySecurity
    }
    else {
        New-Object System.Security.AccessControl.FileSecurity
    }
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = if ($item.PSIsContainer) {
        [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    }
    else {
        [System.Security.AccessControl.InheritanceFlags]::None
    }
    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($sidValue in @(
            'S-1-5-18',
            'S-1-5-32-544',
            $currentSid
        ) | Select-Object -Unique) {
        $sid = New-Object System.Security.Principal.SecurityIdentifier($sidValue)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $sid,
            $rights,
            $inheritance,
            [System.Security.AccessControl.PropagationFlags]::None,
            $allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

$resolvedLobbyRoot = [System.IO.Path]::GetFullPath($LobbyRoot)
$resolvedJarPath = (Resolve-Path -LiteralPath $JarPath).Path
$resolvedTokenPath = (Resolve-Path -LiteralPath $ProtectedTokenPath).Path
if (-not (Test-Path -LiteralPath $resolvedLobbyRoot -PathType Container)) {
    throw "Lobby root does not exist: $resolvedLobbyRoot"
}
if ([System.IO.Path]::GetExtension($resolvedJarPath) -ne '.jar') {
    throw 'JarPath must point to a .jar file.'
}
if ($ApiBaseUrl -notmatch '^https://') {
    throw 'ApiBaseUrl must use HTTPS.'
}
if ($AgentId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$') {
    throw 'AgentId is invalid.'
}

$encryptedToken = [System.IO.File]::ReadAllBytes($resolvedTokenPath)
$plaintextBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    $encryptedToken,
    $null,
    [Security.Cryptography.DataProtectionScope]::LocalMachine)
try {
    $token = [Text.Encoding]::UTF8.GetString($plaintextBytes).Trim()
    if ($token.Length -lt 32 -or $token.Length -gt 256 -or
        $token -notmatch '^[A-Za-z0-9_-]+$') {
        throw 'The protected sync token is invalid.'
    }

    $pluginsDirectory = Join-Path $resolvedLobbyRoot 'plugins'
    $configurationDirectory =
        Join-Path $pluginsDirectory 'HechaoLuckPermsTierAgent'
    $timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $resolvedBackupRoot = [System.IO.Path]::GetFullPath($BackupRoot)
    $backupDirectory =
        Join-Path $resolvedBackupRoot "luckperms-tier-agent-$timestamp"
    $destinationJar =
        Join-Path $pluginsDirectory 'HechaoLuckPermsTierAgent-0.1.3.jar'
    $stagingJar = "$destinationJar.uploading"
    $configurationPath =
        Join-Path $configurationDirectory 'config.properties'

    New-Item -ItemType Directory -Path $pluginsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    Set-RestrictedAcl -LiteralPath $backupDirectory
    if (Test-Path -LiteralPath $configurationDirectory -PathType Leaf) {
        throw 'The plugin configuration path is not a directory.'
    }

    $existingJars = Get-ChildItem -LiteralPath $pluginsDirectory -File |
        Where-Object { $_.Name -like 'HechaoLuckPermsTierAgent-*.jar' }
    foreach ($existingJar in $existingJars) {
        Copy-Item -LiteralPath $existingJar.FullName -Destination $backupDirectory
    }
    $configurationExisted =
        Test-Path -LiteralPath $configurationDirectory -PathType Container
    if ($configurationExisted) {
        Copy-Item -LiteralPath $configurationDirectory `
            -Destination $backupDirectory -Recurse
    }

    $cutoverStarted = $false
    try {
        Copy-Item -LiteralPath $resolvedJarPath -Destination $stagingJar
        $sourceHash =
            (Get-FileHash -LiteralPath $resolvedJarPath -Algorithm SHA256).Hash
        $stagingHash =
            (Get-FileHash -LiteralPath $stagingJar -Algorithm SHA256).Hash
        if ($sourceHash -ne $stagingHash) {
            throw 'The staged plugin JAR checksum does not match the source.'
        }

        $cutoverStarted = $true
        foreach ($existingJar in $existingJars) {
            Remove-Item -LiteralPath $existingJar.FullName -Force
        }
        Move-Item -LiteralPath $stagingJar -Destination $destinationJar -Force

        New-Item -ItemType Directory -Path $configurationDirectory -Force |
            Out-Null
        $configuration = @(
            "api-base-url=$ApiBaseUrl"
            "token=$token"
            "agent-id=$AgentId"
            "request-timeout-seconds=$RequestTimeoutSeconds"
            "poll-interval-seconds=$PollIntervalSeconds"
            "claim-limit=$ClaimLimit"
        ) -join "`n"
        [System.IO.File]::WriteAllText(
            $configurationPath,
            "$configuration`n",
            (New-Object System.Text.UTF8Encoding($false)))

        Set-RestrictedAcl -LiteralPath $configurationPath
        Set-RestrictedAcl -LiteralPath $configurationDirectory
    }
    catch {
        $installFailure = $_
        if ($cutoverStarted) {
            try {
                Get-ChildItem -LiteralPath $pluginsDirectory -File |
                    Where-Object {
                        $_.Name -like 'HechaoLuckPermsTierAgent-*.jar'
                    } |
                    Remove-Item -Force
                Get-ChildItem -LiteralPath $backupDirectory -File |
                    Where-Object {
                        $_.Name -like 'HechaoLuckPermsTierAgent-*.jar'
                    } |
                    Copy-Item -Destination $pluginsDirectory -Force

                if (Test-Path -LiteralPath $configurationDirectory) {
                    Remove-Item -LiteralPath $configurationDirectory `
                        -Recurse -Force
                }
                $backupConfiguration =
                    Join-Path $backupDirectory 'HechaoLuckPermsTierAgent'
                if ($configurationExisted) {
                    Copy-Item -LiteralPath $backupConfiguration `
                        -Destination $pluginsDirectory -Recurse -Force
                }
            }
            catch {
                throw (
                    'LuckPerms tier agent installation and rollback failed. ' +
                    "Install error: $($installFailure.Exception.Message) " +
                    "Rollback error: $($_.Exception.Message)"
                )
            }
        }
        throw $installFailure
    }
    finally {
        Remove-Item -LiteralPath $stagingJar -Force `
            -ErrorAction SilentlyContinue
    }

    [pscustomobject]@{
        PluginJar = $destinationJar
        PluginSha256 =
            (Get-FileHash -LiteralPath $destinationJar -Algorithm SHA256).Hash
        Configuration = $configurationPath
        AgentId = $AgentId
        BackupDirectory = $backupDirectory
        ServerRestartPerformed = $false
    }
}
finally {
    if ($null -ne $plaintextBytes) {
        [Array]::Clear($plaintextBytes, 0, $plaintextBytes.Length)
    }
    $token = $null
}
