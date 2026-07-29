#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DistributionRoot,

    [Parameter(Mandatory)]
    [string]$DestinationRoot,

    [string[]]$DistributionNames = @(
        "base-1.21.11-1.0.5",
        "activity-neoforge-1.21.11-1.0.10",
        "pvp-fabric-1.20.1-1.0.0",
        "vanilla-1.21.11-1.0.0",
        "forge-1.20.1-1.0.0",
        "dollnight-1.21.11-1.0.0"
    ),

    [switch]$UseHardLinks,

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

function Get-TextSha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)
        ).ToLowerInvariant()
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Write-Utf8Json {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

function Assert-SafeDestination {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        $fullPath.TrimEnd("\", "/"),
        $root.TrimEnd("\", "/"),
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "DestinationRoot cannot be a drive root."
    }

    return $fullPath.TrimEnd("\", "/")
}

function Assert-SafePathSegment {
    param(
        [Parameter(Mandatory)]
        [string]$Value,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value.Length -gt 128 -or
        $Value -in @(".", "..") -or
        $Value -notmatch "^[A-Za-z0-9][A-Za-z0-9._+-]*$") {
        throw "$Name is not a safe path segment: $Value"
    }
}

function Test-IsSameOrChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Parent,

        [Parameter(Mandatory)]
        [string]$Candidate
    )

    $normalizedParent = [IO.Path]::GetFullPath($Parent).TrimEnd("\", "/")
    $normalizedCandidate = [IO.Path]::GetFullPath($Candidate).TrimEnd("\", "/")
    if ([string]::Equals(
        $normalizedParent,
        $normalizedCandidate,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        return $true
    }

    $prefix = $normalizedParent + [IO.Path]::DirectorySeparatorChar
    return $normalizedCandidate.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase
    )
}

$sourceRoot = (Resolve-Path -LiteralPath $DistributionRoot).Path
$destination = Assert-SafeDestination -Path $DestinationRoot
if ((Test-IsSameOrChildPath -Parent $sourceRoot -Candidate $destination) -or
    (Test-IsSameOrChildPath -Parent $destination -Candidate $sourceRoot)) {
    throw "DistributionRoot and DestinationRoot must be disjoint."
}
$destinationParent = Split-Path -Parent $destination
if ([string]::IsNullOrWhiteSpace($destinationParent)) {
    throw "DestinationRoot must have a parent directory."
}

[IO.Directory]::CreateDirectory($destinationParent) | Out-Null

$stagingRoot = Join-Path $destinationParent (
    ".{0}.partial-{1}-{2}" -f
        (Split-Path -Leaf $destination),
        $PID,
        [Guid]::NewGuid().ToString("N")
)
$previousRoot = $null
$published = $false

try {
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $stagingRoot "objects")) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $stagingRoot "manifests")) | Out-Null

    $objectRecords = @{}
    $profileRecords = [Collections.Generic.List[object]]::new()
    [long]$referenceCount = 0
    [long]$referenceBytes = 0

    foreach ($distributionName in $DistributionNames) {
        Assert-SafePathSegment `
            -Value $distributionName `
            -Name "Distribution name"

        $distributionPath = Join-Path $sourceRoot $distributionName
        if (-not (Test-Path -LiteralPath $distributionPath -PathType Container)) {
            throw "Distribution does not exist: $distributionName"
        }

        $manifestFiles = @(
            Get-ChildItem `
                -LiteralPath (Join-Path $distributionPath "manifests") `
                -Filter "*.json" `
                -File
        )
        if ($manifestFiles.Count -ne 1) {
            throw (
                "Distribution '{0}' must contain exactly one signed manifest; found {1}." -f
                    $distributionName,
                    $manifestFiles.Count
            )
        }

        $manifestFile = $manifestFiles[0]
        $manifestBytes = [IO.File]::ReadAllBytes($manifestFile.FullName)
        try {
            $envelope = [Text.Encoding]::UTF8.GetString($manifestBytes) |
                ConvertFrom-Json
        } finally {
            [Array]::Clear($manifestBytes, 0, $manifestBytes.Length)
        }

        if ($envelope.schemaVersion -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$envelope.payloadBase64)) {
            throw "Manifest envelope is invalid: $($manifestFile.FullName)"
        }

        $payloadBytes = [Convert]::FromBase64String(
            [string]$envelope.payloadBase64
        )
        try {
            $payload = [Text.Encoding]::UTF8.GetString($payloadBytes) |
                ConvertFrom-Json
        } finally {
            [Array]::Clear($payloadBytes, 0, $payloadBytes.Length)
        }

        $profileId = [string]$payload.profileId
        $profileVersion = [string]$payload.version
        Assert-SafePathSegment -Value $profileId -Name "Manifest profile ID"
        Assert-SafePathSegment `
            -Value $profileVersion `
            -Name "Manifest profile version"

        $manifestRelativePath = (
            "manifests/{0}/{1}.json" -f $profileId, $profileVersion
        )
        $manifestDestination = Join-Path $stagingRoot (
            $manifestRelativePath -replace "/", [IO.Path]::DirectorySeparatorChar
        )
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $manifestDestination)
        ) | Out-Null
        Copy-Item -LiteralPath $manifestFile.FullName -Destination $manifestDestination

        [long]$profileBytes = 0
        [long]$profileReferences = 0
        foreach ($file in @($payload.files)) {
            $sha256 = ([string]$file.sha256).ToLowerInvariant()
            [long]$size = $file.size
            if ($sha256 -notmatch "^[0-9a-f]{64}$") {
                throw (
                    "Manifest '{0}' contains an invalid SHA-256 value." -f
                        $manifestFile.Name
                )
            }
            if ($size -lt 0) {
                throw (
                    "Manifest '{0}' contains a negative object length." -f
                        $manifestFile.Name
                )
            }

            $sourceObject = Join-Path $distributionPath (
                "objects/{0}/{1}" -f $sha256.Substring(0, 2), $sha256
            )
            if (-not (Test-Path -LiteralPath $sourceObject -PathType Leaf)) {
                throw (
                    "Object {0} is missing from distribution '{1}'." -f
                        $sha256,
                        $distributionName
                )
            }

            $sourceItem = Get-Item -LiteralPath $sourceObject
            if ($sourceItem.Length -ne $size) {
                throw (
                    "Object {0} has length {1}; expected {2}." -f
                        $sha256,
                        $sourceItem.Length,
                        $size
                )
            }

            if (-not $objectRecords.ContainsKey($sha256)) {
                $actualSha256 = Get-Sha256 -Path $sourceObject
                if ($actualSha256 -ne $sha256) {
                    throw (
                        "Object {0} failed SHA-256 validation." -f $sha256
                    )
                }

                $objectRelativePath = (
                    "objects/{0}/{1}" -f $sha256.Substring(0, 2), $sha256
                )
                $objectDestination = Join-Path $stagingRoot (
                    $objectRelativePath -replace
                        "/",
                        [IO.Path]::DirectorySeparatorChar
                )
                [IO.Directory]::CreateDirectory(
                    (Split-Path -Parent $objectDestination)
                ) | Out-Null

                if ($UseHardLinks) {
                    New-Item `
                        -ItemType HardLink `
                        -Path $objectDestination `
                        -Target $sourceItem.FullName | Out-Null
                } else {
                    Copy-Item `
                        -LiteralPath $sourceItem.FullName `
                        -Destination $objectDestination
                }

                $objectRecords[$sha256] = [ordered]@{
                    sha256 = $sha256
                    size = $size
                    path = $objectRelativePath
                }
            } elseif ([long]$objectRecords[$sha256].size -ne $size) {
                throw "Object $sha256 has conflicting lengths across manifests."
            }

            $profileReferences++
            $profileBytes += $size
            $referenceCount++
            $referenceBytes += $size
        }

        $profileRecords.Add([ordered]@{
            profileId = $profileId
            version = $profileVersion
            distribution = $distributionName
            manifestPath = $manifestRelativePath
            manifestSha256 = Get-Sha256 -Path $manifestDestination
            fileReferences = $profileReferences
            referencedBytes = $profileBytes
        })
    }

    $objects = @(
        $objectRecords.Values |
            Sort-Object -Property sha256
    )
    [long]$uniqueBytes = 0
    foreach ($object in $objects) {
        $uniqueBytes += [long]$object.size
    }

    $objectDigestInput = (
        $objects |
            ForEach-Object {
                "{0} {1}" -f $_.sha256, $_.size
            }
    ) -join "`n"
    if ($objectDigestInput.Length -gt 0) {
        $objectDigestInput += "`n"
    }

    $inventory = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        fullyVerified = $true
        profileCount = $profileRecords.Count
        objectReferenceCount = $referenceCount
        objectReferenceBytes = $referenceBytes
        uniqueObjectCount = $objects.Count
        uniqueObjectBytes = $uniqueBytes
        duplicateReferenceCount = $referenceCount - $objects.Count
        objectSetSha256 = Get-TextSha256 -Value $objectDigestInput
        profiles = @($profileRecords)
        objects = $objects
    }
    Write-Utf8Json `
        -Path (Join-Path $stagingRoot "inventory.json") `
        -Value $inventory

    $sumLines = [Collections.Generic.List[string]]::new()
    foreach ($profile in $profileRecords | Sort-Object manifestPath) {
        $sumLines.Add((
            "{0}  {1}" -f @(
                $profile.manifestSha256,
                $profile.manifestPath
            )
        ))
    }
    foreach ($object in $objects) {
        $sumLines.Add((
            "{0}  {1}" -f @($object.sha256, $object.path)
        ))
    }
    [IO.File]::WriteAllText(
        (Join-Path $stagingRoot "SHA256SUMS"),
        ($sumLines -join "`n") + "`n",
        [Text.UTF8Encoding]::new($false)
    )

    if (Test-Path -LiteralPath $destination) {
        $previousRoot = (
            "{0}.previous-{1}" -f
                $destination,
                [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
        )
        Move-Item -LiteralPath $destination -Destination $previousRoot
    }

    try {
        Move-Item -LiteralPath $stagingRoot -Destination $destination
        $published = $true
    } catch {
        if ($null -ne $previousRoot -and
            (Test-Path -LiteralPath $previousRoot) -and
            -not (Test-Path -LiteralPath $destination)) {
            Move-Item -LiteralPath $previousRoot -Destination $destination
        }
        throw
    }

    $result = [ordered]@{
        status = "created"
        destination = $destination
        previous = $previousRoot
        profileCount = $inventory.profileCount
        objectReferenceCount = $inventory.objectReferenceCount
        uniqueObjectCount = $inventory.uniqueObjectCount
        uniqueObjectBytes = $inventory.uniqueObjectBytes
        objectSetSha256 = $inventory.objectSetSha256
        hardLinks = [bool]$UseHardLinks
    }

    if ($AsJson) {
        $result | ConvertTo-Json -Depth 4 -Compress
    } else {
        [pscustomobject]$result
    }
} finally {
    if (-not $published -and (Test-Path -LiteralPath $stagingRoot)) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
