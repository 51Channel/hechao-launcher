[CmdletBinding()]
param(
    [string]$SshExecutable = 'C:\Windows\System32\OpenSSH\ssh.exe',

    [string]$ApiHost = 'root@8.148.207.171',

    [string]$ApiKey =
        'C:\Users\Administrator\.ssh\hechao_deploy',

    [string]$ApiKnownHosts =
        'C:\Users\Administrator\.ssh\hechao_old_rebuilt_known_hosts',

    [string]$ApiTokenPath =
        '/opt/hechao-launcher-api/integration-tests/protocol-translation-staging/velocity-token',

    [string]$VelocityHost = 'administrator@owl5.vipi9.top',

    [ValidateRange(1, 65535)]
    [int]$VelocitySshPort = 15152,

    [string]$VelocityKey =
        'C:\Users\Administrator\.ssh\mc_vps',

    [string]$RemoteInstaller =
        'E:\Velocity-PvpReturn-Staging\ops\Set-PvpReturnStagingAuthorization.ps1'
)

$ErrorActionPreference = 'Stop'

foreach ($path in @($SshExecutable, $ApiKey, $ApiKnownHosts, $VelocityKey)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required local file is missing: $path"
    }
}
if ($ApiTokenPath -notmatch '^/opt/hechao-launcher-api/integration-tests/' -or
    $ApiTokenPath -match '[\s''"`|;&<>]') {
    throw 'The API token path is outside the allowed integration-test boundary.'
}
if ($RemoteInstaller -ne
    'E:\Velocity-PvpReturn-Staging\ops\Set-PvpReturnStagingAuthorization.ps1') {
    throw 'The remote installer path is outside the fixed staging boundary.'
}

function New-SshProcess {
    param([Parameter(Mandatory = $true)][string]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $SshExecutable
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start the SSH process.'
    }

    return $process
}

$sourceArguments = @(
    '-i', $ApiKey,
    '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=$ApiKnownHosts",
    $ApiHost,
    'cat', $ApiTokenPath
) -join ' '
$destinationArguments = @(
    '-p', $VelocitySshPort,
    '-i', $VelocityKey,
    '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes',
    $VelocityHost,
    'powershell',
    '-NoProfile',
    '-NonInteractive',
    '-ExecutionPolicy', 'Bypass',
    '-File', $RemoteInstaller,
    '-Action', 'Install'
) -join ' '

$source = $null
$destination = $null
try {
    $source = New-SshProcess -Arguments $sourceArguments
    $destination = New-SshProcess -Arguments $destinationArguments
    $sourceErrorTask = $source.StandardError.ReadToEndAsync()
    $destinationOutputTask = $destination.StandardOutput.ReadToEndAsync()
    $destinationErrorTask = $destination.StandardError.ReadToEndAsync()

    $buffer = [byte[]]::new(4096)
    $transferredBytes = 0
    while (($read = $source.StandardOutput.BaseStream.Read(
                $buffer,
                0,
                $buffer.Length)) -gt 0) {
        $destination.StandardInput.BaseStream.Write($buffer, 0, $read)
        $transferredBytes += $read
        if ($transferredBytes -gt 256) {
            throw 'The staging credential stream exceeded the maximum allowed size.'
        }
    }
    $destination.StandardInput.BaseStream.Flush()
    $destination.StandardInput.Close()

    $source.WaitForExit()
    $destination.WaitForExit()
    $sourceError = $sourceErrorTask.GetAwaiter().GetResult()
    $destinationOutput = $destinationOutputTask.GetAwaiter().GetResult()
    $destinationError = $destinationErrorTask.GetAwaiter().GetResult()

    if ($source.ExitCode -ne 0) {
        throw "Unable to read the staging credential over SSH: $sourceError"
    }
    if ($transferredBytes -lt 24) {
        throw 'The staging credential stream was shorter than the minimum allowed size.'
    }
    if ($destination.ExitCode -ne 0) {
        throw "Unable to install the staging credential over SSH: $destinationError"
    }

    if (-not [string]::IsNullOrWhiteSpace($destinationOutput)) {
        $destinationOutput.Trim()
    }
    [pscustomobject]@{
        RawByteTransfer = $true
        TransferredBytes = $transferredBytes
        CredentialDisclosed = $false
        CredentialWrittenToLocalDisk = $false
        SourceExitCode = $source.ExitCode
        DestinationExitCode = $destination.ExitCode
    } | ConvertTo-Json
}
finally {
    foreach ($process in @($source, $destination)) {
        if ($null -eq $process) {
            continue
        }
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}
