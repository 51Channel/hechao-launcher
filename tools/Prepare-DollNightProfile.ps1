[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceProfileRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidatePattern("^[0-9A-Fa-f]{64}$")]
    [string] $ExpectedSourceTreeSha256 =
        "3DF97F82AC00DF45A4EC392C1896C9B3D97CF5FA5185FB9F5B3366A01AB63D53"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ProfileTreeSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $files = @(
        Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
            Sort-Object {
                $_.FullName.Substring($Root.Length + 1).Replace("\", "/")
            }
    )
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $files) {
            $relativePath = $file.FullName.Substring($Root.Length + 1).Replace("\", "/")
            $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).
                Hash.ToLowerInvariant()
            $line = "$relativePath`0$($file.Length)`0$fileHash`n"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($line)
            $null = $sha256.TransformBlock(
                $bytes,
                0,
                $bytes.Length,
                $bytes,
                0)
        }

        $null = $sha256.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return [System.BitConverter]::ToString($sha256.Hash).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

$sourceRoot = (Resolve-Path -LiteralPath $SourceProfileRoot).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}

$metadataPath = Join-Path $sourceRoot "hechao-profile.json"
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "The approved base profile metadata is missing."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
if ($metadata.schemaVersion -ne 1 -or
    $metadata.versionId -ne "1.21.11-Fabric 0.19.2" -or
    $metadata.javaMajorVersion -ne 21) {
    throw "The source is not the approved base 1.21.11 profile."
}

$forbiddenSourcePaths = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force |
        ForEach-Object {
            $_.FullName.Substring($sourceRoot.Length + 1).Replace("\", "/")
        } |
        Where-Object {
            $_ -match "(^|/)(logs|saves|screenshots|crash-reports|debug|downloads|PCL|natives|runtime)(/|$)" -or
            $_ -match "(^|/)(servers\.dat|servers\.dat_old|usercache\.json|usernamecache\.json|command_history\.txt|launcher_profiles\.json|PCL\.ini)$"
        }
)
if ($forbiddenSourcePaths.Count -ne 0) {
    throw "The source profile contains runtime or player data."
}

$sourceTreeDigest = Get-ProfileTreeSha256 $sourceRoot
if (-not [string]::Equals(
        $sourceTreeDigest,
        $ExpectedSourceTreeSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The approved base profile tree has changed."
}

$outputParent = Split-Path -Parent $outputPath
$outputName = Split-Path -Leaf $outputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$stagingPath = Join-Path $outputParent ".$outputName.staging-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $stagingPath | Out-Null
    Get-ChildItem -LiteralPath $sourceRoot -Force |
        Copy-Item -Destination $stagingPath -Recurse

    $copiedTreeDigest = Get-ProfileTreeSha256 $stagingPath
    if (-not [string]::Equals(
            $copiedTreeDigest,
            $sourceTreeDigest,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The DollNight source copy failed tree validation."
    }

    Move-Item -LiteralPath $stagingPath -Destination $outputPath
    $outputFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Recurse -Force)
    $outputBytes = ($outputFiles | Measure-Object -Property Length -Sum).Sum
    Write-Output "Prepared DollNight profile: $outputPath"
    Write-Output "Files: $($outputFiles.Count)"
    Write-Output "Bytes: $outputBytes"
    Write-Output "Source tree SHA-256: $sourceTreeDigest"
}
catch {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    throw
}
