[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$CandidateVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$BaselineVersion
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$validationRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "artifacts\validation"))
$runRoot = [IO.Path]::GetFullPath(
    (Join-Path $validationRoot (
        "installer-$CandidateVersion-" + [Guid]::NewGuid().ToString("N"))))
$candidateInstaller = Join-Path $repoRoot (
    "artifacts\installer\Hechao-Launcher-Setup-$CandidateVersion-win-x64.exe")
$baselineInstaller = Join-Path $repoRoot (
    "artifacts\installer\Hechao-Launcher-Setup-$BaselineVersion-win-x64.exe")
$registryApp = "HKCU:\Software\Hechao\Launcher"
$registryUninstall =
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HechaoLauncher"
$programsRoot = [IO.Path]::GetFullPath(
    [Environment]::GetFolderPath("Programs"))
$launcherDisplayName = -join @(
    [char]0x8D6B,
    [char]0x671D,
    [char]0x542F,
    [char]0x52A8,
    [char]0x5668)
$startMenuPath = [IO.Path]::GetFullPath(
    (Join-Path $programsRoot $launcherDisplayName))
$upgradeDirectory = Join-Path $runRoot "upgrade-app"
$cleanDirectory = Join-Path $runRoot "clean-app"
$evidencePath = Join-Path $validationRoot (
    "launcher-$CandidateVersion-installer-validation.json")

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$AllowedRoot
    )

    $normalizedPath = [IO.Path]::GetFullPath($Path)
    $normalizedRoot = [IO.Path]::GetFullPath($AllowedRoot) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $normalizedPath.StartsWith(
            $normalizedRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path outside the expected root: $normalizedPath"
    }

    return $normalizedPath
}

function Remove-SafeTree {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $normalizedPath = Assert-ChildPath -Path $Path -AllowedRoot $AllowedRoot
    Remove-Item -LiteralPath $normalizedPath -Recurse -Force
}

function Get-OptionalHash {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }

    return $null
}

function Invoke-SilentInstaller {
    param(
        [Parameter(Mandatory)]
        [string]$Installer,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    $process = Start-Process `
        -FilePath $Installer `
        -ArgumentList @("/S", "/D=$Destination") `
        -PassThru `
        -Wait `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Installer failed with exit code $($process.ExitCode)."
    }
}

function Invoke-SilentUninstaller {
    param([Parameter(Mandatory)][string]$Destination)

    $uninstaller = Join-Path $Destination "Uninstall.exe"
    if (-not (Test-Path -LiteralPath $uninstaller)) {
        throw "The uninstaller is missing: $uninstaller"
    }

    $process = Start-Process `
        -FilePath $uninstaller `
        -ArgumentList "/S" `
        -PassThru `
        -Wait `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Uninstaller failed with exit code $($process.ExitCode)."
    }
}

function Assert-InstalledVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Destination,

        [Parameter(Mandatory)]
        [string]$ExpectedVersion
    )

    $launcher = Join-Path $Destination "Hechao.Launcher.exe"
    if (-not (Test-Path -LiteralPath $launcher)) {
        throw "The installed launcher is missing."
    }

    $actualVersion = (Get-Item -LiteralPath $launcher).VersionInfo.FileVersion
    if ($actualVersion -ne "$ExpectedVersion.0") {
        throw "Expected launcher $ExpectedVersion, found $actualVersion."
    }

    foreach ($asset in @(
            "Assets\IconPark\LICENSE",
            "Assets\IconPark\NOTICE.md")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Destination $asset))) {
            throw "The installed asset is missing: $asset"
        }
    }
}

foreach ($installer in @($baselineInstaller, $candidateInstaller)) {
    if (-not (Test-Path -LiteralPath $installer)) {
        throw "Installer not found: $installer"
    }
}

[void](Assert-ChildPath -Path $runRoot -AllowedRoot $validationRoot)
[void](Assert-ChildPath -Path $startMenuPath -AllowedRoot $programsRoot)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$appRegistryBackup = Join-Path $runRoot "app.reg"
$uninstallRegistryBackup = Join-Path $runRoot "uninstall.reg"
$startMenuBackup = Join-Path $runRoot "start-menu-backup"
$appRegistryExisted = Test-Path -LiteralPath $registryApp
$uninstallRegistryExisted = Test-Path -LiteralPath $registryUninstall
$startMenuExisted = Test-Path -LiteralPath $startMenuPath

if ($appRegistryExisted) {
    & reg.exe export `
        "HKCU\Software\Hechao\Launcher" `
        $appRegistryBackup `
        /y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to back up the launcher registry key."
    }
}

if ($uninstallRegistryExisted) {
    & reg.exe export `
        "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HechaoLauncher" `
        $uninstallRegistryBackup `
        /y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to back up the uninstall registry key."
    }
}

if ($startMenuExisted) {
    Copy-Item `
        -LiteralPath $startMenuPath `
        -Destination $startMenuBackup `
        -Recurse `
        -Force
}

