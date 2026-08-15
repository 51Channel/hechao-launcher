[CmdletBinding()]
param(
    [string]$DotNetPath = 'dotnet',
    [string]$Configuration = 'Release',
    [string]$Version = '0.15.8',
    [string]$OutputDirectory,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\macos-m4'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\macos-m4-staging'))
$publishRoot = Join-Path $stagingRoot 'publish'
$appRoot = Join-Path $outputRoot '赫朝启动器.app'
$contentsRoot = Join-Path $appRoot 'Contents'
$macOsRoot = Join-Path $contentsRoot 'MacOS'
$resourcesRoot = Join-Path $contentsRoot 'Resources'
$projectPath = Join-Path $repoRoot 'src\Hechao.Launcher.Mac\Hechao.Launcher.Mac.csproj'
$packagingRoot = Join-Path $repoRoot 'src\Hechao.Launcher.Mac\Packaging'
$iconSource = Join-Path $repoRoot 'src\Hechao.Launcher\Assets\hechao-launcher-icon.png'
$executableName = 'Hechao.Launcher.Mac'

function Assert-ScopedPath {
    param([string]$Path, [string]$AllowedRoot)
    $candidate = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the expected root: $candidate"
    }
}

function Remove-ScopedDirectory {
    param([string]$Path, [string]$AllowedRoot)
    Assert-ScopedPath -Path $Path -AllowedRoot $AllowedRoot
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-BigEndianUInt32 {
    param([IO.Stream]$Stream, [uint32]$Value)
    $bytes = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($bytes)
    }
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Read-BigEndianUInt32 {
    param([IO.Stream]$Stream)
    $bytes = [byte[]]::new(4)
    if ($Stream.Read($bytes, 0, $bytes.Length) -ne $bytes.Length) {
        throw 'Unexpected end of Mach-O file.'
    }
    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($bytes)
    }
    return [BitConverter]::ToUInt32($bytes, 0)
}

