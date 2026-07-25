[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourcePackRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string] $VersionId = "fabric-loader-0.16.14-1.20.1",

    [ValidatePattern("^[0-9A-Fa-f]{64}$")]
    [string] $ExpectedPackManifestSha256 =
        "5415D5C56D2BB83416EAAD1CD6FD5CDA755A9983162D6C64716DA785F2418D57"
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

    $destinationParent = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }

    Copy-Item -LiteralPath $Source -Destination $Destination
}

$sourceRoot = Resolve-ExistingPath $SourcePackRoot "Source pack root"
$minecraftRoot = Resolve-ExistingPath (Join-Path $sourceRoot ".minecraft") "Source Minecraft root"
$packManifestPath = Resolve-ExistingPath (
    Join-Path $sourceRoot "PACK-MANIFEST.json") "Pack manifest"
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}

$packManifestDigest = (Get-FileHash -LiteralPath $packManifestPath -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $packManifestDigest,
        $ExpectedPackManifestSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PACK-MANIFEST.json does not match the approved PVP client source."
}

$packManifest = Get-Content -LiteralPath $packManifestPath -Raw | ConvertFrom-Json
if ($packManifest.minecraft -ne "1.20.1" -or
    $packManifest.fabric_loader -ne "0.16.14" -or
    $packManifest.pack_version -ne "1.0.0" -or
    $packManifest.server -ne "owl9.vipi9.top:19243" -or
    $packManifest.mod_count -ne 14 -or
    $packManifest.mods.Count -ne 14) {
    throw "The source pack manifest is not the approved Fabric 1.20.1 PVP client."
}

$parentVersionId = "1.20.1"
$parentVersionRoot = Resolve-ExistingPath (
    Join-Path $minecraftRoot "versions\$parentVersionId") "Parent version directory"
$sourceVersionRoot = Resolve-ExistingPath (
    Join-Path $minecraftRoot "versions\$VersionId") "Fabric version directory"
$parentJsonPath = Join-Path $parentVersionRoot "$parentVersionId.json"
$parentJarPath = Join-Path $parentVersionRoot "$parentVersionId.jar"
$versionJsonPath = Join-Path $sourceVersionRoot "$VersionId.json"
$versionJarPath = Join-Path $sourceVersionRoot "$VersionId.jar"

$parentJson = Get-Content -LiteralPath $parentJsonPath -Raw | ConvertFrom-Json
$versionJson = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
if ($parentJson.id -ne $parentVersionId -or
    $parentJson.mainClass -ne "net.minecraft.client.main.Main" -or
    $parentJson.javaVersion.majorVersion -ne 17 -or
    $versionJson.id -ne $VersionId -or
    $versionJson.inheritsFrom -ne $parentVersionId -or
    $versionJson.mainClass -ne "net.fabricmc.loader.impl.launch.knot.KnotClient") {
    throw "The source version metadata is not the approved Fabric 1.20.1 layout."
}

$fabricLoaderLibrary = @(
    $versionJson.libraries |
        Where-Object { $_.name -eq "net.fabricmc:fabric-loader:0.16.14" }
)
if ($fabricLoaderLibrary.Count -ne 1) {
    throw "The Fabric child version does not reference loader 0.16.14."
}

$modsRoot = Resolve-ExistingPath (Join-Path $sourceVersionRoot "mods") "Mods directory"
$sourceMods = @(Get-ChildItem -LiteralPath $modsRoot -File)
$approvedModNames = @($packManifest.mods | ForEach-Object { [string] $_.name })
if ($sourceMods.Count -ne $approvedModNames.Count -or
    @($sourceMods | Where-Object { $_.Name -notin $approvedModNames }).Count -ne 0) {
    throw "The source mods directory does not exactly match PACK-MANIFEST.json."
}

