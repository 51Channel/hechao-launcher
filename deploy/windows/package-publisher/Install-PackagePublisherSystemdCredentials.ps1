[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WindowsConfiguration,

    [Parameter(Mandatory)]
    [ValidatePattern('^root@[A-Za-z0-9.-]+$')]
    [string]$Remote,

    [Parameter(Mandatory)]
    [string]$IdentityFile,

    [Parameter(Mandatory)]
    [string]$KnownHostsFile,

    [ValidatePattern('^/etc/credstore\.encrypted/[a-z0-9-]+$')]
    [string]$RemoteCredentialRoot =
        '/etc/credstore.encrypted/hechao-package-publisher'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required.'
}

$configurationPath = (Resolve-Path -LiteralPath $WindowsConfiguration).Path
$identityPath = (Resolve-Path -LiteralPath $IdentityFile).Path
$knownHostsPath = (Resolve-Path -LiteralPath $KnownHostsFile).Path
$configuration = Get-Content -Raw -LiteralPath $configurationPath |
    ConvertFrom-Json

function Resolve-ProtectedPath {
    param([Parameter(Mandatory)][string]$Value)

    if (-not [IO.Path]::IsPathFullyQualified($Value)) {
        throw 'Windows Publisher protected paths must be absolute.'
    }

    $path = (Resolve-Path -LiteralPath $Value).Path
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Protected input cannot be a reparse point: $path"
    }

    return $path
}

function Unprotect-DpapiFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [byte[]]$Entropy
    )

    $ciphertext = [IO.File]::ReadAllBytes($Path)
    try {
        $plaintext = [Security.Cryptography.ProtectedData]::Unprotect(
            $ciphertext,
            $Entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        return ,$plaintext
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($ciphertext)
    }
}

function Invoke-StrictSsh {
    param(
        [Parameter(Mandatory)][string]$Command,
        [byte[]]$InputBytes
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command ssh.exe -ErrorAction Stop).Source
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '-i', $identityPath,
        '-o', 'BatchMode=yes',
        '-o', 'IdentitiesOnly=yes',
        '-o', "UserKnownHostsFile=$knownHostsPath",
        '-o', 'StrictHostKeyChecking=yes',
        $Remote,
        $Command
    )) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start ssh.exe.'
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    try {
        if ($null -ne $InputBytes) {
            $process.StandardInput.BaseStream.Write(
                $InputBytes,
                0,
                $InputBytes.Length)
        }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "SSH command failed with exit code $($process.ExitCode): $stderr"
        }

        return $stdout
    }
    finally {
        $process.Dispose()
    }
}

function Install-EncryptedCredential {
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')]
        [string]$Name,
        [Parameter(Mandatory)][byte[]]$Plaintext
    )

    $remotePath = "$RemoteCredentialRoot/$Name.cred"
    $command = @"
set -eu
umask 077
tmp=`$(mktemp '${RemoteCredentialRoot}/.${Name}.XXXXXX')
trap 'rm -f "`$tmp"' EXIT
systemd-creds encrypt --with-key=host --name=${Name} - "`$tmp" >/dev/null
install -o root -g root -m 0600 "`$tmp" '${remotePath}.new'
mv -f '${remotePath}.new' '${remotePath}'
systemd-creds decrypt --name=${Name} '${remotePath}' /dev/null >/dev/null
"@
    Invoke-StrictSsh -Command $command -InputBytes $Plaintext | Out-Null
    Write-Output "Installed encrypted systemd credential: $Name"
}

$tokenPath = Resolve-ProtectedPath ([string]$configuration.tokenPath)
$signingKeyPath = Resolve-ProtectedPath ([string]$configuration.signingKeyPath)
$ossCredentialPath = Resolve-ProtectedPath ([string]$configuration.ossCredentialPath)
$utf8 = [Text.UTF8Encoding]::new($false)

Invoke-StrictSsh -Command (
    'set -eu; umask 077; ' +
    'install -d -o root -g root -m 0700 ' +
    "'$RemoteCredentialRoot'; " +
    'systemd-creds setup >/dev/null'
) | Out-Null

$token = $null
$signingKey = $null
$ossCredential = $null
try {
    $token = Unprotect-DpapiFile -Path $tokenPath
    $tokenText = $utf8.GetString($token)
    if ($tokenText -notmatch '^[A-Za-z0-9_-]{32,256}$') {
        throw 'The decrypted Publisher token is invalid.'
    }
    $tokenText = $null

    $signingEntropy = $utf8.GetBytes(
        [string]$configuration.signingKeyEntropyLabel)
    try {
        $signingKey = Unprotect-DpapiFile -Path $signingKeyPath -Entropy $signingEntropy
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            $signingEntropy)
    }

    if ($configuration.signingKeyBlobSha256) {
        $signingCiphertext = [IO.File]::ReadAllBytes($signingKeyPath)
        try {
            $actualDigest = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($signingCiphertext))
        }
        finally {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory(
                $signingCiphertext)
        }
        if ($actualDigest -ne (
            [string]$configuration.signingKeyBlobSha256).ToUpperInvariant()) {
            throw 'The encrypted signing key digest does not match configuration.'
        }
    }

    $ossEntropy = $utf8.GetBytes(
        [string]$configuration.ossCredentialEntropyLabel)
    try {
        $ossCredential = Unprotect-DpapiFile -Path $ossCredentialPath -Entropy $ossEntropy
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            $ossEntropy)
    }

    Install-EncryptedCredential -Name 'publisher-token' -Plaintext $token
    Install-EncryptedCredential -Name 'distribution-signing-key' -Plaintext $signingKey
    Install-EncryptedCredential -Name 'oss-publisher-credential' -Plaintext $ossCredential
}
finally {
    if ($null -ne $token) {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            [byte[]]$token)
    }
    if ($null -ne $signingKey) {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            [byte[]]$signingKey)
    }
    if ($null -ne $ossCredential) {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            [byte[]]$ossCredential)
    }
}
