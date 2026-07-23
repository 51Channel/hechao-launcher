[CmdletBinding()]
param(
    [string]$StateDirectory = "$env:ProgramData\Hechao\LauncherBridge"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$token = [Console]::In.ReadToEnd().Trim()
if ($token.Length -lt 32 -or $token.Length -gt 256 -or $token -notmatch '^[A-Za-z0-9_-]+$') {
    throw 'The supplied sync token is invalid.'
}

$plaintext = [Text.Encoding]::UTF8.GetBytes($token)
try {
    $encrypted = [Security.Cryptography.ProtectedData]::Protect(
        $plaintext,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
}
finally {
    [Array]::Clear($plaintext, 0, $plaintext.Length)
    $token = $null
}

New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
$tokenPath = Join-Path $StateDirectory 'sync-token.dat'
[IO.File]::WriteAllBytes($tokenPath, $encrypted)

& icacls.exe $StateDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to protect the bridge state directory ACL.'
}

Write-Output 'protected_token=ready'
