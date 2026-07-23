[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$JarPath,

    [Parameter(Mandatory = $true)]
    [string]$TokenPath,

    [ValidateSet('disabled', 'monitor', 'enforce')]
    [string]$Mode = 'monitor',

    [string]$VelocityRoot = 'E:\Velocity',
    [string]$BackupRoot = 'E:\manual-backups',
    [string]$ApiUrl = 'https://launcher-api.hechao.world/v1/internal/velocity/authorize',
    [string]$ProxyInstance = 'owl5-main',

    [ValidateRange(500, 10000)]
    [int]$RequestTimeoutMillis = 2500
)

$ErrorActionPreference = 'Stop'

function Set-RestrictedAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $item = Get-Item -LiteralPath $LiteralPath
    $isDirectory = $item.PSIsContainer
    $acl = if ($isDirectory) {
        New-Object System.Security.AccessControl.DirectorySecurity
    }
    else {
        New-Object System.Security.AccessControl.FileSecurity
    }

    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = if ($isDirectory) {
        [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    }
    else {
        [System.Security.AccessControl.InheritanceFlags]::None
    }
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow

    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($sidValue in @('S-1-5-18', 'S-1-5-32-544', $currentSid) | Select-Object -Unique) {
        $sid = New-Object System.Security.Principal.SecurityIdentifier($sidValue)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $sid,
            $rights,
            $inheritance,
            $propagation,
            $allow)
        $acl.AddAccessRule($rule)
    }

    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

$resolvedVelocityRoot = [System.IO.Path]::GetFullPath($VelocityRoot)
$resolvedJarPath = (Resolve-Path -LiteralPath $JarPath).Path
$resolvedTokenPath = (Resolve-Path -LiteralPath $TokenPath).Path

if (-not (Test-Path -LiteralPath $resolvedVelocityRoot -PathType Container)) {
    throw "Velocity root does not exist: $resolvedVelocityRoot"
}
if ([System.IO.Path]::GetExtension($resolvedJarPath) -ne '.jar') {
    throw 'JarPath must point to a .jar file.'
}
if ($ApiUrl -notmatch '^https://') {
    throw 'ApiUrl must use HTTPS.'
}
if ($ProxyInstance -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
    throw 'ProxyInstance is invalid.'
}

$token = [System.IO.File]::ReadAllText($resolvedTokenPath).Trim()
if ($token.Length -lt 24 -or $token.Length -gt 256 -or
    $token -notmatch '^[A-Za-z0-9_-]+$') {
    throw 'The Velocity authorization token is invalid.'
}

$pluginsDirectory = Join-Path $resolvedVelocityRoot 'plugins'
$configurationDirectory = Join-Path $pluginsDirectory 'hechao-velocity-authorizer'
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path ([System.IO.Path]::GetFullPath($BackupRoot)) "velocity-authorizer-$timestamp"
$destinationJar = Join-Path $pluginsDirectory 'HechaoVelocityAuthorizer-0.1.0.jar'
$stagingJar = "$destinationJar.uploading"

New-Item -ItemType Directory -Path $pluginsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$existingJars = Get-ChildItem -LiteralPath $pluginsDirectory -File |
    Where-Object { $_.Name -like 'HechaoVelocityAuthorizer-*.jar' }
foreach ($existingJar in $existingJars) {
    Move-Item -LiteralPath $existingJar.FullName -Destination $backupDirectory
}
if (Test-Path -LiteralPath $configurationDirectory -PathType Container) {
    Copy-Item -LiteralPath $configurationDirectory -Destination $backupDirectory -Recurse
}

Copy-Item -LiteralPath $resolvedJarPath -Destination $stagingJar
$sourceHash = (Get-FileHash -LiteralPath $resolvedJarPath -Algorithm SHA256).Hash
$stagingHash = (Get-FileHash -LiteralPath $stagingJar -Algorithm SHA256).Hash
if ($sourceHash -ne $stagingHash) {
    throw 'The staged plugin JAR checksum does not match the source.'
}
Move-Item -LiteralPath $stagingJar -Destination $destinationJar -Force

New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
$configurationPath = Join-Path $configurationDirectory 'config.properties'
$configuration = @(
    '# disabled, monitor, or enforce'
    "mode=$Mode"
    "api-url=$ApiUrl"
    "token=$token"
    "proxy-instance=$ProxyInstance"
    "request-timeout-millis=$RequestTimeoutMillis"
) -join "`n"
[System.IO.File]::WriteAllText(
    $configurationPath,
    "$configuration`n",
    (New-Object System.Text.UTF8Encoding($false)))

Set-RestrictedAcl -LiteralPath $configurationPath
Set-RestrictedAcl -LiteralPath $configurationDirectory
Set-RestrictedAcl -LiteralPath $backupDirectory

[pscustomobject]@{
    PluginJar = $destinationJar
    PluginSha256 = (Get-FileHash -LiteralPath $destinationJar -Algorithm SHA256).Hash
    Mode = $Mode
    Configuration = $configurationPath
    BackupDirectory = $backupDirectory
    VelocityRestartPerformed = $false
}