foreach ($mod in $packManifest.mods) {
    $modPath = Join-Path $modsRoot $mod.name
    $file = Get-Item -LiteralPath $modPath
    $digest = (Get-FileHash -LiteralPath $modPath -Algorithm SHA256).Hash
    if ($file.Length -ne [long] $mod.size -or
        -not [string]::Equals(
            $digest,
            [string] $mod.sha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Approved mod validation failed: $($mod.name)"
    }
}

$outputParent = Split-Path -Parent $outputPath
$outputName = Split-Path -Leaf $outputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$stagingPath = Join-Path $outputParent ".$outputName.staging-$([Guid]::NewGuid().ToString('N'))"

$configFiles = @(
    "cave_dweller.properties",
    "craftedcore.json5",
    "fabric\indigo-renderer.properties",
    "ferritecore.mixin.properties",
    "immersive_portals.json",
    "man\man.json",
    "man\mod_version.json",
    "man_config.toml",
    "midnightlurkerconfig.json",
    "remorphed.json5",
    "sodium-mixins.properties",
    "sodium-options.json",
    "walkers.json5"
)

try {
    New-Item -ItemType Directory -Path $stagingPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $minecraftRoot "assets") -Destination $stagingPath -Recurse
    Copy-Item -LiteralPath (Join-Path $minecraftRoot "libraries") -Destination $stagingPath -Recurse

    $targetParentRoot = Join-Path $stagingPath "versions\$parentVersionId"
    New-Item -ItemType Directory -Path $targetParentRoot -Force | Out-Null
    Copy-RequiredFile $parentJsonPath (Join-Path $targetParentRoot "$parentVersionId.json")
    Copy-RequiredFile $parentJarPath (Join-Path $targetParentRoot "$parentVersionId.jar")

    $targetVersionRoot = Join-Path $stagingPath "versions\$VersionId"
    New-Item -ItemType Directory -Path $targetVersionRoot -Force | Out-Null
    Copy-RequiredFile $versionJarPath (Join-Path $targetVersionRoot "$VersionId.jar")
    if ($versionJson.PSObject.Properties.Name -contains "javaVersion") {
        $versionJson.javaVersion = [pscustomobject]@{
            component = "java-runtime-gamma"
            majorVersion = 17
        }
    }
    else {
        $versionJson | Add-Member -NotePropertyName "javaVersion" -NotePropertyValue (
            [pscustomobject]@{
                component = "java-runtime-gamma"
                majorVersion = 17
            })
    }

    $normalizedVersionJson = $versionJson | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText(
        (Join-Path $targetVersionRoot "$VersionId.json"),
        $normalizedVersionJson + "`n",
        [System.Text.UTF8Encoding]::new($false))

    $targetModsRoot = Join-Path $stagingPath "mods"
    New-Item -ItemType Directory -Path $targetModsRoot | Out-Null
    foreach ($mod in $packManifest.mods) {
        Copy-RequiredFile (
            Join-Path $modsRoot $mod.name) (
            Join-Path $targetModsRoot $mod.name)
    }

    Copy-RequiredFile (
        Join-Path $sourceVersionRoot "options.txt") (
        Join-Path $stagingPath "options.txt")

    $sourceConfigRoot = Join-Path $sourceVersionRoot "config"
    foreach ($configFile in $configFiles) {
        Copy-RequiredFile (
            Join-Path $sourceConfigRoot $configFile) (
            Join-Path $stagingPath "config\$configFile")
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

    $preparedMods = @(Get-ChildItem -LiteralPath $targetModsRoot -File)
    if ($preparedMods.Count -ne 14) {
        throw "The prepared PVP profile must contain exactly 14 approved mods."
    }

    $forbiddenPaths = @(
        Get-ChildItem -LiteralPath $stagingPath -File -Recurse -Force |
            ForEach-Object {
                $_.FullName.Substring($stagingPath.Length + 1).Replace("\", "/")
            } |
            Where-Object {
                $_ -match "(^|/)(logs|saves|screenshots|crash-reports|debug|downloads|PCL|natives|runtime)(/|$)" -or
                $_ -match "(^|/)(servers\.dat|servers\.dat_old|usercache\.json|usernamecache\.json|command_history\.txt|launcher_profiles\.json|PCL\.ini|sodium-fingerprint\.json)$" -or
                $_ -match "(^|/)voicechat/(category-volumes|player-volumes|voicechat-client)\.properties$"
            }
    )
    if ($forbiddenPaths.Count -ne 0) {
        throw "The prepared profile contains forbidden runtime, account, or device data."
    }

    Move-Item -LiteralPath $stagingPath -Destination $outputPath

    $outputFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Recurse -Force)
    $outputBytes = ($outputFiles | Measure-Object -Property Length -Sum).Sum
    Write-Output "Prepared Fabric PVP profile: $outputPath"
    Write-Output "Files: $($outputFiles.Count)"
    Write-Output "Bytes: $outputBytes"
    Write-Output "Pack manifest SHA-256: $packManifestDigest"
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
