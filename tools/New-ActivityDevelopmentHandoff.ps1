#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$RepositoryRoot = (
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    ),

    [string]$ConfigurationPath = (
        Join-Path $PSScriptRoot `
            "..\handoff\activity-development\package-content.json"
    ),

    [string]$OutputDirectory = (
        Join-Path $PSScriptRoot "..\artifacts\handoff"
    ),

    [string]$PackageName,

    [switch]$AllowDirty,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Invoke-GitLines {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = @(& git -C $repositoryRoot @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "git failed: git $($Arguments -join ' ')"
    }
    return $output
}

function Invoke-GitScalar {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    return ((Invoke-GitLines -Arguments $Arguments | Out-String).Trim())
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

    return $normalizedCandidate.StartsWith(
        $normalizedParent + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase
    )
}

function ConvertTo-SafeRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$Value,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        [IO.Path]::IsPathRooted($Value) -or
        $Value.IndexOf([char]0) -ge 0) {
        throw "$Description must be a non-empty relative path."
    }

    $normalized = $Value.Replace("\", "/").Trim("/")
    $segments = @($normalized.Split("/"))
    foreach ($segment in $segments) {
        if ([string]::IsNullOrWhiteSpace($segment) -or
            $segment -in @(".", "..") -or
            $segment.EndsWith(".", [StringComparison]::Ordinal) -or
            $segment.EndsWith(" ", [StringComparison]::Ordinal) -or
            $segment -match '[\x00-\x1f:*?"<>|]') {
            throw "$Description contains an unsafe segment: $Value"
        }
    }

    return $segments -join "/"
}

function Resolve-RepositoryFile {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $nativeRelativePath = $RelativePath.Replace(
        "/",
        [IO.Path]::DirectorySeparatorChar
    )
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $nativeRelativePath)
    )
    if (-not (Test-IsSameOrChildPath `
            -Parent $repositoryRoot `
            -Candidate $candidate)) {
        throw "Source path escapes the repository: $RelativePath"
    }
    return $candidate
}

function Resolve-StagingFile {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $nativeRelativePath = $RelativePath.Replace(
        "/",
        [IO.Path]::DirectorySeparatorChar
    )
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $stagingRoot $nativeRelativePath)
    )
    if (-not (Test-IsSameOrChildPath `
            -Parent $stagingRoot `
            -Candidate $candidate)) {
        throw "Destination path escapes the staging root: $RelativePath"
    }
    return $candidate
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Value
    )

    [IO.File]::WriteAllText(
        $Path,
        $Value,
        [Text.UTF8Encoding]::new($false)
    )
}

function Write-Utf8Json {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [object]$Value
    )

    Write-Utf8Text `
        -Path $Path `
        -Value (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToLowerInvariant()
}

function Get-StagingRecords {
    param(
        [string[]]$ExcludedNames = @()
    )

    $excluded = [Collections.Generic.HashSet[string]]::new(
        $ExcludedNames,
        [StringComparer]::OrdinalIgnoreCase
    )
    $records = [Collections.Generic.List[object]]::new()
    foreach ($file in @(
            Get-ChildItem -LiteralPath $stagingRoot -File -Recurse
        )) {
        $relativePath = [IO.Path]::GetRelativePath(
            $stagingRoot,
            $file.FullName
        ).Replace("\", "/")
        if ($excluded.Contains($relativePath)) {
            continue
        }
        $records.Add([pscustomobject][ordered]@{
            path = $relativePath
            size = [int64]$file.Length
            sha256 = Get-Sha256 -Path $file.FullName
        })
    }
    return @($records | Sort-Object -Property path)
}

function Copy-MappedFile {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRelativePath,

        [Parameter(Mandatory)]
        [string]$DestinationRelativePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]]$Destinations,

        [switch]$MayBeUntracked
    )

    $source = ConvertTo-SafeRelativePath `
        -Value $SourceRelativePath `
        -Description "Mapping source"
    $destination = ConvertTo-SafeRelativePath `
        -Value $DestinationRelativePath `
        -Description "Mapping destination"
    if (-not $Destinations.Add($destination)) {
        throw "Duplicate package destination: $destination"
    }
    if (-not $MayBeUntracked -and -not $trackedFiles.Contains($source)) {
        throw "Mapped source is not tracked by Git: $source"
    }

    $sourcePath = Resolve-RepositoryFile -RelativePath $source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Mapped source file does not exist: $source"
    }
    $destinationPath = Resolve-StagingFile -RelativePath $destination
    [IO.Directory]::CreateDirectory(
        (Split-Path -Parent $destinationPath)
    ) | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
}

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd(
    "\",
    "/"
)
if ((Invoke-GitScalar -Arguments @("rev-parse", "--is-inside-work-tree")) -ne
    "true") {
    throw "RepositoryRoot is not a Git worktree."
}

$configuration = (Resolve-Path -LiteralPath $ConfigurationPath).Path
if (-not (Test-IsSameOrChildPath `
        -Parent $repositoryRoot `
        -Candidate $configuration)) {
    throw "ConfigurationPath must be inside RepositoryRoot."
}

try {
    $config = Get-Content -LiteralPath $configuration -Raw -Encoding utf8 |
        ConvertFrom-Json
} catch {
    throw "Handoff package configuration is not valid JSON."
}
if ($config.schemaVersion -ne 1 -or
    [string]$config.packageKind -ne
        "hechao-activity-development-handoff") {
    throw "Unsupported handoff package configuration."
}

$gitStatus = @(
    Invoke-GitLines -Arguments @(
        "status",
        "--porcelain=v1",
        "--untracked-files=all"
    )
)
$sourceDirty = $gitStatus.Count -gt 0
if ($sourceDirty -and -not $AllowDirty) {
    throw (
        "The repository must be clean before creating a formal handoff package. " +
        "Use -AllowDirty only for local development validation."
    )
}

$sourceCommit = Invoke-GitScalar -Arguments @("rev-parse", "HEAD")
$sourceShortCommit = Invoke-GitScalar `
    -Arguments @("rev-parse", "--short=10", "HEAD")
$sourceBranch = Invoke-GitScalar `
    -Arguments @("rev-parse", "--abbrev-ref", "HEAD")
$trackedFiles = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($line in Invoke-GitLines -Arguments @(
        "-c",
        "core.quotepath=false",
        "ls-files"
    )) {
    if (-not [string]::IsNullOrWhiteSpace($line)) {
        [void]$trackedFiles.Add(([string]$line).Replace("\", "/"))
    }
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "Hechao-Activity-Development-Handoff-{0}-{1}" -f
        (Get-Date -Format "yyyy-MM-dd"),
        $sourceShortCommit
}
if ($PackageName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{1,180}$') {
    throw "PackageName must be a safe ASCII path segment."
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd("\", "/")
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$finalArchive = Join-Path $outputRoot "$PackageName.zip"
$finalChecksum = "$finalArchive.sha256"
if ((Test-Path -LiteralPath $finalArchive) -or
    (Test-Path -LiteralPath $finalChecksum)) {
    throw "The handoff artifact already exists and will not be overwritten."
}

$workRoot = Join-Path $outputRoot (
    ".handoff-partial-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString("N")
)
$stagingParent = Join-Path $workRoot "content"
$stagingRoot = Join-Path $stagingParent $PackageName
$workArchive = Join-Path $workRoot "$PackageName.zip"
$workChecksum = "$workArchive.sha256"
$publishedArchive = $false
$publishedChecksum = $false

try {
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $destinations = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )

    foreach ($mappingGroup in @(
            @($config.rootMappings),
            @($config.fileMappings)
        )) {
        foreach ($mapping in $mappingGroup) {
            Copy-MappedFile `
                -SourceRelativePath ([string]$mapping.source) `
                -DestinationRelativePath ([string]$mapping.destination) `
                -Destinations $destinations `
                -MayBeUntracked:$AllowDirty
        }
    }

    foreach ($mapping in @($config.directoryMappings)) {
        $sourceDirectory = ConvertTo-SafeRelativePath `
            -Value ([string]$mapping.source) `
            -Description "Directory mapping source"
        $destinationDirectory = ConvertTo-SafeRelativePath `
            -Value ([string]$mapping.destination) `
            -Description "Directory mapping destination"
        $sourceDirectoryPath = Resolve-RepositoryFile `
            -RelativePath $sourceDirectory
        if (-not (Test-Path `
                -LiteralPath $sourceDirectoryPath `
                -PathType Container)) {
            throw "Mapped source directory does not exist: $sourceDirectory"
        }

        $prefix = $sourceDirectory.TrimEnd("/") + "/"
        $directoryFiles = @(
            $trackedFiles |
                Where-Object {
                    $_.StartsWith(
                        $prefix,
                        [StringComparison]::OrdinalIgnoreCase
                    )
                } |
                Sort-Object
        )
        if ($directoryFiles.Count -eq 0) {
            throw "Mapped source directory has no tracked files: $sourceDirectory"
        }

        foreach ($sourceFile in $directoryFiles) {
            $relativeChild = $sourceFile.Substring($prefix.Length)
            $segments = @($relativeChild.Split("/"))
            $isExcluded = $false
            foreach ($segment in $segments) {
                if (@($config.excludedPathSegments) -contains $segment) {
                    $isExcluded = $true
                    break
                }
            }
            if ($isExcluded) {
                continue
            }

            $extension = [IO.Path]::GetExtension($relativeChild)
            if (@($config.forbiddenExtensions) -contains $extension) {
                throw "Mapped directory contains a forbidden file: $sourceFile"
            }
            Copy-MappedFile `
                -SourceRelativePath $sourceFile `
                -DestinationRelativePath (
                    "$destinationDirectory/$relativeChild"
                ) `
                -Destinations $destinations
        }
    }

    $generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    $payloadRecords = @(Get-StagingRecords)
    [long]$payloadBytes = (
        $payloadRecords |
            Measure-Object -Property size -Sum
    ).Sum
    $packageInfo = [ordered]@{
        schemaVersion = 1
        packageKind = [string]$config.packageKind
        packageName = $PackageName
        generatedAtUtc = $generatedAtUtc
        sourceCommit = $sourceCommit
        sourceShortCommit = $sourceShortCommit
        sourceBranch = $sourceBranch
        sourceDirty = $sourceDirty
        configurationPath = [IO.Path]::GetRelativePath(
            $repositoryRoot,
            $configuration
        ).Replace("\", "/")
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        payloadFileCount = $payloadRecords.Count
        payloadBytes = $payloadBytes
        containsCredentials = $false
        productionServersStarted = $false
    }
    Write-Utf8Json `
        -Path (Join-Path $stagingRoot "PACKAGE-INFO.json") `
        -Value $packageInfo

    $manifestEntries = @(
        Get-StagingRecords -ExcludedNames @("MANIFEST.json", "SHA256SUMS")
    )
    [long]$manifestBytes = (
        $manifestEntries |
            Measure-Object -Property size -Sum
    ).Sum
    $manifest = [ordered]@{
        schemaVersion = 1
        packageKind = [string]$config.packageKind
        packageName = $PackageName
        generatedAtUtc = $generatedAtUtc
        sourceCommit = $sourceCommit
        entryCount = $manifestEntries.Count
        totalBytes = $manifestBytes
        entries = $manifestEntries
    }
    Write-Utf8Json `
        -Path (Join-Path $stagingRoot "MANIFEST.json") `
        -Value $manifest

    $sumRecords = @(
        Get-StagingRecords -ExcludedNames @("SHA256SUMS")
    )
    $sumLines = @(
        $sumRecords |
            ForEach-Object { "{0}  {1}" -f $_.sha256, $_.path }
    )
    Write-Utf8Text `
        -Path (Join-Path $stagingRoot "SHA256SUMS") `
        -Value (($sumLines -join "`n") + "`n")

    $validator = Join-Path $repositoryRoot `
        "tools\Test-ActivityDevelopmentHandoff.ps1"
    & $validator -PackageRoot $stagingRoot -AsJson | Out-Null

    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stagingParent,
        $workArchive,
        [IO.Compression.CompressionLevel]::Optimal,
        $false
    )
    $archiveSha256 = Get-Sha256 -Path $workArchive
    Write-Utf8Text `
        -Path $workChecksum `
        -Value ("{0}  {1}`n" -f $archiveSha256, (Split-Path -Leaf $workArchive))
    & $validator `
        -ArchivePath $workArchive `
        -ChecksumPath $workChecksum `
        -AsJson | Out-Null

    try {
        Move-Item -LiteralPath $workArchive -Destination $finalArchive
        $publishedArchive = $true
        Move-Item -LiteralPath $workChecksum -Destination $finalChecksum
        $publishedChecksum = $true
    } catch {
        if ($publishedChecksum -and (Test-Path -LiteralPath $finalChecksum)) {
            Remove-Item -LiteralPath $finalChecksum -Force
        }
        if ($publishedArchive -and (Test-Path -LiteralPath $finalArchive)) {
            Remove-Item -LiteralPath $finalArchive -Force
        }
        throw
    }

    $result = [ordered]@{
        status = "created"
        packageName = $PackageName
        sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty
        archivePath = $finalArchive
        checksumPath = $finalChecksum
        archiveBytes = (Get-Item -LiteralPath $finalArchive).Length
        archiveSha256 = $archiveSha256
        payloadFileCount = $payloadRecords.Count
        payloadBytes = $payloadBytes
        manifestEntryCount = $manifestEntries.Count
    }

    if ($AsJson) {
        $result | ConvertTo-Json -Depth 4 -Compress
    } else {
        [pscustomobject]$result
    }
} finally {
    if (Test-Path -LiteralPath $workRoot) {
        if (-not (Test-IsSameOrChildPath `
                -Parent $outputRoot `
                -Candidate $workRoot) -or
            -not (Split-Path -Leaf $workRoot).StartsWith(
                ".handoff-partial-",
                [StringComparison]::Ordinal
            )) {
            throw "Refusing to remove an unsafe handoff staging path."
        }
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
