[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceMinecraftRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OfficialVersionJson,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string] $UpstreamCacheDirectory =
        "artifacts\upstream-cache\vanilla-1.21.11",

    [ValidateNotNullOrEmpty()]
    [string] $VersionId = "1.21.11",

    [ValidateNotNullOrEmpty()]
    [string] $SourceVersionId = "1.21.11-Fabric 0.19.2",

    [ValidatePattern("^[0-9A-Fa-f]{64}$")]
    [string] $ExpectedOfficialJsonSha256 =
        "24366A082714C66F445A0E64A6C434784EC7AD48DB429CDED2A2A266FA97F76C"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description does not exist: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Copy-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination,

        [AllowEmptyString()]
        [string] $ExpectedSha1 = ""
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required source file does not exist: $Source"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha1)) {
        $actualSha1 = (Get-FileHash -LiteralPath $Source -Algorithm SHA1).Hash
        if (-not [string]::Equals(
                $actualSha1,
                $ExpectedSha1,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "SHA-1 validation failed: $Source"
        }
    }

    $destinationParent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination
}

function Test-FileSha1 {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedSha1
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $actualSha1 = (Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash
    return [string]::Equals(
        $actualSha1,
        $ExpectedSha1,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-VerifiedUpstreamFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LocalPath,

        [Parameter(Mandatory = $true)]
        [string] $CachePath,

        [Parameter(Mandatory = $true)]
        [ValidatePattern("^https://")]
        [string] $Uri,

        [Parameter(Mandatory = $true)]
        [ValidatePattern("^[0-9A-Fa-f]{40}$")]
        [string] $ExpectedSha1
    )

    if (Test-FileSha1 $LocalPath $ExpectedSha1) {
        return $LocalPath
    }

    if (Test-FileSha1 $CachePath $ExpectedSha1) {
        return $CachePath
    }

    $cacheParent = Split-Path -Parent $CachePath
    New-Item -ItemType Directory -Path $cacheParent -Force | Out-Null
    $temporaryPath = "$CachePath.download-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $temporaryPath
        if (-not (Test-FileSha1 $temporaryPath $ExpectedSha1)) {
            throw "Official download SHA-1 validation failed: $Uri"
        }

        Move-Item -LiteralPath $temporaryPath -Destination $CachePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    return $CachePath
}

function Test-LibraryAllowedOnWindows {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Library
    )

    if ($Library.PSObject.Properties.Name -notcontains "rules") {
        return $true
    }

    $allowed = $false
    foreach ($rule in $Library.rules) {
        $matches = $true
        if ($rule.PSObject.Properties.Name -contains "os") {
            $matches = $rule.os.name -eq "windows"
        }

        if ($matches -and
            $rule.PSObject.Properties.Name -contains "features") {
            $matches = $false
        }

        if ($matches) {
            $allowed = $rule.action -eq "allow"
        }
    }

    return $allowed
}

$sourceRoot = Resolve-ExistingPath $SourceMinecraftRoot "Source Minecraft root"
$officialJsonPath = Resolve-ExistingPath $OfficialVersionJson "Official version JSON"
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$upstreamCachePath = [System.IO.Path]::GetFullPath($UpstreamCacheDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}

$officialJsonDigest = (Get-FileHash -LiteralPath $officialJsonPath -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $officialJsonDigest,
        $ExpectedOfficialJsonSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Official version JSON SHA-256 does not match the approved immutable metadata."
}

$versionJson = Get-Content -LiteralPath $officialJsonPath -Raw | ConvertFrom-Json
if ($versionJson.id -ne $VersionId -or
    $versionJson.mainClass -ne "net.minecraft.client.main.Main" -or
    $versionJson.javaVersion.majorVersion -ne 21 -or
    $versionJson.type -ne "release") {
    throw "The supplied JSON is not the approved Minecraft $VersionId release."
}

$sourceVersionRoot = Resolve-ExistingPath (
    Join-Path $sourceRoot "versions\$SourceVersionId") "Source version directory"
