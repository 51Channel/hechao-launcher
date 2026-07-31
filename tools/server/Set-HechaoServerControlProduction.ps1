#Requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('true', 'false')]
    [string]$Enabled = 'false',

    [string]$ApiHostName = '8.148.207.171',
    [string]$ApiUserName = 'root',
    [ValidateRange(1, 65535)]
    [int]$ApiPort = 22,
    [string]$ApiIdentityFile = "$HOME\.ssh\hechao_deploy",
    [string]$ApiKnownHostsFile =
        "$HOME\.ssh\hechao_old_rebuilt_known_hosts",
    [string]$ApiEnvironmentFile =
        '/etc/hechao-launcher-api/environment',

    [string]$Owl5HostName = 'owl5.vipi9.top',
    [ValidateRange(1, 65535)]
    [int]$Owl5Port = 15152,
    [string]$Owl5IdentityFile = "$HOME\.ssh\mc_vps",

    [string]$Owl9HostName = 'owl9.vipi9.top',
    [ValidateRange(1, 65535)]
    [int]$Owl9Port = 19241,
    [string]$Owl9IdentityFile = "$HOME\.ssh\id_ed25519",

    [ValidateRange(10, 300)]
    [int]$AgentFreshnessSeconds = 30,
    [ValidateRange(30, 600)]
    [int]$ClaimLeaseSeconds = 120,
    [string]$ConfigureScript = (Join-Path $PSScriptRoot `
        '..\..\deploy\linux\configure-server-control.sh'),
    [switch]$RestartApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-File {
    param([Parameter(Mandatory)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Required file is missing: $LiteralPath"
    }
}

function ConvertTo-ShellLiteral {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value.Contains("'")) {
        throw 'Single quotes are not allowed in production control values.'
    }

    return "'$Value'"
}

function Get-WindowsTokenDigest {
    param(
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$IdentityFile
    )

    $remoteScript = @'
Add-Type -AssemblyName System.Security
$path = 'C:\ProgramData\Hechao\ServerControlAgent\server-control-token.dat'
$protected = [System.IO.File]::ReadAllBytes($path)
$clear = [System.Security.Cryptography.ProtectedData]::Unprotect(
    $protected,
    $null,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
try {
    [Console]::Out.Write([Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($clear)))
}
finally {
    [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($clear)
}
'@
    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($remoteScript))
    $arguments = @(
        '-i', (Resolve-Path -LiteralPath $IdentityFile).Path,
        '-p', $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=15',
        '-o', 'StrictHostKeyChecking=yes',
        "administrator@$HostName",
        'C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile ' +
            "-EncodedCommand $encoded"
    )
    $output = & ssh.exe @arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not verify the protected token on $HostName."
    }

    $digest = ($output -join '').Trim()
    if ($digest -notmatch '^[A-F0-9]{64}$') {
        throw "The protected token on $HostName returned an invalid digest."
    }

    return $digest
}

function New-ApiSshArguments {
    param([Parameter(Mandatory)][string]$RemoteCommand)

    return @(
        '-i', (Resolve-Path -LiteralPath $ApiIdentityFile).Path,
        '-p', $ApiPort.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=15',
        '-o', 'StrictHostKeyChecking=yes',
        '-o', "UserKnownHostsFile=$((Resolve-Path -LiteralPath `
            $ApiKnownHostsFile).Path)",
        "$ApiUserName@$ApiHostName",
        $RemoteCommand
    )
}

