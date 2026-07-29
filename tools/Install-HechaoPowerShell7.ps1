[CmdletBinding()]
param(
    [string]$Version = "7.6.4",
    [string]$ExpectedSha256 = "D11942DF52FD12470169797ABFA4781D9480EFDC81000BA4FA55A5B921ED8DD0",
    [string]$InstallerRoot = (Join-Path $env:ProgramData "Hechao\Installers"),
    [string]$PortableRoot = (Join-Path $env:LOCALAPPDATA "Programs\PowerShell\7"),
    [string]$DownloadUri,
    [switch]$KeepInstaller
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$machinePwshPath = Join-Path $env:ProgramFiles "PowerShell\7\pwsh.exe"
$portablePwshPath = Join-Path $PortableRoot "PowerShell\7\pwsh.exe"
$pwshPath = if (Test-Path -LiteralPath $machinePwshPath) {
    $machinePwshPath
} elseif (Test-Path -LiteralPath $portablePwshPath) {
    $portablePwshPath
} else {
    $machinePwshPath
}
$requestedVersion = [version]$Version

function Add-PwshToUserPath {
    param([Parameter(Mandatory)][string]$Path)

    $directory = Split-Path -Parent $Path
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $entries = @(
        $userPath -split ";" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    if ($entries -notcontains $directory) {
        [Environment]::SetEnvironmentVariable(
            "Path",
            (($entries + $directory) -join ";"),
            "User"
        )
    }
}

if (Test-Path -LiteralPath $pwshPath) {
    $currentVersion = [version](& $pwshPath -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')
    if ($currentVersion -ge $requestedVersion) {
        Add-PwshToUserPath -Path $pwshPath
        [pscustomobject]@{
            status = "already-installed"
            version = $currentVersion.ToString()
            path = $pwshPath
        } | ConvertTo-Json -Compress
        return
    }
}

New-Item -ItemType Directory -Path $InstallerRoot -Force | Out-Null
$installerPath = Join-Path $InstallerRoot "PowerShell-$Version-win-x64.msi"
if ([string]::IsNullOrWhiteSpace($DownloadUri)) {
    $DownloadUri = "https://github.com/PowerShell/PowerShell/releases/download/v$Version/PowerShell-$Version-win-x64.msi"
}

$installerReady = $false
if (Test-Path -LiteralPath $installerPath) {
    $existingHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    $installerReady = $existingHash -eq $ExpectedSha256
}

if (-not $installerReady) {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -ne $curl) {
        & $curl.Source --fail --location --retry 5 --retry-delay 2 --continue-at - --output $installerPath $DownloadUri
        if ($LASTEXITCODE -ne 0) {
            $downloadedHash = if (Test-Path -LiteralPath $installerPath) {
                (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
            } else {
                ""
            }

            if ($downloadedHash -ne $ExpectedSha256) {
                throw "PowerShell installer download failed with exit code $LASTEXITCODE."
            }
        }
    } else {
        if (Test-Path -LiteralPath $installerPath) {
            Remove-Item -LiteralPath $installerPath -Force
        }

        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $DownloadUri -OutFile $installerPath -UseBasicParsing
    }
}

$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if ($actualHash -ne $ExpectedSha256) {
    throw "PowerShell installer SHA256 mismatch. Expected $ExpectedSha256, got $actualHash."
}

$signature = Get-AuthenticodeSignature -FilePath $installerPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "PowerShell installer signature is not valid: $($signature.Status)."
}

if ($signature.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
    throw "PowerShell installer signer is not Microsoft Corporation."
}

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
$isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isElevated) {
    $msiLogPath = Join-Path $InstallerRoot "PowerShell-$Version-install.log"
    $msiArguments = @(
        "/i"
        "`"$installerPath`""
        "/qn"
        "/norestart"
        "/L*v"
        "`"$msiLogPath`""
        "ADD_PATH=1"
        "ENABLE_PSREMOTING=0"
        "REGISTER_MANIFEST=1"
        "USE_MU=1"
        "ENABLE_MU=1"
    )

    $installer = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArguments -Wait -PassThru
    if ($installer.ExitCode -notin @(0, 3010)) {
        throw "PowerShell MSI installation failed with exit code $($installer.ExitCode). See $msiLogPath."
    }

    $pwshPath = $machinePwshPath
} else {
    $msiLogPath = Join-Path $InstallerRoot "PowerShell-$Version-admin-extract.log"
    $msiArguments = @(
        "/a"
        "`"$installerPath`""
        "/qn"
        "/L*v"
        "`"$msiLogPath`""
        "TARGETDIR=`"$PortableRoot`""
    )

    $installer = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "PowerShell portable extraction failed with exit code $($installer.ExitCode). See $msiLogPath."
    }

    $pwshPath = $portablePwshPath
    Add-PwshToUserPath -Path $pwshPath
}

if (-not (Test-Path -LiteralPath $pwshPath)) {
    throw "PowerShell MSI completed but pwsh.exe was not found."
}

$installedVersion = [version](& $pwshPath -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')
if ($installedVersion -lt $requestedVersion) {
    throw "Installed PowerShell version $installedVersion is older than requested version $requestedVersion."
}

if (-not $KeepInstaller) {
    Remove-Item -LiteralPath $installerPath -Force
}

[pscustomobject]@{
    status = "installed"
    version = $installedVersion.ToString()
    path = $pwshPath
    sha256 = $actualHash
    signer = $signature.SignerCertificate.Subject
    restartRequired = $isElevated -and $installer.ExitCode -eq 3010
} | ConvertTo-Json -Compress
