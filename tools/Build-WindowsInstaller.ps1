[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactRoot = Join-Path $repoRoot "artifacts"
$publishDirectory = Join-Path $artifactRoot "publish\win-x64"
$installerDirectory = Join-Path $artifactRoot "installer"
$projectPath = Join-Path $repoRoot "src\Hechao.Launcher\Hechao.Launcher.csproj"
$solutionPath = Join-Path $repoRoot "Hechao.Launcher.sln"
$installerScript = Join-Path $repoRoot "installer\HechaoLauncher.nsi"

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

& $nsisCompiler `
    "/INPUTCHARSET" `
    "UTF8" `
    "/DAPP_VERSION=$Version" `
    "/DPUBLISH_DIR=$publishDirectory" `
    "/DOUTPUT_DIR=$installerDirectory" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed."
}

$installerPath = Join-Path $installerDirectory "Hechao-Launcher-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The expected installer was not produced."
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$installerPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $(Split-Path $installerPath -Leaf)" -Encoding ascii

Write-Host "Installer: $installerPath"
Write-Host "SHA256:   $hash"
