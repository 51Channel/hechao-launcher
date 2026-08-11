[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or later is required. Run this script with pwsh."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$contentPath = Join-Path $repositoryRoot "handoff\package-import-template\package-content.json"
if (-not (Test-Path -LiteralPath $contentPath -PathType Leaf)) {
    throw "Package content definition is missing: $contentPath"
}

$content = Get-Content -LiteralPath $contentPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($content.schemaVersion -ne 1 -or
    $content.packageKind -ne "hechao-package-import-template-handoff") {
    throw "Package content definition has an unsupported schema or kind."
}

$archivePath = [System.IO.Path]::GetFullPath($OutputArchive)
if (-not $archivePath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputArchive must use the .zip extension."
}
$sidecarPath = $archivePath + ".sha256"
foreach ($path in @($archivePath, $sidecarPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Output already exists and will not be overwritten: $path"
    }
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($archivePath)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$stagingRoot = Join-Path $outputDirectory (
    ".hechao-package-import-template-" + [Guid]::NewGuid().ToString("N"))
$temporaryArchive = $archivePath + ".tmp-" + [Guid]::NewGuid().ToString("N")
$temporarySidecar = $sidecarPath + ".tmp-" + [Guid]::NewGuid().ToString("N")

function Copy-RepositoryFile {
    param(
        [Parameter(Mandatory = $true)][string] $SourceRelativePath,
        [Parameter(Mandatory = $true)][string] $DestinationRelativePath
    )

    $source = Join-Path $repositoryRoot $SourceRelativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required handoff source file is missing: $SourceRelativePath"
    }
    $destination = Join-Path $stagingRoot $DestinationRelativePath
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force |
        Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

function Get-StagingRecords {
    param([string[]] $ExcludedNames = @())

    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
            Sort-Object FullName)) {
        if ($file.Name -in $ExcludedNames) {
            continue
        }
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Handoff cannot contain symbolic links or reparse points: $($file.FullName)"
        }
        $relativePath = [System.IO.Path]::GetRelativePath(
            $stagingRoot,
            $file.FullName).Replace("\", "/")
        [void] $records.Add([pscustomobject] [ordered]@{
            path = $relativePath
            bytes = [long] $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    return @($records)
}

try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    $handoffSource = Join-Path $repositoryRoot ([string] $content.sourceDirectory)
    foreach ($file in @(Get-ChildItem -LiteralPath $handoffSource -File -Recurse -Force)) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $handoffSource,
            $file.FullName).Replace("\", "/")
        if ($relativePath -eq "package-content.json") {
            continue
        }
        Copy-RepositoryFile `
            -SourceRelativePath (([string] $content.sourceDirectory) + "/" + $relativePath) `
            -DestinationRelativePath $relativePath
    }

    Copy-RepositoryFile `
        -SourceRelativePath "handoff/package-import-template/package-content.json" `
        -DestinationRelativePath "reference/package-content.json"

    foreach ($tool in @($content.toolMappings)) {
        Copy-RepositoryFile `
            -SourceRelativePath ([string] $tool) `
            -DestinationRelativePath ([string] $tool)
    }

    foreach ($reference in @($content.referenceMappings)) {
        $relative = [string] $reference
        if ($relative.StartsWith("docs/", [System.StringComparison]::Ordinal)) {
            $destination = "reference/platform-docs/" + $relative.Substring("docs/".Length)
        }
        elseif ($relative.StartsWith("src/", [System.StringComparison]::Ordinal)) {
            $destination = "reference/source-contract/" + $relative.Substring("src/".Length)
        }
        else {
            throw "Reference mapping must start with docs/ or src/: $relative"
        }
        Copy-RepositoryFile -SourceRelativePath $relative -DestinationRelativePath $destination
    }

    $commit = (git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw "Unable to determine the repository commit."
    }
    $branch = (git -C $repositoryRoot branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine the repository branch."
    }
    $snapshot = [pscustomobject] [ordered]@{
        schemaVersion = 1
        packageKind = "hechao-package-import-template-handoff"
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        sourceCommit = $commit
        sourceBranch = $branch
        launcherApiContract = "0.30.1"
        packageDescriptorSchema = 1
        clientProfileSchema = 1
        productionStateIncluded = $false
        credentialsIncluded = $false
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingRoot "SOURCE-SNAPSHOT.json"),
        ($snapshot | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    $manifestEntries = Get-StagingRecords -ExcludedNames @("MANIFEST.json", "SHA256SUMS")
    $manifest = [pscustomobject] [ordered]@{
        schemaVersion = 1
        packageKind = "hechao-package-import-template-handoff"
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        totalFiles = $manifestEntries.Count
        totalBytes = [long] (($manifestEntries | Measure-Object bytes -Sum).Sum ?? 0)
        entries = $manifestEntries
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingRoot "MANIFEST.json"),
        ($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    $sumRecords = Get-StagingRecords -ExcludedNames @("SHA256SUMS")
    $sumLines = @($sumRecords | ForEach-Object { "$($_.sha256) *$($_.path)" })
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingRoot "SHA256SUMS"),
        ($sumLines -join [Environment]::NewLine) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.IO.Compression
    $stream = [System.IO.FileStream]::new(
        $temporaryArchive,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
                    Sort-Object FullName)) {
                $relativePath = [System.IO.Path]::GetRelativePath(
                    $stagingRoot,
                    $file.FullName).Replace("\", "/")
                $entry = $zip.CreateEntry(
                    $relativePath,
                    [System.IO.Compression.CompressionLevel]::NoCompression)
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = $file.OpenRead()
                try {
                    $destination = $entry.Open()
                    try {
                        $input.CopyTo($destination)
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
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }

    $archiveSha256 = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $archiveName = [System.IO.Path]::GetFileName($archivePath)
    [System.IO.File]::WriteAllText(
        $temporarySidecar,
        "$archiveSha256 *$archiveName$([Environment]::NewLine)",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporaryArchive, $archivePath)
    [System.IO.File]::Move($temporarySidecar, $sidecarPath)

    Write-Output ([pscustomobject] [ordered]@{
        archive = $archivePath
        sha256File = $sidecarPath
        archiveSha256 = $archiveSha256
        archiveBytes = (Get-Item -LiteralPath $archivePath).Length
        packageFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force).Count
        sourceCommit = $commit
    })
}
catch {
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $sidecarPath -Force -ErrorAction SilentlyContinue
    throw
}
finally {
    Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporarySidecar -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
