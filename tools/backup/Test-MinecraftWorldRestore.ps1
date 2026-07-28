[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RestoreRoot,

    [string]$OutputPath,

    [ValidateRange(1, 1000)]
    [int]$RegionSampleModulo = 1,

    [string[]]$AllowedEmptyAuxiliaryRegionRelativePaths = @()
)

$ErrorActionPreference = 'Stop'
$sectorBytes = 4096
$regionHeaderBytes = 8192

function Read-Exactly {
    param(
        [System.IO.Stream]$Stream,
        [byte[]]$Buffer,
        [int]$Count
    )

    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($Buffer, $offset, $Count - $offset)
        if ($read -eq 0) {
            return $false
        }
        $offset += $read
    }
    return $true
}

function Read-BigEndianInt32 {
    param(
        [byte[]]$Buffer,
        [int]$Offset
    )

    return [int](
        ([uint32]$Buffer[$Offset] -shl 24) -bor
        ([uint32]$Buffer[$Offset + 1] -shl 16) -bor
        ([uint32]$Buffer[$Offset + 2] -shl 8) -bor
        [uint32]$Buffer[$Offset + 3]
    )
}

function Test-LevelDat {
    param([System.IO.FileInfo]$File)

    $stream = [System.IO.File]::OpenRead($File.FullName)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $stream,
            [System.IO.Compression.CompressionMode]::Decompress,
            $true)
        try {
            $rootTag = $gzip.ReadByte()
            if ($rootTag -ne 10) {
                throw "NBT root tag is $rootTag instead of TAG_Compound."
            }
            $nameLengthBytes = [byte[]]::new(2)
            if (-not (Read-Exactly -Stream $gzip -Buffer $nameLengthBytes -Count 2)) {
                throw 'NBT root name length is truncated.'
            }
            $nameLength = ([int]$nameLengthBytes[0] -shl 8) -bor $nameLengthBytes[1]
            if ($nameLength -gt 0) {
                $nameBytes = [byte[]]::new($nameLength)
                if (-not (Read-Exactly -Stream $gzip -Buffer $nameBytes -Count $nameLength)) {
                    throw 'NBT root name is truncated.'
                }
            }
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return [pscustomobject]@{
        Path = $File.FullName
        Bytes = $File.Length
        RootTag = 'TAG_Compound'
        Valid = $true
    }
}

