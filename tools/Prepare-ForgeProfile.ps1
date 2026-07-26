[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceMinecraftRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string] $VersionId = "1.20.1-Forge_47.4.0",

    [ValidatePattern("^[0-9A-Fa-f]{64}$")]
    [string] $ExpectedVersionJsonSha256 =
        "D47500CC8243B5BBECBFF61D64B510069A752C1FC6A5F73259507F3E568E539C"
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
$sourceVersionRoot = Resolve-ExistingPath (
    Join-Path $sourceRoot "versions\$VersionId") "Forge version directory"
$versionJsonPath = Join-Path $sourceVersionRoot "$VersionId.json"
$versionJarPath = Join-Path $sourceVersionRoot "$VersionId.jar"
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}

$versionJsonDigest = (Get-FileHash -LiteralPath $versionJsonPath -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $versionJsonDigest,
        $ExpectedVersionJsonSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Forge version JSON SHA-256 does not match the approved source."
}

$versionJson = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
if ($versionJson.id -ne $VersionId -or
    $versionJson.mainClass -ne "cpw.mods.bootstraplauncher.BootstrapLauncher" -or
    $versionJson.javaVersion.majorVersion -ne 17) {
    throw "The source version metadata is not the approved Forge 1.20.1 profile."
}

$forgeLoader = @(
    $versionJson.libraries |
        Where-Object { $_.name -eq "net.minecraftforge:fmlloader:1.20.1-47.4.0" }
)
$bootstrapLauncher = @(
    $versionJson.libraries |
        Where-Object { $_.name -eq "cpw.mods:bootstraplauncher:1.1.2" }
)
if ($forgeLoader.Count -ne 1 -or $bootstrapLauncher.Count -ne 1) {
    throw "The source version does not contain the approved Forge loader chain."
}

$outputParent = Split-Path -Parent $outputPath
$outputName = Split-Path -Leaf $outputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$stagingPath = Join-Path $outputParent ".$outputName.staging-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $stagingPath | Out-Null

    $targetVersionRoot = Join-Path $stagingPath "versions\$VersionId"
    Copy-VerifiedFile `
        -Source $versionJsonPath `
        -Destination (Join-Path $targetVersionRoot "$VersionId.json")
    Copy-VerifiedFile `
        -Source $versionJarPath `
        -Destination (Join-Path $targetVersionRoot "$VersionId.jar")

    $assetIndexId = [string] $versionJson.assetIndex.id
    $sourceAssetIndex = Join-Path $sourceRoot "assets\indexes\$assetIndexId.json"
    Copy-VerifiedFile `
        -Source $sourceAssetIndex `
        -Destination (Join-Path $stagingPath "assets\indexes\$assetIndexId.json") `
        -ExpectedSha1 ([string] $versionJson.assetIndex.sha1)
    $assetIndex = Get-Content -LiteralPath $sourceAssetIndex -Raw | ConvertFrom-Json
    $assetHashes = @(
        $assetIndex.objects.PSObject.Properties |
            ForEach-Object { [string] $_.Value.hash } |
            Sort-Object -Unique
    )
    foreach ($assetHash in $assetHashes) {
        if ($assetHash -notmatch "^[0-9a-f]{40}$") {
            throw "The Forge asset index contains an invalid digest."
        }

        $relativeAssetPath = "$($assetHash.Substring(0, 2))\$assetHash"
        Copy-VerifiedFile `
            -Source (Join-Path $sourceRoot "assets\objects\$relativeAssetPath") `
            -Destination (Join-Path $stagingPath "assets\objects\$relativeAssetPath") `
            -ExpectedSha1 $assetHash
    }

    $libraryDownloads = @{}
    foreach ($library in @(
            $versionJson.libraries |
                Where-Object { Test-LibraryAllowedOnWindows $_ })) {
        if ($library.downloads.PSObject.Properties.Name -contains "artifact") {
            $artifact = $library.downloads.artifact
            $libraryDownloads[[string] $artifact.path] = [string] $artifact.sha1
        }

        if ($library.downloads.PSObject.Properties.Name -contains "classifiers") {
            foreach ($classifier in $library.downloads.classifiers.PSObject.Properties) {
                $artifact = $classifier.Value
                $libraryDownloads[[string] $artifact.path] = [string] $artifact.sha1
            }
        }
    }

    foreach ($relativeLibraryPath in ($libraryDownloads.Keys | Sort-Object)) {
        Copy-VerifiedFile `
            -Source (Join-Path $sourceRoot "libraries\$relativeLibraryPath") `
            -Destination (Join-Path $stagingPath "libraries\$relativeLibraryPath") `
            -ExpectedSha1 $libraryDownloads[$relativeLibraryPath]
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
        "  `"javaMajorVersion`": 17",
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
                $_ -match "^(mods|config|logs|saves|screenshots|crash-reports|debug|downloads|PCL|natives|runtime|replay_recordings|Distant_Horizons_server_data|tacz)(/|$)" -or
                $_ -match "^(servers\.dat|usercache\.json|usernamecache\.json|command_history\.txt|launcher_profiles\.json|PCL\.ini)$"
            }
    )
    if ($forbiddenPaths.Count -ne 0) {
        throw "The prepared Forge baseline contains mods, runtime, or player data."
    }

    Move-Item -LiteralPath $stagingPath -Destination $outputPath
    $outputFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Recurse -Force)
    $outputBytes = ($outputFiles | Measure-Object -Property Length -Sum).Sum
    Write-Output "Prepared Forge baseline: $outputPath"
    Write-Output "Files: $($outputFiles.Count)"
    Write-Output "Bytes: $outputBytes"
    Write-Output "Version JSON SHA-256: $versionJsonDigest"
}
catch {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    throw
}
