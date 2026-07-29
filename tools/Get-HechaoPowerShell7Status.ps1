[CmdletBinding()]
param(
    [string]$ExpectedVersion = "7.6.4"
)

$ErrorActionPreference = "Stop"
$machinePwshPath = Join-Path $env:ProgramFiles "PowerShell\7\pwsh.exe"
$portablePwshPath = Join-Path $env:LOCALAPPDATA "Programs\PowerShell\7\PowerShell\7\pwsh.exe"
$pwshPath = if (Test-Path -LiteralPath $machinePwshPath) {
    $machinePwshPath
} elseif (Test-Path -LiteralPath $portablePwshPath) {
    $portablePwshPath
} else {
    $machinePwshPath
}
$installerPath = Join-Path $env:ProgramData "Hechao\Installers\PowerShell-$ExpectedVersion-win-x64.msi"

$version = $null
if (Test-Path -LiteralPath $pwshPath) {
    $version = & $pwshPath -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
}

$installer = Get-Item -LiteralPath $installerPath -ErrorAction SilentlyContinue
$activeProcesses = @(
    Get-Process -Name curl, msiexec -ErrorAction SilentlyContinue |
        Select-Object ProcessName, Id, StartTime
)

[pscustomobject]@{
    installed = $null -ne $version
    version = $version
    path = if ($null -ne $version) { $pwshPath } else { $null }
    installerBytes = if ($null -ne $installer) { $installer.Length } else { 0 }
    activeProcesses = $activeProcesses
} | ConvertTo-Json -Depth 4 -Compress