function Test-RegionFile {
    param(
        [System.IO.FileInfo]$File,

        [switch]$AllowEmptyAuxiliaryPlaceholder
    )

    if ($File.Length -lt $regionHeaderBytes) {
        if ($File.Length -eq 0 -and
            $AllowEmptyAuxiliaryPlaceholder -and
            $File.Directory.Name -in @('entities', 'poi')) {
            return [pscustomobject]@{
                Path = $File.FullName
                Bytes = 0
                Chunks = 0
                ExternalChunks = 0
                EmptyAuxiliaryPlaceholder = $true
                Valid = $true
            }
        }
        throw "Region file is shorter than $regionHeaderBytes bytes."
    }

    $stream = [System.IO.File]::OpenRead($File.FullName)
    try {
        $header = [byte[]]::new($regionHeaderBytes)
        if (-not (Read-Exactly -Stream $stream -Buffer $header -Count $regionHeaderBytes)) {
            throw 'Region header is truncated.'
        }

        $totalSectors = [long][Math]::Ceiling($File.Length / [double]$sectorBytes)
        $claimedSectors = [System.Collections.Generic.HashSet[long]]::new()
        [void]$claimedSectors.Add(0)
        [void]$claimedSectors.Add(1)
        $chunks = 0
        $externalChunks = 0

        for ($index = 0; $index -lt 1024; $index++) {
            $entryOffset = $index * 4
            $sectorOffset =
                ([int]$header[$entryOffset] -shl 16) -bor
                ([int]$header[$entryOffset + 1] -shl 8) -bor
                [int]$header[$entryOffset + 2]
            $sectorCount = [int]$header[$entryOffset + 3]
            if ($sectorOffset -eq 0 -and $sectorCount -eq 0) {
                continue
            }
            if ($sectorOffset -lt 2 -or $sectorCount -lt 1) {
                throw "Chunk index $index has an invalid location entry."
            }
            if ([long]$sectorOffset + $sectorCount -gt $totalSectors) {
                throw "Chunk index $index points beyond the region file."
            }

            for ($sector = $sectorOffset; $sector -lt $sectorOffset + $sectorCount; $sector++) {
                if (-not $claimedSectors.Add([long]$sector)) {
                    throw "Chunk index $index overlaps claimed sector $sector."
                }
            }

            $stream.Position = [long]$sectorOffset * $sectorBytes
            $chunkHeader = [byte[]]::new(5)
            if (-not (Read-Exactly -Stream $stream -Buffer $chunkHeader -Count 5)) {
                throw "Chunk index $index header is truncated."
            }
            $chunkLength = Read-BigEndianInt32 -Buffer $chunkHeader -Offset 0
            if ($chunkLength -lt 1 -or
                $chunkLength -gt ($sectorCount * $sectorBytes) - 4) {
                throw "Chunk index $index has invalid length $chunkLength."
            }
            if ([long]$sectorOffset * $sectorBytes + 4 + $chunkLength -gt $File.Length) {
                throw "Chunk index $index data is truncated."
            }

            $compression = [int]$chunkHeader[4]
            $isExternal = ($compression -band 0x80) -ne 0
            $compressionType = $compression -band 0x7f
            if ($compressionType -lt 1 -or $compressionType -gt 4) {
                throw "Chunk index $index has unsupported compression type $compressionType."
            }
            if ($isExternal) {
                $externalChunks++
            }
            $chunks++
        }

        return [pscustomobject]@{
            Path = $File.FullName
            Bytes = $File.Length
            Chunks = $chunks
            ExternalChunks = $externalChunks
            EmptyAuxiliaryPlaceholder = $false
            Valid = $true
        }
    }
    finally {
        $stream.Dispose()
    }
}