function Invoke-ApiSshInput {
    param([Parameter(Mandatory)][string]$Payload)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'ssh.exe'
    foreach ($argument in (New-ApiSshArguments -RemoteCommand 'bash -s')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Could not start API SSH process.'
    }

    $process.StandardInput.Write($Payload)
    $process.StandardInput.Close()
    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        $safeOutput = "$standardOutput`n$standardError" -replace `
            '(?i)[a-f0-9]{64}', '<redacted>'
        throw "API configuration failed: $safeOutput"
    }

    return $standardOutput
}

function Invoke-ApiSshText {
    param([Parameter(Mandatory)][string]$RemoteCommand)

    $arguments = New-ApiSshArguments -RemoteCommand $RemoteCommand
    $output = & ssh.exe @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "API SSH command failed: $($output -join [Environment]::NewLine)"
    }

    return ($output -join [Environment]::NewLine).Trim()
}

Assert-File -LiteralPath $ApiIdentityFile
Assert-File -LiteralPath $ApiKnownHostsFile
Assert-File -LiteralPath $Owl5IdentityFile
Assert-File -LiteralPath $Owl9IdentityFile
Assert-File -LiteralPath $ConfigureScript

$owl5Digest = Get-WindowsTokenDigest `
    -HostName $Owl5HostName `
    -Port $Owl5Port `
    -IdentityFile $Owl5IdentityFile
$owl9Digest = Get-WindowsTokenDigest `
    -HostName $Owl9HostName `
    -Port $Owl9Port `
    -IdentityFile $Owl9IdentityFile
if ($owl5Digest -eq $owl9Digest) {
    throw 'owl5 and owl9 must not share a server-control token.'
}

$configureBody = [IO.File]::ReadAllText(
    (Resolve-Path -LiteralPath $ConfigureScript).Path).Replace("`r`n", "`n")
$setArguments = @(
    $ApiEnvironmentFile,
    $Enabled,
    $AgentFreshnessSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    $ClaimLeaseSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    "owl5=$owl5Digest",
    "owl9=$owl9Digest"
) | ForEach-Object { ConvertTo-ShellLiteral -Value $_ }
$payload = "set -- $($setArguments -join ' ')`n" +
    $configureBody.TrimEnd() + "`n"
$configurationOutput = Invoke-ApiSshInput -Payload $payload
$backupMatch = [regex]::Match(
    $configurationOutput,
    '(?m)^backup=(?<path>[^\r\n]+)$')
if (-not $backupMatch.Success) {
    throw 'API configuration completed without returning a backup path.'
}
$backupPath = $backupMatch.Groups['path'].Value
$expectedBackupPrefix = "$ApiEnvironmentFile.server-control."
if (-not $backupPath.StartsWith(
        $expectedBackupPrefix,
        [StringComparison]::Ordinal) -or
    -not $backupPath.EndsWith('.bak', [StringComparison]::Ordinal)) {
    throw 'API configuration returned an unsafe backup path.'
}

$serviceStatus = 'not-restarted'
if ($RestartApi) {
    $enabledLiteral = ConvertTo-ShellLiteral -Value `
        "ServerControl__Enabled=$Enabled"
    $environmentLiteral = ConvertTo-ShellLiteral -Value $ApiEnvironmentFile
    $verifyCommand = @"
set -e
systemctl restart hechao-launcher-api.service
for i in `$(seq 1 30); do
  if curl -fsS http://127.0.0.1:8090/readyz >/dev/null; then break; fi
  sleep 1
done
curl -fsS http://127.0.0.1:8090/readyz >/dev/null
test "`$(grep -c '^ServerControl__AgentTokenSha256__' $environmentLiteral)" -eq 2
grep -Fqx $enabledLiteral $environmentLiteral
systemctl is-active hechao-launcher-api.service
"@
    try {
        $serviceStatus = Invoke-ApiSshText -RemoteCommand $verifyCommand
    }
    catch {
        $backupLiteral = ConvertTo-ShellLiteral -Value $backupPath
        $rollbackCommand = @"
set -e
cp --preserve=mode,ownership,timestamps -- $backupLiteral $environmentLiteral
systemctl restart hechao-launcher-api.service
"@
        try {
            Invoke-ApiSshText -RemoteCommand $rollbackCommand | Out-Null
        }
        catch {
            throw 'API restart failed and environment rollback also failed.'
        }
        throw
    }
}

[ordered]@{
    enabled = [bool]::Parse($Enabled)
    agents = @('owl5', 'owl9')
    protected_tokens_verified = 2
    configured_digests = 2
    api_status = $serviceStatus
    backup_created = $true
    secret_output = $false
} | ConvertTo-Json -Compress
