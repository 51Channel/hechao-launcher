#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostName,
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$KeyPath,
    [string]$UserName = "administrator",
    [string]$Version = "7.6.4",
    [string]$ExpectedSha256 = "D11942DF52FD12470169797ABFA4781D9480EFDC81000BA4FA55A5B921ED8DD0",
    [string]$InstallerPath = "C:\ProgramData\Hechao\Installers\PowerShell-7.6.4-win-x64.msi",
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "This deployment script requires PowerShell 7."
}

$bootstrapScript = Join-Path $RepositoryRoot "tools\Install-HechaoPowerShell7.ps1"
$statusScript = Join-Path $RepositoryRoot "tools\Get-HechaoPowerShell7Status.ps1"

foreach ($path in @($KeyPath, $InstallerPath, $bootstrapScript, $statusScript)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$actualHash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
if ($actualHash -ne $ExpectedSha256) {
    throw "PowerShell installer SHA256 mismatch."
}

$signature = Get-AuthenticodeSignature -FilePath $InstallerPath
if (
    $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $signature.SignerCertificate.Subject -notmatch "Microsoft Corporation"
) {
    throw "PowerShell installer signature validation failed."
}

$sshBase = @(
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-p", $Port.ToString()
    "$UserName@$HostName"
)

$scpBase = @(
    "-q"
    "-o", "BatchMode=yes"
    "-o", "ConnectTimeout=10"
    "-o", "StrictHostKeyChecking=yes"
    "-i", $KeyPath
    "-P", $Port.ToString()
)

$versionProbe = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes('$PSVersionTable.PSVersion.ToString()')
)
$remoteVersionOutput = & ssh.exe @sshBase "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -EncodedCommand $versionProbe" 2>$null
if ($LASTEXITCODE -eq 0) {
    $remoteVersionText = $remoteVersionOutput | Select-Object -Last 1
    $remoteVersion = $null
    if ([version]::TryParse($remoteVersionText, [ref]$remoteVersion)) {
        if ($remoteVersion -ge [version]$Version) {
            [pscustomobject]@{
                host = $HostName
                status = "already-installed-and-verified"
                version = $remoteVersion.ToString()
                path = "C:\Program Files\PowerShell\7\pwsh.exe"
            } | ConvertTo-Json -Compress
            return
        }
    }
}

$prepareCommand = @"
`$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path "C:\ProgramData\Hechao\Installers" -Force | Out-Null
Get-CimInstance Win32_Process -Filter "Name='curl.exe'" |
    Where-Object { `$_.CommandLine -like "*PowerShell-$Version-win-x64.msi*" } |
    ForEach-Object { Stop-Process -Id `$_.ProcessId -Force }
"@
$encodedPrepareCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($prepareCommand)
)

& ssh.exe @sshBase "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedPrepareCommand"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to prepare $HostName for the PowerShell 7 bootstrap."
}

$uploads = @(
    @{
        Local = $bootstrapScript
        Remote = "C:/ProgramData/Hechao/Install-HechaoPowerShell7.ps1"
    }
    @{
        Local = $statusScript
        Remote = "C:/ProgramData/Hechao/Get-HechaoPowerShell7Status.ps1"
    }
    @{
        Local = $InstallerPath
        Remote = "C:/ProgramData/Hechao/Installers/PowerShell-$Version-win-x64.msi"
    }
)

foreach ($upload in $uploads) {
    & scp.exe @scpBase $upload.Local "${UserName}@${HostName}:$($upload.Remote)"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload $($upload.Local) to $HostName."
    }
}

$installCommand = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\ProgramData\Hechao\Install-HechaoPowerShell7.ps1 -KeepInstaller"
& ssh.exe @sshBase $installCommand
if ($LASTEXITCODE -ne 0) {
    throw "PowerShell 7 installation failed on $HostName."
}

$verifyCommand = "C:\Progra~1\PowerShell\7\pwsh.exe -NoLogo -NoProfile -File C:\ProgramData\Hechao\Get-HechaoPowerShell7Status.ps1"
$verification = & ssh.exe @sshBase $verifyCommand
if ($LASTEXITCODE -ne 0) {
    throw "PowerShell 7 verification failed on $HostName."
}

$status = $verification | Select-Object -Last 1 | ConvertFrom-Json
if (-not $status.installed -or [version]$status.version -lt [version]$Version) {
    throw "PowerShell 7 verification returned an unexpected result on $HostName."
}

[pscustomobject]@{
    host = $HostName
    status = "installed-and-verified"
    version = $status.version
    path = $status.path
    installerSha256 = $actualHash
} | ConvertTo-Json -Compress
