#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostName,
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$KeyPath,
    [string]$UserName = "administrator"
)

$ErrorActionPreference = "Stop"
$auditScript = Join-Path $PSScriptRoot "Get-HechaoPowerShellTaskAudit.ps1"

foreach ($path in @($KeyPath, $auditScript)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$scpArguments = @(
    "-q"
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-P", $Port.ToString()
    $auditScript
    "${UserName}@${HostName}:Get-HechaoPowerShellTaskAudit.ps1"
)

& scp.exe @scpArguments
if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload the scheduled-task audit script to $HostName."
}

$sshArguments = @(
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-p", $Port.ToString()
    "$UserName@$HostName"
    "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -File C:\Users\$UserName\Get-HechaoPowerShellTaskAudit.ps1"
)

& ssh.exe @sshArguments
if ($LASTEXITCODE -ne 0) {
    throw "Scheduled-task audit failed on $HostName."
}
