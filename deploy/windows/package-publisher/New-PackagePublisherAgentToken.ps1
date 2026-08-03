[CmdletBinding()]
param(
    [string]$StateDirectory =
        "$env:ProgramData\Hechao\PackagePublisherAgent",

    [string]$TokenFileName = 'publisher-token.dpapi',

    [string]$BackupRoot = "$env:ProgramData\Hechao\backups",

    [switch]$Replace
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$windowsPrincipal = [System.Security.Principal.WindowsPrincipal]::new(
    [System.Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $windowsPrincipal.IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Publisher token creation requires an elevated PowerShell 7 session.'
}

function Set-RestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$LiteralPath)

    $currentIdentity =
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @(
            $currentIdentity,
            'SYSTEM',
            'BUILTIN\Administrators'
        )) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            (
                [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            ),
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

$state = [System.IO.Path]::GetFullPath($StateDirectory)
$tokenPath = Join-Path $state $TokenFileName
$tokenExisted = Test-Path -LiteralPath $tokenPath -PathType Leaf
if ($tokenExisted -and -not $Replace) {
    throw "A protected publisher token already exists: $tokenPath"
}

[System.IO.Directory]::CreateDirectory($state) | Out-Null
[System.IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
Set-RestrictedDirectoryAcl -LiteralPath $state
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupPath = $null
if ($tokenExisted) {
    $backupDirectory = Join-Path (
        [System.IO.Path]::GetFullPath($BackupRoot)
    ) "package-publisher-token-$timestamp"
    [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
    Set-RestrictedDirectoryAcl -LiteralPath $backupDirectory
    $backupPath = Join-Path $backupDirectory $TokenFileName
    Copy-Item -LiteralPath $tokenPath -Destination $backupPath
}

$randomBytes = [byte[]]::new(48)
$clearBytes = $null
$protectedBytes = $null
$verificationBytes = $null
$commitStarted = $false
$temporaryPath = "$tokenPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
    $token = [Convert]::ToBase64String($randomBytes).
        TrimEnd('=').
        Replace('+', '-').
        Replace('/', '_')
    $clearBytes = [System.Text.Encoding]::UTF8.GetBytes($token)
    $sha256 = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($clearBytes)
    )
    $protectedBytes =
        [System.Security.Cryptography.ProtectedData]::Protect(
            $clearBytes,
            $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    [System.IO.File]::WriteAllBytes($temporaryPath, $protectedBytes)
    $commitStarted = $true
    Move-Item -LiteralPath $temporaryPath -Destination $tokenPath -Force

    $verificationBytes =
        [System.Security.Cryptography.ProtectedData]::Unprotect(
            [System.IO.File]::ReadAllBytes($tokenPath),
            $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    if (-not [System.Security.Cryptography.CryptographicOperations]::
        FixedTimeEquals($clearBytes, $verificationBytes)) {
        throw 'The protected publisher token failed its DPAPI round trip.'
    }
}
catch {
    $failure = $_
    if ($commitStarted) {
        try {
            if ($tokenExisted) {
                Copy-Item `
                    -LiteralPath $backupPath `
                    -Destination $tokenPath `
                    -Force
            }
            else {
                Remove-Item `
                    -LiteralPath $tokenPath `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
        }
        catch {
            throw (
                "Publisher token creation failed and rollback also failed. " +
                "Creation error: $($failure.Exception.Message) Rollback error: " +
                $_.Exception.Message
            )
        }
    }
    throw $failure
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
    [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    if ($null -ne $clearBytes) {
        [Array]::Clear($clearBytes, 0, $clearBytes.Length)
    }
    if ($null -ne $protectedBytes) {
        [Array]::Clear($protectedBytes, 0, $protectedBytes.Length)
    }
    if ($null -ne $verificationBytes) {
        [Array]::Clear($verificationBytes, 0, $verificationBytes.Length)
    }
    $token = $null
}

$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

[ordered]@{
    token_path = $tokenPath
    token_sha256 = $sha256
    protection_scope = 'CurrentUser'
    run_as_user = $currentIdentity
    backup = $backupPath
    clear_token_output = $false
} | ConvertTo-Json -Compress
