#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MirrorRoot,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-Sha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-ContainedPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Mirror checksum path must be relative."
    }

    $normalizedRelativePath = $RelativePath -replace
        "/",
        [IO.Path]::DirectorySeparatorChar
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $Root $normalizedRelativePath)
    )
    $rootPrefix = $Root.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith(
        $rootPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Mirror checksum path escapes the mirror root: $RelativePath"
    }

    return $candidate
}

$root = (Resolve-Path -LiteralPath $MirrorRoot).Path.TrimEnd("\", "/")
$inventoryPath = Join-Path $root "inventory.json"
$sumsPath = Join-Path $root "SHA256SUMS"
if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
    throw "Mirror inventory is missing."
}
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) {
    throw "Mirror checksum list is missing."
}

$inventory = Get-Content -Raw -LiteralPath $inventoryPath -Encoding utf8 |
    ConvertFrom-Json
if ($inventory.schemaVersion -ne 1 -or -not [bool]$inventory.fullyVerified) {
    throw "Mirror inventory is not a fully verified schema version 1 inventory."
}

$entries = [Collections.Generic.List[object]]::new()
$seenPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($line in Get-Content -LiteralPath $sumsPath -Encoding utf8) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -notmatch "^([0-9a-f]{64})  (.+)$") {
        throw "Invalid SHA256SUMS line."
    }

    $relativePath = $Matches[2]
    if (-not $seenPaths.Add($relativePath)) {
        throw "Duplicate SHA256SUMS path: $relativePath"
    }
    $entries.Add([ordered]@{
        sha256 = $Matches[1]
        path = $relativePath
        fullPath = Resolve-ContainedPath -Root $root -RelativePath $relativePath
    })
}

$expectedEntryCount = [int64]$inventory.uniqueObjectCount +
    [int64]$inventory.profileCount
if ($entries.Count -ne $expectedEntryCount) {
    throw (
        "Checksum entry count {0} does not match inventory count {1}." -f
            $entries.Count,
            $expectedEntryCount
    )
}

[long]$validatedBytes = 0
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.fullPath -PathType Leaf)) {
        throw "Mirror file is missing: $($entry.path)"
    }
    $actualSha256 = Get-Sha256 -Path $entry.fullPath
    if ($actualSha256 -ne $entry.sha256) {
        throw "Mirror file failed SHA-256 validation: $($entry.path)"
    }
    $validatedBytes += (Get-Item -LiteralPath $entry.fullPath).Length
}

$actualObjects = @(
    Get-ChildItem -LiteralPath (Join-Path $root "objects") -File -Recurse
)
$actualManifests = @(
    Get-ChildItem -LiteralPath (Join-Path $root "manifests") -File -Recurse
)
if ($actualObjects.Count -ne [int64]$inventory.uniqueObjectCount) {
    throw (
        "Mirror object count {0} does not match inventory count {1}." -f
            $actualObjects.Count,
            $inventory.uniqueObjectCount
    )
}
if ($actualManifests.Count -ne [int64]$inventory.profileCount) {
    throw (
        "Mirror manifest count {0} does not match inventory count {1}." -f
            $actualManifests.Count,
            $inventory.profileCount
    )
}

[long]$actualObjectBytes = (
    $actualObjects |
        Measure-Object -Property Length -Sum
).Sum
if ($actualObjectBytes -ne [int64]$inventory.uniqueObjectBytes) {
    throw (
        "Mirror object bytes {0} do not match inventory bytes {1}." -f
            $actualObjectBytes,
            $inventory.uniqueObjectBytes
    )
}

$result = [ordered]@{
    status = "valid"
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    profileCount = [int64]$inventory.profileCount
    uniqueObjectCount = [int64]$inventory.uniqueObjectCount
    uniqueObjectBytes = [int64]$inventory.uniqueObjectBytes
    validatedFileCount = $entries.Count
    validatedBytes = $validatedBytes
    objectSetSha256 = [string]$inventory.objectSetSha256
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4 -Compress
} else {
    [pscustomobject]$result
}
