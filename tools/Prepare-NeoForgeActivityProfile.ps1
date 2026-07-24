[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceMinecraftRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $ServerMecchaJar,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string] $VersionId = "1.21.11-NeoForge_21.11.42",

    [ValidatePattern("^[0-9A-Fa-f]{64}$")]
    [string] $ExpectedMecchaSha256 =
        "C72511BEF3B0CC2C1A1C97E1C33709901714460191F9549FD461E71215534E9E"
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

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required source file does not exist: $Source"
    }

    Copy-Item -LiteralPath $Source -Destination $Destination
}

$sourceRoot = Resolve-ExistingPath $SourceMinecraftRoot "Source Minecraft root"
$mecchaJar = Resolve-ExistingPath $ServerMecchaJar "Server-matched Meccha JAR"
$sourceVersionRoot = Join-Path $sourceRoot "versions\$VersionId"
$sourceVersionRoot = Resolve-ExistingPath $sourceVersionRoot "NeoForge version directory"
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}

$mecchaDigest = (Get-FileHash -LiteralPath $mecchaJar -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $mecchaDigest,
        $ExpectedMecchaSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Meccha JAR SHA-256 does not match the approved server build."
}

$versionJsonPath = Join-Path $sourceVersionRoot "$VersionId.json"
$versionJarPath = Join-Path $sourceVersionRoot "$VersionId.jar"
if (-not (Test-Path -LiteralPath $versionJsonPath -PathType Leaf)) {
    throw "Required version JSON does not exist: $versionJsonPath"
}

$versionJson = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
if ($versionJson.id -ne $VersionId -or
    $versionJson.mainClass -ne "net.neoforged.fml.startup.Client" -or
    $versionJson.javaVersion.majorVersion -ne 21) {
    throw "The version JSON is not the approved standalone NeoForge 21 profile."
}

if (-not (Test-Path -LiteralPath $versionJarPath -PathType Leaf)) {
    throw "Required version JAR does not exist: $versionJarPath"
}

$outputParent = Split-Path -Parent $outputPath
$outputName = Split-Path -Leaf $outputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$stagingPath = Join-Path $outputParent ".$outputName.staging-$([Guid]::NewGuid().ToString('N'))"

$configFiles = @(
    "appleskin-client.toml",
    "lithium.properties",
    "MouseTweaks.cfg",
    "sodium-extra.properties",
    "sodium-extra-options.json",
    "sodium-mixins.properties",
    "sodium-options.json"
)

try {
    New-Item -ItemType Directory -Path $stagingPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot "assets") -Destination $stagingPath -Recurse
    Copy-Item -LiteralPath (Join-Path $sourceRoot "libraries") -Destination $stagingPath -Recurse

    $targetVersionRoot = Join-Path $stagingPath "versions\$VersionId"
    New-Item -ItemType Directory -Path $targetVersionRoot -Force | Out-Null
    Copy-RequiredFile $versionJsonPath $targetVersionRoot
    Copy-RequiredFile $versionJarPath $targetVersionRoot

    $targetModsRoot = Join-Path $stagingPath "mods"
    New-Item -ItemType Directory -Path $targetModsRoot | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourceVersionRoot "mods") -File |
        Where-Object { $_.Name -notlike "meccha_chameleon*.jar" } |
        Copy-Item -Destination $targetModsRoot
    Copy-Item -LiteralPath $mecchaJar -Destination $targetModsRoot

    Copy-RequiredFile (Join-Path $sourceVersionRoot "options.txt") $stagingPath

    $sourceConfigRoot = Join-Path $sourceVersionRoot "config"
    $targetConfigRoot = Join-Path $stagingPath "config"
    New-Item -ItemType Directory -Path $targetConfigRoot | Out-Null
    foreach ($configFile in $configFiles) {
        Copy-RequiredFile (Join-Path $sourceConfigRoot $configFile) $targetConfigRoot
    }

    $soundPhysicsConfig = Join-Path $sourceConfigRoot "sound_physics_remastered"
    if (Test-Path -LiteralPath $soundPhysicsConfig -PathType Container) {
        Copy-Item -LiteralPath $soundPhysicsConfig -Destination $targetConfigRoot -Recurse
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

    $mecchaFiles = @(
        Get-ChildItem -LiteralPath $targetModsRoot -File -Filter "meccha_chameleon*.jar"
    )
    if ($mecchaFiles.Count -ne 1) {
        throw "The prepared profile must contain exactly one Meccha JAR."
    }

    $forbiddenPaths = @(
        Get-ChildItem -LiteralPath $stagingPath -File -Recurse -Force |
            ForEach-Object {
                $_.FullName.Substring($stagingPath.Length + 1).Replace("\", "/")
            } |
            Where-Object {
                $_ -match "(^|/)(logs|saves|screenshots|crash-reports|debug|downloads|PCL|natives)(/|$)" -or
                $_ -match "(^|/)(servers\.dat|usercache\.json|usernamecache\.json|command_history\.txt|launcher_profiles\.json|PCL\.ini)$"
            }
    )
    if ($forbiddenPaths.Count -ne 0) {
        throw "The prepared profile contains forbidden runtime or account data."
    }

    Move-Item -LiteralPath $stagingPath -Destination $outputPath

    $outputFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Recurse -Force)
    $outputBytes = ($outputFiles | Measure-Object -Property Length -Sum).Sum
    Write-Output "Prepared NeoForge profile: $outputPath"
    Write-Output "Files: $($outputFiles.Count)"
    Write-Output "Bytes: $outputBytes"
    Write-Output "Meccha SHA-256: $mecchaDigest"
}
catch {
    if (Test-Path -LiteralPath $stagingPath) {
        $resolvedStaging = (Resolve-Path -LiteralPath $stagingPath).Path
        $parentPrefix = $outputParent.TrimEnd("\") + "\"
        if ($resolvedStaging.StartsWith(
                $parentPrefix,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }

    throw
}
