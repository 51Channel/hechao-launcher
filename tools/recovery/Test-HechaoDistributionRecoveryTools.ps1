#requires -Version 7.4

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$toolRoot = Split-Path -Parent $PSCommandPath
$createTool = Join-Path $toolRoot "New-HechaoDistributionObjectMirror.ps1"
$verifyTool = Join-Path $toolRoot "Test-HechaoDistributionObjectMirror.ps1"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "hechao-distribution-recovery-{0}" -f [Guid]::NewGuid().ToString("N")
)

function New-TestDistribution {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$ProfileId,

        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [byte[][]]$Objects
    )

    $distributionRoot = Join-Path $Root $Name
    $manifestRoot = Join-Path $distributionRoot "manifests"
    $objectRoot = Join-Path $distributionRoot "objects"
    [IO.Directory]::CreateDirectory($manifestRoot) | Out-Null
    [IO.Directory]::CreateDirectory($objectRoot) | Out-Null

    $files = [Collections.Generic.List[object]]::new()
    $index = 0
    foreach ($bytes in $Objects) {
        $sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)
        ).ToLowerInvariant()
        $objectPath = Join-Path $objectRoot (
            "{0}/{1}" -f $sha256.Substring(0, 2), $sha256
        )
        [IO.Directory]::CreateDirectory((Split-Path -Parent $objectPath)) |
            Out-Null
        [IO.File]::WriteAllBytes($objectPath, $bytes)
        $files.Add([ordered]@{
            path = "fixture/$index.bin"
            size = $bytes.Length
            sha256 = $sha256
            url = "https://example.invalid/objects/$sha256"
            required = $true
        })
        $index++
    }

    $payload = [ordered]@{
        schemaVersion = 1
        profileId = $ProfileId
        version = $Version
        minecraftVersion = "test"
        javaVersion = "21"
        loader = "Vanilla"
        loaderVersion = $null
        publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
        files = @($files)
    } | ConvertTo-Json -Depth 8 -Compress
    $envelope = [ordered]@{
        schemaVersion = 1
        algorithm = "TEST"
        keyId = "fixture"
        payloadBase64 = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes($payload)
        )
        signatureBase64 = ""
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText(
        (Join-Path $manifestRoot "$ProfileId.json"),
        $envelope + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

try {
    $distributionRoot = Join-Path $testRoot "distributions"
    $mirrorRoot = Join-Path $testRoot "mirror"
    [IO.Directory]::CreateDirectory($distributionRoot) | Out-Null

    $shared = [Text.Encoding]::UTF8.GetBytes("shared-object")
    New-TestDistribution `
        -Root $distributionRoot `
        -Name "profile-a-1.0.0" `
        -ProfileId "profile-a" `
        -Version "1.0.0" `
        -Objects @(
            $shared,
            [Text.Encoding]::UTF8.GetBytes("profile-a-only")
        )
    New-TestDistribution `
        -Root $distributionRoot `
        -Name "profile-b-2.0.0" `
        -ProfileId "profile-b" `
        -Version "2.0.0" `
        -Objects @(
            $shared,
            [Text.Encoding]::UTF8.GetBytes("profile-b-only")
        )

    $created = & $createTool `
        -DistributionRoot $distributionRoot `
        -DestinationRoot $mirrorRoot `
        -DistributionNames @("profile-a-1.0.0", "profile-b-2.0.0") `
        -AsJson |
        ConvertFrom-Json
    if ($created.profileCount -ne 2 -or
        $created.objectReferenceCount -ne 4 -or
        $created.uniqueObjectCount -ne 3) {
        throw "Mirror creation did not deduplicate the fixture object set."
    }

    $verified = & $verifyTool -MirrorRoot $mirrorRoot -AsJson |
        ConvertFrom-Json
    if ($verified.status -ne "valid" -or
        $verified.validatedFileCount -ne 5) {
        throw "Mirror verification did not validate the fixture."
    }

    $objectToCorrupt = Get-ChildItem `
        -LiteralPath (Join-Path $mirrorRoot "objects") `
        -File `
        -Recurse |
        Select-Object -First 1
    [IO.File]::WriteAllText(
        $objectToCorrupt.FullName,
        "corrupt",
        [Text.UTF8Encoding]::new($false)
    )

    $corruptionRejected = $false
    try {
        & $verifyTool -MirrorRoot $mirrorRoot -AsJson | Out-Null
    } catch {
        $corruptionRejected = $true
    }
    if (-not $corruptionRejected) {
        throw "Mirror verification accepted a corrupted object."
    }

    & $createTool `
        -DistributionRoot $distributionRoot `
        -DestinationRoot $mirrorRoot `
        -DistributionNames @("profile-a-1.0.0", "profile-b-2.0.0") `
        -AsJson | Out-Null
    & $verifyTool -MirrorRoot $mirrorRoot -AsJson | Out-Null

    $sourceObjectToCorrupt = Get-ChildItem `
        -LiteralPath (
            Join-Path $distributionRoot "profile-a-1.0.0\objects"
        ) `
        -File `
        -Recurse |
        Select-Object -First 1
    [IO.File]::WriteAllText(
        $sourceObjectToCorrupt.FullName,
        "corrupt-source",
        [Text.UTF8Encoding]::new($false)
    )

    $sourceCorruptionRejected = $false
    try {
        & $createTool `
            -DistributionRoot $distributionRoot `
            -DestinationRoot $mirrorRoot `
            -DistributionNames @("profile-a-1.0.0", "profile-b-2.0.0") `
            -AsJson | Out-Null
    } catch {
        $sourceCorruptionRejected = $true
    }
    if (-not $sourceCorruptionRejected) {
        throw "Mirror creation accepted a corrupted source object."
    }

    New-TestDistribution `
        -Root $distributionRoot `
        -Name "profile-traversal-1.0.0" `
        -ProfileId ".." `
        -Version "1.0.0" `
        -Objects @(
            [Text.Encoding]::UTF8.GetBytes("path-traversal")
        )
    $pathTraversalRejected = $false
    try {
        & $createTool `
            -DistributionRoot $distributionRoot `
            -DestinationRoot $mirrorRoot `
            -DistributionNames @("profile-traversal-1.0.0") `
            -AsJson | Out-Null
    } catch {
        $pathTraversalRejected = $true
    }
    if (-not $pathTraversalRejected) {
        throw "Mirror creation accepted a traversing profile identity."
    }

    $overlappingRootsRejected = $false
    try {
        & $createTool `
            -DistributionRoot $distributionRoot `
            -DestinationRoot (Join-Path $distributionRoot "unsafe-mirror") `
            -DistributionNames @("profile-b-2.0.0") `
            -AsJson | Out-Null
    } catch {
        $overlappingRootsRejected = $true
    }
    if (-not $overlappingRootsRejected) {
        throw "Mirror creation accepted overlapping source and destination roots."
    }

    $preserved = & $verifyTool -MirrorRoot $mirrorRoot -AsJson |
        ConvertFrom-Json
    if ($preserved.status -ne "valid") {
        throw "A failed replacement did not preserve the current mirror."
    }

    [pscustomobject]@{
        status = "passed"
        scenarios = 7
        deduplicatedObjects = 3
        corruptionRejected = $true
        sourceCorruptionRejected = $true
        pathTraversalRejected = $true
        overlappingRootsRejected = $true
        failedReplacementPreservedCurrent = $true
    }
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()
        ).TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTestRoot.StartsWith(
            $resolvedTempRoot,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to remove a test directory outside the temp root."
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
