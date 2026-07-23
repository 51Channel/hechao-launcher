[CmdletBinding()]
param(
    [string]$StateDirectory = "$env:ProgramData\Hechao\StatusCollector",
    [string]$TokenFileName = 'heartbeat-token.dat'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$token = Read-Host 'Paste the server heartbeat token'
if ($token -notmatch '^[A-Za-z0-9_-]{32,256}$') {
    throw 'The heartbeat token must be 32-256 URL-safe characters.'
}

[System.IO.Directory]::CreateDirectory($StateDirectory) | Out-Null
$clearBytes = [System.Text.Encoding]::UTF8.GetBytes($token)
try {
    $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
        $clearBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $tokenPath = Join-Path $StateDirectory $TokenFileName
    [System.IO.File]::WriteAllBytes($tokenPath, $protectedBytes)
}
finally {
    [Array]::Clear($clearBytes, 0, $clearBytes.Length)
}

$acl = Get-Acl -LiteralPath $StateDirectory
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    'SYSTEM',
    'FullControl',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow')))
$acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Administrators',
    'FullControl',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow')))
Set-Acl -LiteralPath $StateDirectory -AclObject $acl

Write-Output "protected_token=$tokenPath"
