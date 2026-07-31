#requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = "Archive")]
param(
    [Parameter(Mandatory, ParameterSetName = "Archive")]
    [string]$ArchivePath,

    [Parameter(ParameterSetName = "Archive")]
    [string]$ChecksumPath,

    [Parameter(Mandatory, ParameterSetName = "Directory")]
    [string]$PackageRoot,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$packageKind = "hechao-activity-development-handoff"
$maximumFileCount = 10000
$maximumSingleFileBytes = 128MB
$maximumPackageBytes = 1GB
$excludedSegments = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($segment in @(
        ".git",
        ".gradle",
        "artifacts",
        "bin",
        "build",
        "node_modules",
        "obj",
        "secrets",
        "TestResults"
    )) {
    [void]$excludedSegments.Add($segment)
}

$forbiddenExtensions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($extension in @(
        ".db",
        ".dmp",
        ".dpapi",
        ".dump",
        ".jks",
        ".key",
        ".keystore",
        ".log",
        ".p12",
        ".pem",
        ".pfx",
        ".sqlite",
        ".sqlite3"
    )) {
    [void]$forbiddenExtensions.Add($extension)
}

$textExtensions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($extension in @(
        "",
        ".config",
        ".cs",
        ".csproj",
        ".editorconfig",
        ".gitignore",
        ".json",
        ".md",
        ".props",
        ".ps1",
        ".psd1",
        ".psm1",
        ".sln",
        ".targets",
        ".toml",
        ".txt",
        ".xml",
        ".yaml",
        ".yml"
    )) {
    [void]$textExtensions.Add($extension)
}

$secretSignatures = @(
    [ordered]@{
        name = "private key material"
        pattern = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
    },
    [ordered]@{
        name = "GitHub access token"
        pattern = '(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'
    },
    [ordered]@{
        name = "Aliyun AccessKey ID"
        pattern = '(?<![A-Za-z0-9])LTAI[A-Za-z0-9]{12,}(?![A-Za-z0-9])'
    },
    [ordered]@{
        name = "AWS access key ID"
        pattern = '(?<![A-Z0-9])(?:AKIA|ASIA)[A-Z0-9]{16}(?![A-Z0-9])'
    }
)

$requiredFiles = @(
    "README.md",
    "AGENTS.md",
    "00-从这里开始.md",
    "01-给Codex的首条消息.md",
    "02-新活动需求单模板.md",
    "03-如何基于现有框架开发.md",
    "04-开发与上线验收清单.md",
    "05-最终交付报告模板.md",
    "06-常见错误与处理.md",
    "docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md",
    "docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md",
    "reference/package-content.json",
    "tools/Test-ActivityDevelopmentHandoff.ps1",
    "PACKAGE-INFO.json",
    "MANIFEST.json",
    "SHA256SUMS"
)

function Get-Sha256FromBytes {
    param(
        [Parameter(Mandatory)]
        [byte[]]$Bytes
    )

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($Bytes)
    ).ToLowerInvariant()
}

function Get-Sha256FromFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToLowerInvariant()
}

