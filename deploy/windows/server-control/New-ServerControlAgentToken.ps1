[CmdletBinding()]
param(
    [string]$StateDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent",

    [string]$TokenFileName = 'server-control-token.dat',

    [string]$BackupRoot = "$env:ProgramData\Hechao\backups",

    [switch]$Replace
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$state = [System.IO.Path]::GetFullPath($StateDirectory)
$tokenPath = Join-Path $state $TokenFileName
if ((Test-Path -LiteralPath $tokenPath -PathType Leaf) -and -not $Replace) {
    throw "A protected token already exists: $tokenPath"
}

[System.IO.Directory]::CreateDirectory($state) | Out-Null
[System.IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupPath = $null
if (Test-Path -LiteralPath $tokenPath -PathType Leaf) {
    $backupDirectory = Join-Path (
        [System.IO.Path]::GetFullPath($BackupRoot)
    ) "server-control-token-$timestamp"
    [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
    $backupPath = Join-Path $backupDirectory $TokenFileName
    Copy-Item -LiteralPath $tokenPath -Destination $backupPath
}

$randomBytes = [byte[]]::new(48)
$clearBytes = $null
$protectedBytes = $null
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
            [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    [System.IO.File]::WriteAllBytes($temporaryPath, $protectedBytes)
    Move-Item -LiteralPath $temporaryPath -Destination $tokenPath -Force
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
    $token = $null
}

$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @('SYSTEM', 'BUILTIN\Administrators')) {
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
Set-Acl -LiteralPath $state -AclObject $acl

[ordered]@{
    token_path = $tokenPath
    token_sha256 = $sha256
    protection_scope = 'LocalMachine'
    backup = $backupPath
    clear_token_output = $false
} | ConvertTo-Json -Compress
