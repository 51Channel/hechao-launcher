[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$SkipSigning,
    [string]$SigningCertificateThumbprint = $env:HECHAO_SIGNING_CERT_THUMBPRINT,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$SigningCertificateStoreLocation = "CurrentUser",
    [string]$SigningTimestampUrl = $env:HECHAO_SIGNING_TIMESTAMP_URL,
    [string]$SignToolPath = $env:HECHAO_SIGNTOOL_PATH
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactRoot = Join-Path $repoRoot "artifacts"
$publishDirectory = Join-Path $artifactRoot "publish\win-x64"
$installerDirectory = Join-Path $artifactRoot "installer"
$projectPath = Join-Path $repoRoot "src\Hechao.Launcher\Hechao.Launcher.csproj"
$solutionPath = Join-Path $repoRoot "Hechao.Launcher.sln"
$installerScript = Join-Path $repoRoot "installer\HechaoLauncher.nsi"
$signingScript = Join-Path $repoRoot "tools\Invoke-WindowsCodeSigning.ps1"

function Resolve-Dotnet {
    $bundledCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path $repoRoot "..\.dotnet\dotnet.exe"))
    if (Test-Path -LiteralPath $bundledCandidate) {
        return $bundledCandidate
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw ".NET SDK was not found."
    }

    return $command.Source
}

function Resolve-NsisCompiler {
    if (-not [string]::IsNullOrWhiteSpace($env:NSIS_COMPILER) -and
        (Test-Path -LiteralPath $env:NSIS_COMPILER)) {
        return [System.IO.Path]::GetFullPath($env:NSIS_COMPILER)
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"),
        (Join-Path $env:ProgramFiles "NSIS\makensis.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\NSIS\makensis.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "NSIS was not found. Install package NSIS.NSIS or set NSIS_COMPILER."
}

function Write-Sha256Sidecar {
    param([string]$Path)

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashPath = "$Path.sha256"
    Set-Content `
        -LiteralPath $hashPath `
        -Value "$hash  $(Split-Path $Path -Leaf)" `
        -Encoding ascii

    return $hash
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $versionNode = Select-Xml -Xml $projectXml -XPath "/Project/PropertyGroup/Version" |
        Select-Object -First 1
    if ($null -eq $versionNode) {
        throw "The launcher project does not define a Version."
    }

    $Version = $versionNode.Node.InnerText.Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use major.minor.patch format."
}

$normalizedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot) +
    [System.IO.Path]::DirectorySeparatorChar
$normalizedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $normalizedPublishDirectory.StartsWith(
        $normalizedArtifactRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish directory is outside the repository artifact directory."
}

$dotnet = Resolve-Dotnet
$nsisCompiler = Resolve-NsisCompiler
$signingParameters = @{
    CertificateThumbprint = $SigningCertificateThumbprint
    CertificateStoreLocation = $SigningCertificateStoreLocation
    TimestampUrl = $SigningTimestampUrl
    SignToolPath = $SignToolPath
}

if (-not $SkipSigning) {
    if (-not (Test-Path -LiteralPath $signingScript -PathType Leaf)) {
        throw "The Windows code-signing script was not found."
    }

    & $signingScript -ValidateEnvironment @signingParameters
}

if (-not $SkipTests) {
    & $dotnet test $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

& $dotnet publish $projectPath `
    -c Release `
    -p:PublishProfile=win-x64 `
    "-p:Version=$Version" `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Launcher publish failed."
}

$launcherPath = Join-Path $publishDirectory "Hechao.Launcher.exe"
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "The expected launcher executable was not produced."
}

if (-not $SkipSigning) {
    & $signingScript -Path $launcherPath @signingParameters
}

$nsisArguments = @(
    "/INPUTCHARSET",
    "UTF8",
    "/DAPP_VERSION=$Version",
    "/DPUBLISH_DIR=$publishDirectory",
    "/DOUTPUT_DIR=$installerDirectory"
)

$originalSigningEnvironment = @{}
$signingEnvironment = @{
    HECHAO_SIGNING_CERT_THUMBPRINT = $SigningCertificateThumbprint
    HECHAO_SIGNING_CERT_STORE = $SigningCertificateStoreLocation
    HECHAO_SIGNING_TIMESTAMP_URL = $SigningTimestampUrl
    HECHAO_SIGNTOOL_PATH = $SignToolPath
}

if (-not $SkipSigning) {
    $powerShellExecutable = (Get-Process -Id $PID).Path
    $nsisArguments += @(
        "/DSIGN_UNINSTALLER=1",
        "/DPOWERSHELL_EXE=$powerShellExecutable",
        "/DSIGNING_SCRIPT=$signingScript"
    )

    foreach ($entry in $signingEnvironment.GetEnumerator()) {
        $existing = Get-Item -LiteralPath "Env:\$($entry.Key)" -ErrorAction SilentlyContinue
        $originalSigningEnvironment[$entry.Key] = if ($null -eq $existing) {
            $null
        } else {
            $existing.Value
        }
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
            Remove-Item `
                -LiteralPath "Env:\$($entry.Key)" `
                -ErrorAction SilentlyContinue
        } else {
            Set-Item -LiteralPath "Env:\$($entry.Key)" -Value $entry.Value
        }
    }
}

try {
    & $nsisCompiler @nsisArguments $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed."
    }
}
finally {
    if (-not $SkipSigning) {
        foreach ($entry in $originalSigningEnvironment.GetEnumerator()) {
            if ($null -eq $entry.Value) {
                Remove-Item `
                    -LiteralPath "Env:\$($entry.Key)" `
                    -ErrorAction SilentlyContinue
            } else {
                Set-Item -LiteralPath "Env:\$($entry.Key)" -Value $entry.Value
            }
        }
    }
}

$installerPath = Join-Path $installerDirectory "Hechao-Launcher-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The expected installer was not produced."
}

if (-not $SkipSigning) {
    & $signingScript -Path $installerPath @signingParameters
} else {
    Write-Warning "This is an unsigned development build and must not be distributed."
}

$launcherHash = Write-Sha256Sidecar -Path $launcherPath
$installerHash = Write-Sha256Sidecar -Path $installerPath

Write-Host "Launcher:        $launcherPath"
Write-Host "Launcher SHA256: $launcherHash"
Write-Host "Installer:       $installerPath"
Write-Host "Installer SHA256: $installerHash"