function New-IcnsFile {
    param([string]$PngPath, [string]$DestinationPath)
    $png = [IO.File]::ReadAllBytes($PngPath)
    $stream = [IO.File]::Create($DestinationPath)
    try {
        $magic = [Text.Encoding]::ASCII.GetBytes('icns')
        $chunkType = [Text.Encoding]::ASCII.GetBytes('ic09')
        $stream.Write($magic, 0, $magic.Length)
        Write-BigEndianUInt32 -Stream $stream -Value ([uint32](16 + $png.Length))
        $stream.Write($chunkType, 0, $chunkType.Length)
        Write-BigEndianUInt32 -Stream $stream -Value ([uint32](8 + $png.Length))
        $stream.Write($png, 0, $png.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Test-Arm64MachO {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = [byte[]]::new(8)
        if ($stream.Read($header, 0, $header.Length) -ne $header.Length) {
            return $false
        }
        return $header[0] -eq 0xCF -and
            $header[1] -eq 0xFA -and
            $header[2] -eq 0xED -and
            $header[3] -eq 0xFE -and
            $header[4] -eq 0x0C -and
            $header[5] -eq 0x00 -and
            $header[6] -eq 0x00 -and
            $header[7] -eq 0x01
    }
    finally {
        $stream.Dispose()
    }
}

function Test-MachOFile {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $magic = [byte[]]::new(4)
        if ($stream.Read($magic, 0, $magic.Length) -ne $magic.Length) {
            return $false
        }
        $hex = [Convert]::ToHexString($magic)
        return $hex -in @(
            'FEEDFACE',
            'CEFAEDFE',
            'FEEDFACF',
            'CFFAEDFE',
            'CAFEBABE',
            'BEBAFECA',
            'CAFEBABF',
            'BFBAFECA')
    }
    finally {
        $stream.Dispose()
    }
}

function Convert-FatMachOToArm64 {
    param([string]$Path)
    $source = [IO.File]::OpenRead($Path)
    try {
        $fatMagic = [uint32]::Parse(
            'CAFEBABE',
            [Globalization.NumberStyles]::HexNumber)
        if ((Read-BigEndianUInt32 -Stream $source) -ne $fatMagic) {
            return $false
        }
        $architectureCount = Read-BigEndianUInt32 -Stream $source
        if ($architectureCount -lt 1 -or $architectureCount -gt 32) {
            throw "Invalid FAT Mach-O architecture count in ${Path}: $architectureCount"
        }

        $arm64Offset = [uint32]0
        $arm64Size = [uint32]0
        for ($index = 0; $index -lt $architectureCount; $index++) {
            $cpuType = Read-BigEndianUInt32 -Stream $source
            $null = Read-BigEndianUInt32 -Stream $source
            $offset = Read-BigEndianUInt32 -Stream $source
            $size = Read-BigEndianUInt32 -Stream $source
            $null = Read-BigEndianUInt32 -Stream $source
            if ($cpuType -eq 0x0100000C) {
                $arm64Offset = $offset
                $arm64Size = $size
            }
        }
        if ($arm64Size -eq 0) {
            return $false
        }
        if ([uint64]$arm64Offset + [uint64]$arm64Size -gt [uint64]$source.Length) {
            throw "Invalid ARM64 slice bounds in FAT Mach-O file: $Path"
        }

        $temporaryPath = "$Path.arm64"
        $destination = [IO.File]::Create($temporaryPath)
        try {
            $source.Position = $arm64Offset
            $remaining = [uint64]$arm64Size
            $buffer = [byte[]]::new(1MB)
            while ($remaining -gt 0) {
                $requested = [int][Math]::Min([uint64]$buffer.Length, $remaining)
                $read = $source.Read($buffer, 0, $requested)
                if ($read -eq 0) {
                    throw "Unexpected end of ARM64 slice in FAT Mach-O file: $Path"
                }
                $destination.Write($buffer, 0, $read)
                $remaining -= [uint64]$read
            }
        }
        finally {
            $destination.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    if (-not (Test-Arm64MachO -Path $temporaryPath)) {
        Remove-Item -LiteralPath $temporaryPath -Force
        throw "Extracted Mach-O slice is not ARM64: $Path"
    }
    [IO.File]::Move($temporaryPath, $Path, $true)
    return $true
}

function New-UnixModeZip {
    param([string]$SourceDirectory, [string]$DestinationPath)
    Add-Type -AssemblyName System.IO.Compression
    $archiveStream = [IO.File]::Create($DestinationPath)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $sourceParent = Split-Path -Parent $SourceDirectory
            foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File) {
                $relative = [IO.Path]::GetRelativePath($sourceParent, $file.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    $relative,
                    [IO.Compression.CompressionLevel]::Optimal)
                $isExecutable = $relative.StartsWith('赫朝启动器.app/Contents/MacOS/')
                $unixMode = if ($isExecutable) { 0x81ED } else { 0x81A4 }
                $entry.ExternalAttributes = [int]($unixMode -shl 16)
                $entry.LastWriteTime = $file.LastWriteTime
                $input = $file.OpenRead()
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Set-ZipUnixHostPlatform {
    param([string]$Path)
    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    try {
        $tailLength = [int][Math]::Min(65557, $stream.Length)
        $tail = [byte[]]::new($tailLength)
        $stream.Position = $stream.Length - $tailLength
        if ($stream.Read($tail, 0, $tail.Length) -ne $tail.Length) {
            throw "Unable to read ZIP directory: $Path"
        }

        $endOfCentralDirectory = -1
        for ($index = $tail.Length - 22; $index -ge 0; $index--) {
            if ($tail[$index] -eq 0x50 -and
                $tail[$index + 1] -eq 0x4B -and
                $tail[$index + 2] -eq 0x05 -and
                $tail[$index + 3] -eq 0x06) {
                $endOfCentralDirectory = $index
                break
            }
        }
        if ($endOfCentralDirectory -lt 0) {
            throw "ZIP end-of-central-directory record is missing: $Path"
        }

        $entryCount = [BitConverter]::ToUInt16(
            $tail,
            $endOfCentralDirectory + 10)
        $centralDirectoryOffset = [BitConverter]::ToUInt32(
            $tail,
            $endOfCentralDirectory + 16)
        if ($entryCount -eq [uint16]::MaxValue -or
            $centralDirectoryOffset -eq [uint32]::MaxValue) {
            throw 'ZIP64 archives are not supported by the macOS bundle packager.'
        }

        $stream.Position = $centralDirectoryOffset
        for ($entryIndex = 0; $entryIndex -lt $entryCount; $entryIndex++) {
            $headerPosition = $stream.Position
            $header = [byte[]]::new(46)
            if ($stream.Read($header, 0, $header.Length) -ne $header.Length -or
                [BitConverter]::ToUInt32($header, 0) -ne 0x02014B50) {
                throw "Invalid ZIP central-directory entry: $Path"
            }

            $fileNameLength = [BitConverter]::ToUInt16($header, 28)
            $extraFieldLength = [BitConverter]::ToUInt16($header, 30)
            $commentLength = [BitConverter]::ToUInt16($header, 32)

            # ZipArchive writes Unix mode bits but labels entries as Windows.
            # macOS only restores those bits when the host platform is Unix (3).
            $stream.Position = $headerPosition + 5
            $stream.WriteByte(3)
            $stream.Position =
                $headerPosition +
                $header.Length +
                $fileNameLength +
                $extraFieldLength +
                $commentLength
        }
    }
    finally {
        $stream.Dispose()
    }
}

Assert-ScopedPath -Path $stagingRoot -AllowedRoot $repoRoot
if (-not $SkipPublish) {
    Remove-ScopedDirectory -Path $stagingRoot -AllowedRoot $repoRoot
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    & $DotNetPath publish $projectPath `
        -c $Configuration `
        -r osx-arm64 `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishRoot
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

$publishedExecutable = Join-Path $publishRoot $executableName
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable is missing: $publishedExecutable"
}
if (-not (Test-Arm64MachO -Path $publishedExecutable)) {
    throw 'Published executable is not a thin ARM64 Mach-O binary.'
}
foreach ($nativeFile in Get-ChildItem -LiteralPath $publishRoot -Recurse -File) {
    if (-not (Test-MachOFile -Path $nativeFile.FullName)) {
        continue
    }
    if (-not (Test-Arm64MachO -Path $nativeFile.FullName)) {
        $converted = Convert-FatMachOToArm64 -Path $nativeFile.FullName
        if (-not $converted -or -not (Test-Arm64MachO -Path $nativeFile.FullName)) {
            throw "Published native file is not ARM64: $($nativeFile.FullName)"
        }
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Remove-ScopedDirectory -Path $appRoot -AllowedRoot $outputRoot
New-Item -ItemType Directory -Path $macOsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resourcesRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishRoot '*') -Destination $macOsRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $packagingRoot 'Info.plist') -Destination $contentsRoot
Copy-Item -LiteralPath (Join-Path $packagingRoot 'PkgInfo') -Destination $contentsRoot
New-IcnsFile -PngPath $iconSource -DestinationPath (Join-Path $resourcesRoot 'AppIcon.icns')

$signed = $false
if ($IsMacOS) {
    & chmod +x (Join-Path $macOsRoot $executableName)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to mark the launcher executable as executable.'
    }
    & codesign --force --deep --sign - $appRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Ad-hoc code signing failed.'
    }
    & codesign --verify --deep --strict $appRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Ad-hoc signature verification failed.'
    }
    $signed = $true
}

$suffix = if ($signed) { 'adhoc' } else { 'unsigned' }
$archivePath = Join-Path $outputRoot "Hechao-Launcher-macOS-M4-v$Version-$suffix.zip"
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
New-UnixModeZip -SourceDirectory $appRoot -DestinationPath $archivePath
Set-ZipUnixHostPlatform -Path $archivePath
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$archivePath.sha256"
[IO.File]::WriteAllText(
    $checksumPath,
    "$hash  $([IO.Path]::GetFileName($archivePath))`n",
    [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    App = $appRoot
    Archive = $archivePath
    Sha256 = $hash
    AdHocSigned = $signed
    RuntimeIdentifier = 'osx-arm64'
}