$sourceClientJar = Join-Path $sourceVersionRoot "$SourceVersionId.jar"
$outputParent = Split-Path -Parent $outputPath
$outputName = Split-Path -Leaf $outputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$stagingPath = Join-Path $outputParent ".$outputName.staging-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $stagingPath | Out-Null

    $targetVersionRoot = Join-Path $stagingPath "versions\$VersionId"
    Copy-VerifiedFile `
        -Source $officialJsonPath `
        -Destination (Join-Path $targetVersionRoot "$VersionId.json")
    $verifiedClientJar = Get-VerifiedUpstreamFile `
        -LocalPath $sourceClientJar `
        -CachePath (Join-Path $upstreamCachePath "versions\$VersionId\client.jar") `
        -Uri ([string] $versionJson.downloads.client.url) `
        -ExpectedSha1 ([string] $versionJson.downloads.client.sha1)
    Copy-VerifiedFile `
        -Source $verifiedClientJar `
        -Destination (Join-Path $targetVersionRoot "$VersionId.jar") `
        -ExpectedSha1 ([string] $versionJson.downloads.client.sha1)

    $assetIndexId = [string] $versionJson.assetIndex.id
    $sourceAssetIndex = Join-Path $sourceRoot "assets\indexes\$assetIndexId.json"
    $verifiedAssetIndex = Get-VerifiedUpstreamFile `
        -LocalPath $sourceAssetIndex `
        -CachePath (Join-Path $upstreamCachePath "assets\indexes\$assetIndexId.json") `
        -Uri ([string] $versionJson.assetIndex.url) `
        -ExpectedSha1 ([string] $versionJson.assetIndex.sha1)
    $targetAssetIndex = Join-Path $stagingPath "assets\indexes\$assetIndexId.json"
    Copy-VerifiedFile `
        -Source $verifiedAssetIndex `
        -Destination $targetAssetIndex `
        -ExpectedSha1 ([string] $versionJson.assetIndex.sha1)
    $assetIndex = Get-Content -LiteralPath $verifiedAssetIndex -Raw | ConvertFrom-Json
    $assetHashes = @(
        $assetIndex.objects.PSObject.Properties |
            ForEach-Object { [string] $_.Value.hash } |
            Sort-Object -Unique
    )
    foreach ($assetHash in $assetHashes) {
        if ($assetHash -notmatch "^[0-9a-f]{40}$") {
            throw "The official asset index contains an invalid digest."
        }

        $relativeAssetPath = "$($assetHash.Substring(0, 2))\$assetHash"
        $verifiedAsset = Get-VerifiedUpstreamFile `
            -LocalPath (Join-Path $sourceRoot "assets\objects\$relativeAssetPath") `
            -CachePath (Join-Path $upstreamCachePath "assets\objects\$relativeAssetPath") `
            -Uri "https://resources.download.minecraft.net/$($assetHash.Substring(0, 2))/$assetHash" `
            -ExpectedSha1 $assetHash
        Copy-VerifiedFile `
            -Source $verifiedAsset `
            -Destination (Join-Path $stagingPath "assets\objects\$relativeAssetPath") `
            -ExpectedSha1 $assetHash
    }

    $libraryDownloads = @{}
    foreach ($library in @(
            $versionJson.libraries |
                Where-Object { Test-LibraryAllowedOnWindows $_ })) {
        if ($library.downloads.PSObject.Properties.Name -contains "artifact") {
            $artifact = $library.downloads.artifact
            $libraryDownloads[[string] $artifact.path] = [pscustomobject] @{
                Sha1 = [string] $artifact.sha1
                Uri = [string] $artifact.url
            }
        }

        if ($library.downloads.PSObject.Properties.Name -contains "classifiers") {
            foreach ($classifier in $library.downloads.classifiers.PSObject.Properties) {
                $artifact = $classifier.Value
                $libraryDownloads[[string] $artifact.path] = [pscustomobject] @{
                    Sha1 = [string] $artifact.sha1
                    Uri = [string] $artifact.url
                }
            }
        }
    }

    foreach ($relativeLibraryPath in ($libraryDownloads.Keys | Sort-Object)) {
        $libraryDownload = $libraryDownloads[$relativeLibraryPath]
        $verifiedLibrary = Get-VerifiedUpstreamFile `
            -LocalPath (Join-Path $sourceRoot "libraries\$relativeLibraryPath") `
            -CachePath (Join-Path $upstreamCachePath "libraries\$relativeLibraryPath") `
            -Uri $libraryDownload.Uri `
            -ExpectedSha1 $libraryDownload.Sha1
        Copy-VerifiedFile `
            -Source $verifiedLibrary `
            -Destination (Join-Path $stagingPath "libraries\$relativeLibraryPath") `
            -ExpectedSha1 $libraryDownload.Sha1
    }

    $sourceOptions = Join-Path $sourceVersionRoot "options.txt"
    if (Test-Path -LiteralPath $sourceOptions -PathType Leaf) {
        Copy-VerifiedFile `
            -Source $sourceOptions `
            -Destination (Join-Path $stagingPath "options.txt")
    }

    $metadata = @(
        "{",
        "  `"schemaVersion`": 1,",
        "  `"versionId`": `"$VersionId`",",
        "  `"javaMajorVersion`": 21",
        "}"
    ) -join "`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingPath "hechao-profile.json"),
        $metadata + "`n",
        [System.Text.UTF8Encoding]::new($false))

    $forbiddenPaths = @(
        Get-ChildItem -LiteralPath $stagingPath -File -Recurse -Force |
            ForEach-Object {
                $_.FullName.Substring($stagingPath.Length + 1).Replace("\", "/")
            } |
            Where-Object {
                $_ -match "(^|/)(mods|config|logs|saves|screenshots|crash-reports|debug|downloads|PCL|natives|runtime)(/|$)" -or
                $_ -match "(^|/)(servers\.dat|usercache\.json|usernamecache\.json|command_history\.txt|launcher_profiles\.json|PCL\.ini)$"
            }
    )
    if ($forbiddenPaths.Count -ne 0) {
        throw "The prepared Vanilla profile contains modded, runtime, or account data."
    }

    Move-Item -LiteralPath $stagingPath -Destination $outputPath
    $outputFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Recurse -Force)
    $outputBytes = ($outputFiles | Measure-Object -Property Length -Sum).Sum
    Write-Output "Prepared Vanilla profile: $outputPath"
    Write-Output "Files: $($outputFiles.Count)"
    Write-Output "Bytes: $outputBytes"
    Write-Output "Official JSON SHA-256: $officialJsonDigest"
}
catch {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    throw
}