$resolvedRoot = (Resolve-Path -LiteralPath $RestoreRoot).Path
$allowedEmptyAuxiliaryRegions =
    [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
$usedAllowedEmptyAuxiliaryRegions =
    [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in $AllowedEmptyAuxiliaryRegionRelativePaths) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        [System.IO.Path]::IsPathRooted($relativePath)) {
        throw "Allowed empty auxiliary region path is invalid: $relativePath"
    }

    $candidatePath = [System.IO.Path]::GetFullPath(
        (Join-Path $resolvedRoot ($relativePath -replace '/', '\')))
    $rootPrefix = $resolvedRoot.TrimEnd('\') + '\'
    if (-not $candidatePath.StartsWith(
            $rootPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Allowed empty auxiliary region path escapes restore root: $relativePath"
    }

    $normalizedRelativePath = $candidatePath.Substring($rootPrefix.Length)
    if ([System.IO.Path]::GetExtension($normalizedRelativePath) -ine '.mca') {
        throw "Allowed empty auxiliary region is not an .mca file: $relativePath"
    }
    if (-not $allowedEmptyAuxiliaryRegions.Add($normalizedRelativePath)) {
        throw "Allowed empty auxiliary region is duplicated: $relativePath"
    }
}

$worldResults = foreach ($worldDirectory in Get-ChildItem -LiteralPath $resolvedRoot -Directory) {
    $files = @(Get-ChildItem -LiteralPath $worldDirectory.FullName -File -Recurse)
    $levelFiles = @($files | Where-Object Name -eq 'level.dat')
    $regionFiles = @(
        $files |
            Where-Object Extension -eq '.mca' |
            Sort-Object FullName
    )
    $regionFilesToValidate = @(
        for ($index = 0; $index -lt $regionFiles.Count; $index++) {
            if ($index % $RegionSampleModulo -eq 0) {
                $regionFiles[$index]
            }
        }
    )
    $issues = [System.Collections.Generic.List[string]]::new()
    $validLevels = [System.Collections.Generic.List[object]]::new()
    $validRegions = [System.Collections.Generic.List[object]]::new()

    foreach ($levelFile in $levelFiles) {
        try {
            $validLevels.Add((Test-LevelDat -File $levelFile))
        }
        catch {
            $issues.Add("$($levelFile.FullName): $($_.Exception.Message)")
        }
    }
    foreach ($regionFile in $regionFilesToValidate) {
        try {
            $relativeRegionPath = $regionFile.FullName.Substring(
                $resolvedRoot.TrimEnd('\').Length + 1)
            $allowEmptyAuxiliaryPlaceholder =
                $allowedEmptyAuxiliaryRegions.Contains($relativeRegionPath)
            $regionResult = Test-RegionFile `
                -File $regionFile `
                -AllowEmptyAuxiliaryPlaceholder:$allowEmptyAuxiliaryPlaceholder
            $validRegions.Add($regionResult)
            if ($regionResult.EmptyAuxiliaryPlaceholder) {
                [void]$usedAllowedEmptyAuxiliaryRegions.Add($relativeRegionPath)
            }
        }
        catch {
            $issues.Add("$($regionFile.FullName): $($_.Exception.Message)")
        }
    }

    [pscustomobject]@{
        World = $worldDirectory.Name
        Root = $worldDirectory.FullName
        FileCount = $files.Count
        TotalBytes = ($files | Measure-Object Length -Sum).Sum
        SessionLockCount = @($files | Where-Object Name -eq 'session.lock').Count
        LevelDatCount = $levelFiles.Count
        ValidLevelDatCount = $validLevels.Count
        RegionFileCount = $regionFiles.Count
        RegionSampleModulo = $RegionSampleModulo
        SampledRegionFileCount = $regionFilesToValidate.Count
        ValidRegionFileCount = $validRegions.Count
        EmptyAuxiliaryRegionPlaceholderCount = @(
            $validRegions | Where-Object EmptyAuxiliaryPlaceholder
        ).Count
        ChunkCount = ($validRegions | Measure-Object Chunks -Sum).Sum
        ExternalChunkCount = ($validRegions | Measure-Object ExternalChunks -Sum).Sum
        IssueCount = $issues.Count
        Issues = @($issues)
        Passed = $issues.Count -eq 0 -and
            $levelFiles.Count -gt 0 -and
            $validLevels.Count -eq $levelFiles.Count -and
            $validRegions.Count -eq $regionFilesToValidate.Count -and
            @($files | Where-Object Name -eq 'session.lock').Count -eq 0
    }
}

$unusedAllowedEmptyAuxiliaryRegions = @(
    $allowedEmptyAuxiliaryRegions |
        Where-Object {
            -not $usedAllowedEmptyAuxiliaryRegions.Contains($_)
        } |
        Sort-Object
)
$result = [pscustomobject]@{
    SchemaVersion = 1
    VerifiedAt = (Get-Date).ToUniversalTime().ToString('o')
    RestoreRoot = $resolvedRoot
    AllowedEmptyAuxiliaryRegionRelativePaths = @(
        $allowedEmptyAuxiliaryRegions | Sort-Object
    )
    UsedAllowedEmptyAuxiliaryRegionRelativePaths = @(
        $usedAllowedEmptyAuxiliaryRegions | Sort-Object
    )
    UnusedAllowedEmptyAuxiliaryRegionRelativePaths =
        $unusedAllowedEmptyAuxiliaryRegions
    Worlds = @($worldResults)
    Passed = @($worldResults).Count -gt 0 -and
        @($worldResults | Where-Object { -not $_.Passed }).Count -eq 0 -and
        $unusedAllowedEmptyAuxiliaryRegions.Count -eq 0
}
$json = $result | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}
$json
if (-not $result.Passed) {
    exit 1
}