$settingsPath = Join-Path $env:LOCALAPPDATA "Hechao\Launcher\settings.json"
$sessionPath = Join-Path $env:LOCALAPPDATA "Hechao\Launcher\session.dat"
$settingsBefore = Get-OptionalHash -Path $settingsPath
$sessionBefore = Get-OptionalHash -Path $sessionPath
$processesBefore = @(
    Get-CimInstance `
        -ClassName Win32_Process `
        -Filter "Name='Hechao.Launcher.exe'" |
        Select-Object ProcessId, ExecutablePath)
$succeeded = $false

try {
    Invoke-SilentInstaller `
        -Installer $baselineInstaller `
        -Destination $upgradeDirectory
    Assert-InstalledVersion `
        -Destination $upgradeDirectory `
        -ExpectedVersion $BaselineVersion

    Invoke-SilentInstaller `
        -Installer $candidateInstaller `
        -Destination $upgradeDirectory
    Assert-InstalledVersion `
        -Destination $upgradeDirectory `
        -ExpectedVersion $CandidateVersion

    $displayVersion = (
        Get-ItemProperty -LiteralPath $registryUninstall).DisplayVersion
    if ($displayVersion -ne $CandidateVersion) {
        throw "The uninstall registry version is $displayVersion."
    }

    $shortcutPath = Join-Path $startMenuPath "$launcherDisplayName.lnk"
    if (-not (Test-Path -LiteralPath $shortcutPath)) {
        throw "The Start menu shortcut is missing."
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcutTarget = $shell.CreateShortcut($shortcutPath).TargetPath
    $expectedTarget = Join-Path $upgradeDirectory "Hechao.Launcher.exe"
    if (-not [IO.Path]::GetFullPath($shortcutTarget).Equals(
            [IO.Path]::GetFullPath($expectedTarget),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Start menu shortcut points to an unexpected launcher."
    }

    Invoke-SilentUninstaller -Destination $upgradeDirectory
    if (Test-Path -LiteralPath $upgradeDirectory) {
        throw "The upgrade test directory remains after uninstall."
    }

    Invoke-SilentInstaller `
        -Installer $candidateInstaller `
        -Destination $cleanDirectory
    Assert-InstalledVersion `
        -Destination $cleanDirectory `
        -ExpectedVersion $CandidateVersion
    Invoke-SilentUninstaller -Destination $cleanDirectory
    if (Test-Path -LiteralPath $cleanDirectory) {
        throw "The clean install directory remains after uninstall."
    }

    if ((Get-OptionalHash -Path $settingsPath) -ne $settingsBefore) {
        throw "settings.json changed during installer validation."
    }

    if ((Get-OptionalHash -Path $sessionPath) -ne $sessionBefore) {
        throw "session.dat changed during installer validation."
    }

    $processesAfter = @(
        Get-CimInstance `
            -ClassName Win32_Process `
            -Filter "Name='Hechao.Launcher.exe'" |
            Select-Object ProcessId, ExecutablePath)
    foreach ($process in $processesBefore) {
        if ($processesAfter.ProcessId -notcontains $process.ProcessId) {
            throw "Existing launcher process $($process.ProcessId) was terminated."
        }
    }

    $launcher = Join-Path $repoRoot (
        "artifacts\publish\win-x64\Hechao.Launcher.exe")
    $evidence = [ordered]@{
        validatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        upgradeFrom = $BaselineVersion
        upgradeTo = $CandidateVersion
        upgradeInstall = $true
        cleanInstall = $true
        uninstallRounds = 2
        settingsPreserved = $true
        sessionPreserved = $true
        existingLauncherProcessesPreserved = $true
        installerSha256 = (
            Get-FileHash `
                -LiteralPath $candidateInstaller `
                -Algorithm SHA256).Hash
        installerBytes = (Get-Item -LiteralPath $candidateInstaller).Length
        exeSha256 = (
            Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash
        exeBytes = (Get-Item -LiteralPath $launcher).Length
    }
    $evidence |
        ConvertTo-Json |
        Set-Content -LiteralPath $evidencePath -Encoding UTF8
    $succeeded = $true
}
finally {
    Remove-SafeTree -Path $upgradeDirectory -AllowedRoot $runRoot
    Remove-SafeTree -Path $cleanDirectory -AllowedRoot $runRoot
    Remove-SafeTree -Path $startMenuPath -AllowedRoot $programsRoot
    if ($startMenuExisted) {
        Copy-Item `
            -LiteralPath $startMenuBackup `
            -Destination $startMenuPath `
            -Recurse `
            -Force
    }

    if (Test-Path -LiteralPath $registryApp) {
        Remove-Item -LiteralPath $registryApp -Recurse -Force
    }

    if (Test-Path -LiteralPath $registryUninstall) {
        Remove-Item -LiteralPath $registryUninstall -Recurse -Force
    }

    if ($appRegistryExisted) {
        & reg.exe import $appRegistryBackup | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to restore the launcher registry key."
        }
    }

    if ($uninstallRegistryExisted) {
        & reg.exe import $uninstallRegistryBackup | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to restore the uninstall registry key."
        }
    }

    Remove-SafeTree -Path $runRoot -AllowedRoot $validationRoot
}

if (-not $succeeded) {
    throw "Installer validation did not complete."
}

Get-Content -LiteralPath $evidencePath -Encoding UTF8
