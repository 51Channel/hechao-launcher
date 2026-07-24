[CmdletBinding()]
param(
    [string]$Version = "10.0.26100.7705"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$knownPackages = @{
    "10.0.26100.7705" = "48A81375752F9F1FF56A34062084B426BFE412A5A8072E1C99B6A4BE0E774841"
}
if (-not $knownPackages.ContainsKey($Version)) {
    throw "Windows SDK Build Tools version '$Version' is not pinned by this repository."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$toolRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactRoot "tools\windows-sdk-buildtools\$Version"))
$packageRoot = Join-Path $toolRoot "package"
$packageName = "microsoft.windows.sdk.buildtools.$Version.nupkg"
$packagePath = Join-Path $toolRoot $packageName
$packageUri = "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$Version/$packageName"

function Assert-PathInsideToolRoot {
    param([string]$Path)

    $normalizedToolRoot = $toolRoot + [System.IO.Path]::DirectorySeparatorChar
    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $normalizedPath.StartsWith(
            $normalizedToolRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The signing-tool path is outside the expected artifact directory."
    }
}

function Resolve-VerifiedSignTool {
    param([string]$Root)

    $signTool = Get-ChildItem `
        -LiteralPath $Root `
        -Filter signtool.exe `
        -File `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $signTool) {
        throw "The Windows SDK package does not contain x64 SignTool."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $signTool.FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "The downloaded SignTool does not have a valid Microsoft signature."
    }

    return $signTool.FullName
}

if (Test-Path -LiteralPath $packageRoot -PathType Container) {
    $existingSignTool = Resolve-VerifiedSignTool -Root $packageRoot
    Write-Host "Windows signing tools are already installed."
    Write-Host "SignTool: $existingSignTool"
    return
}

New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
Assert-PathInsideToolRoot -Path $packagePath

$downloadIsValid = $false
if (Test-Path -LiteralPath $packagePath -PathType Leaf) {
    $downloadHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    $downloadIsValid = $downloadHash -eq $knownPackages[$Version]
}

if (-not $downloadIsValid) {
    $temporaryPackagePath = "$packagePath.download"
    Assert-PathInsideToolRoot -Path $temporaryPackagePath
    Invoke-WebRequest -Uri $packageUri -OutFile $temporaryPackagePath

    $downloadHash = (Get-FileHash `
        -LiteralPath $temporaryPackagePath `
        -Algorithm SHA256).Hash
    if ($downloadHash -ne $knownPackages[$Version]) {
        Remove-Item -LiteralPath $temporaryPackagePath -Force
        throw "The Windows SDK Build Tools package hash did not match the pinned value."
    }

    Move-Item `
        -LiteralPath $temporaryPackagePath `
        -Destination $packagePath `
        -Force
}

$stagingRoot = Join-Path $toolRoot "package-staging-$([System.Guid]::NewGuid().ToString('N'))"
Assert-PathInsideToolRoot -Path $stagingRoot
New-Item -ItemType Directory -Path $stagingRoot | Out-Null

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $stagingRoot)
    $signToolPath = Resolve-VerifiedSignTool -Root $stagingRoot
    Assert-PathInsideToolRoot -Path $packageRoot
    Move-Item -LiteralPath $stagingRoot -Destination $packageRoot
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Assert-PathInsideToolRoot -Path $stagingRoot
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

$installedSignTool = Resolve-VerifiedSignTool -Root $packageRoot
Write-Host "Windows signing tools installed successfully."
Write-Host "SignTool: $installedSignTool"