function ConvertTo-NormalizedPackagePath {
    param(
        [Parameter(Mandatory)]
        [string]$Value,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        [IO.Path]::IsPathRooted($Value) -or
        $Value.Contains("\") -or
        $Value.IndexOf([char]0) -ge 0) {
        throw "$Description must be a non-empty forward-slash relative path."
    }

    $segments = @($Value.Split("/"))
    if ($segments.Count -eq 0) {
        throw "$Description is empty."
    }

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

function Assert-SafePackagePath {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $normalized = ConvertTo-NormalizedPackagePath `
        -Value $RelativePath `
        -Description "Package path"
    $segments = @($normalized.Split("/"))
    foreach ($segment in $segments) {
        if ($excludedSegments.Contains($segment)) {
            throw "Package contains an excluded path segment: $normalized"
        }
    }

    $fileName = $segments[-1]
    $extension = [IO.Path]::GetExtension($fileName)
    if ($forbiddenExtensions.Contains($extension)) {
        throw "Package contains a forbidden file type: $normalized"
    }
    if ($fileName -eq ".env" -or
        $fileName.StartsWith(".env.", [StringComparison]::OrdinalIgnoreCase) -or
        $fileName -match '(?i)^appsettings\..*\.local\.json$' -or
        $fileName -match '(?i)^id_(?:rsa|dsa|ecdsa|ed25519)(?:\.pub)?$') {
        throw "Package contains a forbidden local or credential file: $normalized"
    }

    return $normalized
}

function ConvertFrom-StrictUtf8 {
    param(
        [Parameter(Mandatory)]
        [byte[]]$Bytes,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    } catch {
        throw "Expected UTF-8 text but decoding failed: $RelativePath"
    }
}

function Test-TextForSecrets {
    param(
        [Parameter(Mandatory)]
        [string]$Text,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    foreach ($signature in $secretSignatures) {
        if ([Text.RegularExpressions.Regex]::IsMatch(
                $Text,
                [string]$signature.pattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant
            )) {
            throw (
                "Package contains a high-confidence {0} signature in {1}." -f
                    $signature.name,
                    $RelativePath
            )
        }
    }
}

function New-FileRecord {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [byte[]]$Bytes
    )

    $normalized = Assert-SafePackagePath -RelativePath $RelativePath
    if ($Bytes.LongLength -gt $maximumSingleFileBytes) {
        throw "Package file exceeds the size limit: $normalized"
    }

    $extension = [IO.Path]::GetExtension($normalized)
    $text = $null
    if ($textExtensions.Contains($extension)) {
        $text = ConvertFrom-StrictUtf8 -Bytes $Bytes -RelativePath $normalized
        Test-TextForSecrets -Text $text -RelativePath $normalized
    }

    return [pscustomobject]@{
        path = $normalized
        size = [int64]$Bytes.LongLength
        sha256 = Get-Sha256FromBytes -Bytes $Bytes
        text = $text
    }
}

function Read-ArchiveRecords {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $records = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    $roots = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    [long]$totalBytes = 0

    $fileStream = [IO.File]::OpenRead($Path)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $fileStream,
            [IO.Compression.ZipArchiveMode]::Read,
            $false,
            [Text.Encoding]::UTF8
        )
        try {
            foreach ($entry in $archive.Entries) {
                $entryPath = [string]$entry.FullName
                if ([string]::IsNullOrWhiteSpace($entryPath) -or
                    $entryPath.Contains("\") -or
                    $entryPath.IndexOf([char]0) -ge 0) {
                    throw "Archive contains an invalid entry path."
                }

                $isDirectory = $entryPath.EndsWith(
                    "/",
                    [StringComparison]::Ordinal
                )
                $pathToValidate = if ($isDirectory) {
                    $entryPath.TrimEnd("/")
                } else {
                    $entryPath
                }
                $normalizedEntry = ConvertTo-NormalizedPackagePath `
                    -Value $pathToValidate `
                    -Description "Archive entry"
                $parts = @($normalizedEntry.Split("/"))
                if ($parts.Count -lt 1) {
                    throw "Archive entry has no top-level directory."
                }
                [void]$roots.Add($parts[0])

                $unixFileType = ($entry.ExternalAttributes -shr 16) -band 0xF000
                $windowsAttributes = $entry.ExternalAttributes -band 0xFFFF
                if ($unixFileType -eq 0xA000 -or
                    ($windowsAttributes -band [int][IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Archive contains a symbolic link or reparse point: $entryPath"
                }

                if ($isDirectory) {
                    continue
                }
                if ($parts.Count -lt 2) {
                    throw "Archive files must be inside one top-level directory."
                }
                if ($entry.Length -lt 0 -or
                    $entry.Length -gt $maximumSingleFileBytes) {
                    throw "Archive entry exceeds the size limit: $entryPath"
                }
                if ($entry.CompressedLength -gt 0 -and
                    $entry.Length -gt 1MB -and
                    ($entry.Length / $entry.CompressedLength) -gt 1000) {
                    throw "Archive entry has an unsafe compression ratio: $entryPath"
                }

                $relativePath = $parts[1..($parts.Count - 1)] -join "/"
                $memory = [IO.MemoryStream]::new()
                try {
                    $entryStream = $entry.Open()
                    try {
                        $entryStream.CopyTo($memory)
                    } finally {
                        $entryStream.Dispose()
                    }
                    $bytes = $memory.ToArray()
                } finally {
                    $memory.Dispose()
                }

                if ($bytes.LongLength -ne $entry.Length) {
                    throw "Archive entry length changed while reading: $entryPath"
                }
                $record = New-FileRecord `
                    -RelativePath $relativePath `
                    -Bytes $bytes
                if ($records.ContainsKey($record.path)) {
                    throw "Archive contains a duplicate path: $($record.path)"
                }
                $records.Add($record.path, $record)
                $totalBytes += $record.size
                if ($records.Count -gt $maximumFileCount -or
                    $totalBytes -gt $maximumPackageBytes) {
                    throw "Archive exceeds the package size limits."
                }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $fileStream.Dispose()
    }

    if ($roots.Count -ne 1) {
        throw "Archive must contain exactly one top-level directory."
    }

    return [pscustomobject]@{
        records = $records
        rootName = @($roots)[0]
        totalBytes = $totalBytes
    }
}

function Read-DirectoryRecords {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $records = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    [long]$totalBytes = 0
    $items = @(Get-ChildItem -LiteralPath $Root -Force -Recurse)
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Package directory contains a reparse point: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            continue
        }

        $relativePath = [IO.Path]::GetRelativePath($Root, $item.FullName).
            Replace("\", "/")
        $bytes = [IO.File]::ReadAllBytes($item.FullName)
        $record = New-FileRecord `
            -RelativePath $relativePath `
            -Bytes $bytes
        if ($records.ContainsKey($record.path)) {
            throw "Package directory contains a duplicate path: $($record.path)"
        }
        $records.Add($record.path, $record)
        $totalBytes += $record.size
        if ($records.Count -gt $maximumFileCount -or
            $totalBytes -gt $maximumPackageBytes) {
            throw "Package directory exceeds the package size limits."
        }
    }

    return [pscustomobject]@{
        records = $records
        rootName = Split-Path -Leaf $Root
        totalBytes = $totalBytes
    }
}

function Get-RequiredRecord {
    param(
        [Parameter(Mandatory)]
        [Collections.Generic.Dictionary[string, object]]$Records,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if (-not $Records.ContainsKey($RelativePath)) {
        throw "Required package file is missing: $RelativePath"
    }
    return $Records[$RelativePath]
}

function ConvertFrom-PackageJson {
    param(
        [Parameter(Mandatory)]
        [object]$Record
    )

    if ($null -eq $Record.text) {
        throw "Package JSON is not UTF-8 text: $($Record.path)"
    }
    try {
        return $Record.text | ConvertFrom-Json
    } catch {
        throw "Package JSON is invalid: $($Record.path)"
    }
}

function Assert-RecordSetsEqual {
    param(
        [Parameter(Mandatory)]
        [Collections.Generic.HashSet[string]]$Expected,

        [Parameter(Mandatory)]
        [Collections.Generic.HashSet[string]]$Actual,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $missing = @($Expected | Where-Object { -not $Actual.Contains($_) })
    $extra = @($Actual | Where-Object { -not $Expected.Contains($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw (
            "{0} paths differ. Missing: [{1}]. Extra: [{2}]." -f
                $Description,
                ($missing -join ", "),
                ($extra -join ", ")
        )
    }
}

function Test-PackageRecords {
    param(
        [Parameter(Mandatory)]
        [Collections.Generic.Dictionary[string, object]]$Records,

        [Parameter(Mandatory)]
        [string]$RootName
    )

    foreach ($requiredFile in $requiredFiles) {
        [void](Get-RequiredRecord `
            -Records $Records `
            -RelativePath $requiredFile)
    }

    $sumsRecord = Get-RequiredRecord `
        -Records $Records `
        -RelativePath "SHA256SUMS"
    if ($null -eq $sumsRecord.text) {
        throw "SHA256SUMS must be UTF-8 text."
    }

    $sumPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($line in $sumsRecord.text -split "\r?\n") {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
            throw "SHA256SUMS contains an invalid line."
        }

        $expectedSha256 = $Matches[1]
        $relativePath = Assert-SafePackagePath -RelativePath $Matches[2]
        if ($relativePath -eq "SHA256SUMS") {
            throw "SHA256SUMS cannot include itself."
        }
        if (-not $sumPaths.Add($relativePath)) {
            throw "SHA256SUMS contains a duplicate path: $relativePath"
        }
        $record = Get-RequiredRecord `
            -Records $Records `
            -RelativePath $relativePath
        if ($record.sha256 -ne $expectedSha256) {
            throw "SHA256SUMS validation failed: $relativePath"
        }
    }

    $expectedSumPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($path in $Records.Keys) {
        if ($path -ne "SHA256SUMS") {
            [void]$expectedSumPaths.Add($path)
        }
    }
    Assert-RecordSetsEqual `
        -Expected $expectedSumPaths `
        -Actual $sumPaths `
        -Description "SHA256SUMS"

    $manifestRecord = Get-RequiredRecord `
        -Records $Records `
        -RelativePath "MANIFEST.json"
    $manifest = ConvertFrom-PackageJson -Record $manifestRecord
    if ($manifest.schemaVersion -ne 1 -or
        [string]$manifest.packageKind -ne $packageKind) {
        throw "MANIFEST.json has an unsupported schema or package kind."
    }

    $manifestPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    [long]$manifestBytes = 0
    foreach ($entry in @($manifest.entries)) {
        $relativePath = Assert-SafePackagePath -RelativePath ([string]$entry.path)
        if ($relativePath -in @("MANIFEST.json", "SHA256SUMS")) {
            throw "MANIFEST.json cannot list package integrity metadata: $relativePath"
        }
        if (-not $manifestPaths.Add($relativePath)) {
            throw "MANIFEST.json contains a duplicate path: $relativePath"
        }
        $record = Get-RequiredRecord `
            -Records $Records `
            -RelativePath $relativePath
        if ([int64]$entry.size -ne $record.size -or
            [string]$entry.sha256 -cne $record.sha256) {
            throw "MANIFEST.json validation failed: $relativePath"
        }
        $manifestBytes += $record.size
    }

    $expectedManifestPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($path in $Records.Keys) {
        if ($path -notin @("MANIFEST.json", "SHA256SUMS")) {
            [void]$expectedManifestPaths.Add($path)
        }
    }
    Assert-RecordSetsEqual `
        -Expected $expectedManifestPaths `
        -Actual $manifestPaths `
        -Description "MANIFEST.json"
    if ([int64]$manifest.entryCount -ne $manifestPaths.Count -or
        [int64]$manifest.totalBytes -ne $manifestBytes) {
        throw "MANIFEST.json totals do not match its entries."
    }

    $packageInfoRecord = Get-RequiredRecord `
        -Records $Records `
        -RelativePath "PACKAGE-INFO.json"
    $packageInfo = ConvertFrom-PackageJson -Record $packageInfoRecord
    if ($packageInfo.schemaVersion -ne 1 -or
        [string]$packageInfo.packageKind -ne $packageKind) {
        throw "PACKAGE-INFO.json has an unsupported schema or package kind."
    }
    if ([string]$packageInfo.packageName -ne $RootName) {
        throw "Package root does not match PACKAGE-INFO.json."
    }
    if ([string]$manifest.packageName -ne [string]$packageInfo.packageName -or
        [string]$manifest.sourceCommit -ne [string]$packageInfo.sourceCommit -or
        [string]$manifest.generatedAtUtc -ne [string]$packageInfo.generatedAtUtc) {
        throw "Package metadata files do not identify the same build."
    }
    if ([string]$packageInfo.sourceCommit -notmatch '^[0-9a-f]{40}$' -or
        [string]$packageInfo.sourceShortCommit -notmatch '^[0-9a-f]{7,12}$') {
        throw "PACKAGE-INFO.json contains an invalid Git commit."
    }

    $generatedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string]$packageInfo.generatedAtUtc,
            [ref]$generatedAt
        )) {
        throw "PACKAGE-INFO.json contains an invalid generatedAtUtc."
    }

    $expectedPayloadPaths = @(
        $manifestPaths |
            Where-Object { $_ -ne "PACKAGE-INFO.json" }
    )
    [long]$expectedPayloadBytes = 0
    foreach ($path in $expectedPayloadPaths) {
        $expectedPayloadBytes += $Records[$path].size
    }
    if ([int64]$packageInfo.payloadFileCount -ne $expectedPayloadPaths.Count -or
        [int64]$packageInfo.payloadBytes -ne $expectedPayloadBytes) {
        throw "PACKAGE-INFO.json payload totals do not match the package."
    }

    return [pscustomobject]@{
        packageInfo = $packageInfo
        manifest = $manifest
        validatedFileCount = $Records.Count
        validatedBytes = [int64](
            $Records.Values |
                Measure-Object -Property size -Sum
        ).Sum
    }
}

$archiveSha256 = $null
if ($PSCmdlet.ParameterSetName -eq "Archive") {
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    if ([IO.Path]::GetExtension($resolvedArchive) -ne ".zip") {
        throw "ArchivePath must point to a .zip file."
    }
    if ([string]::IsNullOrWhiteSpace($ChecksumPath)) {
        $ChecksumPath = "$resolvedArchive.sha256"
    }
    $resolvedChecksum = (Resolve-Path -LiteralPath $ChecksumPath).Path
    $checksumLines = @(
        Get-Content -LiteralPath $resolvedChecksum -Encoding utf8 |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($checksumLines.Count -ne 1 -or
        $checksumLines[0] -notmatch '^([0-9a-f]{64})  ([^\\/]+\.zip)$') {
        throw "Archive checksum sidecar is invalid."
    }
    if ($Matches[2] -cne (Split-Path -Leaf $resolvedArchive)) {
        throw "Archive checksum sidecar names a different file."
    }
    $archiveSha256 = Get-Sha256FromFile -Path $resolvedArchive
    if ($archiveSha256 -cne $Matches[1]) {
        throw "Archive SHA-256 does not match its sidecar."
    }

    $readResult = Read-ArchiveRecords -Path $resolvedArchive
    $sourceDescription = $resolvedArchive
} else {
    $resolvedRoot = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd("\", "/")
    $rootItem = Get-Item -LiteralPath $resolvedRoot
    if (-not $rootItem.PSIsContainer -or
        ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "PackageRoot must be a real directory, not a reparse point."
    }
    $readResult = Read-DirectoryRecords -Root $resolvedRoot
    $sourceDescription = $resolvedRoot
}

$validation = Test-PackageRecords `
    -Records $readResult.records `
    -RootName $readResult.rootName

$result = [ordered]@{
    status = "valid"
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    mode = $PSCmdlet.ParameterSetName.ToLowerInvariant()
    source = $sourceDescription
    packageName = [string]$validation.packageInfo.packageName
    packageKind = [string]$validation.packageInfo.packageKind
    sourceCommit = [string]$validation.packageInfo.sourceCommit
    sourceDirty = [bool]$validation.packageInfo.sourceDirty
    validatedFileCount = [int64]$validation.validatedFileCount
    validatedBytes = [int64]$validation.validatedBytes
    archiveSha256 = $archiveSha256
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4 -Compress
} else {
    [pscustomobject]$result
}
