#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostName,
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$KeyPath,
    [string]$UserName = "administrator"
)

$ErrorActionPreference = "Stop"
$migrationScript = Join-Path $PSScriptRoot "Convert-HechaoScheduledTasksToPowerShell7.ps1"

foreach ($path in @($KeyPath, $migrationScript)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$remoteFileName = "Convert-HechaoScheduledTasksToPowerShell7.ps1"
$scpArguments = @(
    "-q"
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-P", $Port.ToString()
    $migrationScript
    "${UserName}@${HostName}:$remoteFileName"
)

& scp.exe @scpArguments
if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload the scheduled-task migration script to $HostName."
}

$sshArguments = @(
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-p", $Port.ToString()
    "$UserName@$HostName"
    "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -File C:\Users\$UserName\$remoteFileName"
)

& ssh.exe @sshArguments
if ($LASTEXITCODE -ne 0) {
    throw "Scheduled-task migration failed on $HostName."
}
