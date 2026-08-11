[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or later is required. Run this script with pwsh."
}

$validator = Join-Path $PSScriptRoot "Test-HechaoPackageImportSource.ps1"
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Package source validator is missing: $validator"
}

$sourceRoot = [System.IO.Path]::GetFullPath($SourceDirectory).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$archivePath = [System.IO.Path]::GetFullPath($OutputArchive)
if (-not $archivePath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputArchive must use the .zip extension."
}

$archiveName = [System.IO.Path]::GetFileName($archivePath)
$archiveNameHasControl = $false
foreach ($character in $archiveName.ToCharArray()) {
    if ([char]::IsControl($character)) {
        $archiveNameHasControl = $true
        break
    }
}
if ($archiveName.Length -lt 5 -or $archiveName.Length -gt 180 -or
    $archiveNameHasControl) {
    throw "The output ZIP file name must be 5-180 visible characters."
}

$sourcePrefix = $sourceRoot + [System.IO.Path]::DirectorySeparatorChar
if ($archivePath.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputArchive must be outside SourceDirectory."
}

$reportPath = $archivePath + ".report.json"
$sha256Path = $archivePath + ".sha256"
foreach ($path in @($archivePath, $reportPath, $sha256Path)) {
    if (Test-Path -LiteralPath $path) {
        throw "Output already exists and will not be overwritten: $path"
    }
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($archivePath)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "OutputArchive does not have a valid parent directory."
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$validation = & $validator -SourceDirectory $sourceRoot -PassThru
$temporaryArchive = $archivePath + ".tmp-" + [Guid]::NewGuid().ToString("N")
$temporaryReport = $reportPath + ".tmp-" + [Guid]::NewGuid().ToString("N")
$temporarySha256 = $sha256Path + ".tmp-" + [Guid]::NewGuid().ToString("N")
$archiveMoved = $false

try {
    Add-Type -AssemblyName System.IO.Compression
    $fileStream = [System.IO.FileStream]::new(
        $temporaryArchive,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None,
        1MB,
        [System.IO.FileOptions]::SequentialScan)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new(
            $fileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($record in @($validation.files | Sort-Object path)) {
                $sourcePath = Join-Path $sourceRoot ($record.path.Replace(
                    "/",
                    [System.IO.Path]::DirectorySeparatorChar))
                $entry = $zip.CreateEntry(
                    $record.path,
                    [System.IO.Compression.CompressionLevel]::NoCompression)
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    2000,
                    1,
                    1,
                    0,
                    0,
                    0,
                    [TimeSpan]::Zero)
                $input = [System.IO.FileStream]::new(
                    $sourcePath,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::Read,
                    1MB,
                    [System.IO.FileOptions]::SequentialScan)
                try {
                    $destination = $entry.Open()
                    try {
                        $input.CopyTo($destination, 1MB)
                    }
                    finally {
                        $destination.Dispose()
                    }
                }
                finally {
                    $input.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }
        $fileStream.Flush($true)
    }
    finally {
        $fileStream.Dispose()
    }

    $archiveFile = Get-Item -LiteralPath $temporaryArchive
    if ($archiveFile.Length -gt 4L * 1024 * 1024 * 1024) {
        throw "The generated ZIP exceeds the current 4 GiB upload limit."
    }

    $expected = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $validation.files) {
        $expected.Add([string] $record.path, $record)
    }

    $readArchive = [System.IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    try {
        if ($readArchive.Entries.Count -ne $expected.Count) {
            throw "Generated ZIP entry count does not match the validated source."
        }
        foreach ($entry in $readArchive.Entries) {
            if (-not $expected.ContainsKey($entry.FullName)) {
                throw "Generated ZIP contains an unexpected entry: $($entry.FullName)"
            }
            $record = $expected[$entry.FullName]
            if ($entry.Length -ne [long] $record.size) {
                throw "Generated ZIP entry length mismatch: $($entry.FullName)"
            }
            $entryStream = $entry.Open()
            try {
                $digest = [System.Convert]::ToHexString(
                    [System.Security.Cryptography.SHA256]::HashData($entryStream)
                ).ToLowerInvariant()
            }
            finally {
                $entryStream.Dispose()
            }
            if ($digest -cne [string] $record.sha256) {
                throw "Generated ZIP entry SHA-256 mismatch: $($entry.FullName)"
            }
        }
    }
    finally {
        $readArchive.Dispose()
    }

    $archiveSha256 = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $artifactReport = [pscustomobject] [ordered]@{
        schemaVersion = 1
        reportKind = "hechao-package-import-archive"
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        archive = [pscustomobject] [ordered]@{
            fileName = $archiveName
            bytes = [long] $archiveFile.Length
            sha256 = $archiveSha256
            layout = "canonical-client-server-shared-v1"
            compression = "store"
        }
        package = $validation.package
        totals = $validation.totals
        commonJars = $validation.commonJars
        warnings = $validation.warnings
        files = $validation.files
    }
    $reportJson = $artifactReport | ConvertTo-Json -Depth 12
    [System.IO.File]::WriteAllText(
        $temporaryReport,
        $reportJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        $temporarySha256,
        "$archiveSha256 *$archiveName$([Environment]::NewLine)",
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::Move($temporaryArchive, $archivePath)
    $archiveMoved = $true
    [System.IO.File]::Move($temporaryReport, $reportPath)
    [System.IO.File]::Move($temporarySha256, $sha256Path)

    Write-Output ([pscustomobject] [ordered]@{
        archive = $archivePath
        report = $reportPath
        sha256File = $sha256Path
        archiveSha256 = $archiveSha256
        archiveBytes = [long] $archiveFile.Length
        fileCount = [int] $validation.totals.fileCount
    })
}
catch {
    if ($archiveMoved) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $sha256Path -Force -ErrorAction SilentlyContinue
    throw
}
finally {
    Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryReport -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporarySha256 -Force -ErrorAction SilentlyContinue
}
