[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceDirectory,

    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or later is required. Run this script with pwsh."
}

$maximumEntries = 50000
$maximumExpandedBytes = 20L * 1024 * 1024 * 1024
$maximumEntryBytes = 4L * 1024 * 1024 * 1024
$maximumPathLength = 400
$supportedLoaders = @("Vanilla", "Paper", "NeoForge", "Fabric", "Forge")
$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string] $Message)
    [void] $errors.Add($Message)
}

function Add-ValidationWarning {
    param([Parameter(Mandatory = $true)][string] $Message)
    [void] $warnings.Add($Message)
}

function Test-HasControlCharacter {
    param([Parameter(Mandatory = $true)][string] $Value)

    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsControl($character)) {
            return $true
        }
    }
    return $false
}

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $Path
    )

    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace("\", "/")
}

function Read-StrictJsonHashtable {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $file = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $file -or $file.PSIsContainer) {
        Add-ValidationError "$Description is missing: $Path"
        return $null
    }

    if ($file.Length -le 0 -or $file.Length -gt 1MB) {
        Add-ValidationError "$Description must be between 1 byte and 1 MiB: $Path"
        return $null
    }

    try {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $options = [System.Text.Json.JsonDocumentOptions]::new()
        $options.AllowTrailingCommas = $false
        $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
        $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
        $document.Dispose()
        return $text | ConvertFrom-Json -AsHashtable
    }
    catch {
        Add-ValidationError "$Description is not strict JSON: $($_.Exception.Message)"
        return $null
    }
}

function Test-JsonShape {
    param(
        [Parameter(Mandatory = $true)][hashtable] $Value,
        [Parameter(Mandatory = $true)][string[]] $RequiredProperties,
        [Parameter(Mandatory = $true)][string] $Description
    )

    foreach ($property in $RequiredProperties) {
        if (-not $Value.ContainsKey($property)) {
            Add-ValidationError "$Description is missing property '$property'."
        }
    }

    foreach ($property in $Value.Keys) {
        if ($property -notin $RequiredProperties) {
            Add-ValidationError "$Description contains unsupported property '$property'."
        }
    }
}

function Test-RequiredString {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory = $true)][string] $Description,
        [int] $MaximumLength = 160
    )

    if ($Value -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string] $Value) -or
        ([string] $Value).Length -gt $MaximumLength -or
        (Test-HasControlCharacter ([string] $Value))) {
        Add-ValidationError "$Description must be non-empty text no longer than $MaximumLength characters."
        return $false
    }

    return $true
}

function Test-SafeArchivePath {
    param([Parameter(Mandatory = $true)][string] $RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        $RelativePath.Length -gt $maximumPathLength -or
        $RelativePath.StartsWith("/") -or
        $RelativePath.Contains("\") -or
        $RelativePath.Contains([char] 0)) {
        return $false
    }

    foreach ($segment in $RelativePath.Split('/')) {
        if ([string]::IsNullOrEmpty($segment) -or
            $segment -in @(".", "..") -or
            $segment.EndsWith(" ") -or
            $segment.EndsWith(".") -or
            $segment.IndexOfAny([char[]] '<>:"|?*') -ge 0 -or
            (Test-HasControlCharacter $segment)) {
            return $false
        }

        $deviceName = $segment.Split('.')[0]
        if ($deviceName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
            return $false
        }
    }

    return $true
}

function Get-FirstFile {
    param([Parameter(Mandatory = $true)][string] $Directory)

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $Directory -File -Recurse -Force |
        Select-Object -First 1
}

function Get-MemoryMiB {
    param([Parameter(Mandatory = $true)][System.Text.RegularExpressions.Match] $Match)

    $number = [long]::Parse(
        $Match.Groups['value'].Value,
        [System.Globalization.CultureInfo]::InvariantCulture)
    switch ($Match.Groups['unit'].Value.ToUpperInvariant()) {
        'K' { return $number % 1024 -eq 0 ? $number / 1024 : 0 }
        'M' { return $number }
        'G' { return $number * 1024 }
        'T' { return $number * 1024 * 1024 }
        default { return 0 }
    }
}

$source = Get-Item -LiteralPath $SourceDirectory -ErrorAction SilentlyContinue
if ($null -eq $source -or -not $source.PSIsContainer) {
    throw "Source directory does not exist: $SourceDirectory"
}

$sourceRoot = [System.IO.Path]::GetFullPath($source.FullName).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (($source.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Source directory cannot be a symbolic link or reparse point: $sourceRoot"
}

$allowedRootEntries = @("hechao-pack.json", "client", "server", "shared")
foreach ($entry in @(Get-ChildItem -LiteralPath $sourceRoot -Force)) {
    if ($entry.Name -notin $allowedRootEntries) {
        Add-ValidationError "Unexpected root entry '$($entry.Name)'. Only hechao-pack.json, client, server, and optional shared are allowed."
    }
}

$descriptorPath = Join-Path $sourceRoot "hechao-pack.json"
$clientRoot = Join-Path $sourceRoot "client"
$serverRoot = Join-Path $sourceRoot "server"
$sharedRoot = Join-Path $sourceRoot "shared"

if (-not (Test-Path -LiteralPath $clientRoot -PathType Container)) {
    Add-ValidationError "The client directory is missing."
}
if (-not (Test-Path -LiteralPath $serverRoot -PathType Container)) {
    Add-ValidationError "The server directory is missing."
}

$allEntries = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force)
foreach ($entry in $allEntries) {
    if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-ValidationError "Symbolic links and reparse points are not allowed: $(Get-NormalizedRelativePath $sourceRoot $entry.FullName)"
    }
}

$files = @($allEntries | Where-Object { -not $_.PSIsContainer })
if ($files.Count -gt $maximumEntries) {
    Add-ValidationError "The source contains $($files.Count) files; the limit is $maximumEntries."
}

$pathSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
$records = [System.Collections.Generic.List[object]]::new()
$expandedBytes = 0L
$forbiddenExactNames = @(
    ".env", ".env.local", ".env.production", "forwarding.secret",
    "launcher_accounts.json", "launcher_profiles.json", "servers.dat",
    "usercache.json", "usernamecache.json", "command_history.txt",
    "id_rsa", "id_ed25519"
)
$forbiddenExtensions = @(
    ".dpapi", ".jks", ".key", ".keystore", ".p12", ".pem", ".pfx"
)
$placeholderPattern = '(?i)(<[^>]+>|REPLACE[_ -]?ME|TEMPLATE[_ -]?ONLY|DO[_ -]?NOT[_ -]?UPLOAD|\.example$)'

foreach ($file in ($files | Sort-Object FullName)) {
    $relativePath = Get-NormalizedRelativePath $sourceRoot $file.FullName
    if (-not (Test-SafeArchivePath $relativePath)) {
        Add-ValidationError "Unsafe archive path: $relativePath"
    }
    if (-not $pathSet.Add($relativePath)) {
        Add-ValidationError "Case-insensitive path collision: $relativePath"
    }
    if ($relativePath -match $placeholderPattern) {
        Add-ValidationError "Template or placeholder file cannot enter the upload package: $relativePath"
    }
    if ($file.Length -gt $maximumEntryBytes) {
        Add-ValidationError "File exceeds the 4 GiB entry limit: $relativePath"
    }

    $expandedBytes += [long] $file.Length
    $fileName = $file.Name.ToLowerInvariant()
    if ($fileName -in $forbiddenExactNames -or
        $file.Extension.ToLowerInvariant() -in $forbiddenExtensions) {
        Add-ValidationError "Secret, account, or private-key file is forbidden: $relativePath"
    }

    $normalizedLower = $relativePath.ToLowerInvariant()
    if ($normalizedLower -match '^client/(?:[^/]+/)*(?:saves|logs|crash-reports|screenshots|debug|downloads|natives|runtime|pcl)(?:/|$)' -or
        $normalizedLower -match '^client/(?:[^/]+/)*(?:\.minecraft|\.git|\.hechao)(?:/|$)' -or
        $normalizedLower -eq 'client/.hechao-install.json') {
        Add-ValidationError "Client runtime, account, or writable player data is forbidden: $relativePath"
    }
    if ($normalizedLower -match '^server/(?:logs|crash-reports|backups|cache)(?:/|$)' -or
        $normalizedLower -match '^server/(?:\.git|\.hechao)(?:/|$)') {
        Add-ValidationError "Server runtime, backup, or repository data is forbidden: $relativePath"
    }

    $side = if ($normalizedLower.StartsWith("client/")) {
        "Client"
    }
    elseif ($normalizedLower.StartsWith("server/")) {
        "Server"
    }
    elseif ($normalizedLower.StartsWith("shared/")) {
        "Shared"
    }
    else {
        "Metadata"
    }
    $digest = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [void] $records.Add([pscustomobject] [ordered]@{
        path = $relativePath
        side = $side
        size = [long] $file.Length
        sha256 = $digest
    })
}

if ($expandedBytes -gt $maximumExpandedBytes) {
    Add-ValidationError "The source expands to $expandedBytes bytes; the limit is $maximumExpandedBytes."
}

$descriptor = Read-StrictJsonHashtable $descriptorPath "hechao-pack.json"
$descriptorProperties = @(
    "schemaVersion", "id", "displayName", "version", "minecraftVersion",
    "javaMajorVersion", "loader", "loaderVersion", "clientRoot", "serverRoot",
    "sharedRoot"
)
if ($null -ne $descriptor) {
    Test-JsonShape $descriptor $descriptorProperties "hechao-pack.json"
    if ($descriptor['schemaVersion'] -ne 1) {
        Add-ValidationError "hechao-pack.json schemaVersion must be 1."
    }
    if (Test-RequiredString $descriptor['id'] "hechao-pack.json id" 64) {
        if ([string] $descriptor['id'] -notmatch '^[a-z0-9][a-z0-9._-]{1,63}$') {
            Add-ValidationError "hechao-pack.json id must use 2-64 lowercase letters, digits, dots, underscores, or hyphens."
        }
    }
    if (Test-RequiredString $descriptor['displayName'] "hechao-pack.json displayName" 80) {
        if ([string] $descriptor['displayName'] -match '(?i)(<[^>]+>|REPLACE[_ -]?ME|TEMPLATE[_ -]?ONLY|示例活动)') {
            Add-ValidationError "hechao-pack.json displayName still contains a template value."
        }
    }
    if (Test-RequiredString $descriptor['version'] "hechao-pack.json version" 40) {
        if ([string] $descriptor['version'] -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$') {
            Add-ValidationError "hechao-pack.json version must use major.minor.patch SemVer format."
        }
    }
    [void] (Test-RequiredString $descriptor['minecraftVersion'] "hechao-pack.json minecraftVersion" 40)
    [void] (Test-RequiredString $descriptor['loaderVersion'] "hechao-pack.json loaderVersion" 80)
    if ($descriptor['javaMajorVersion'] -isnot [long] -and
        $descriptor['javaMajorVersion'] -isnot [int]) {
        Add-ValidationError "hechao-pack.json javaMajorVersion must be an integer."
    }
    elseif ([long] $descriptor['javaMajorVersion'] -lt 8 -or
            [long] $descriptor['javaMajorVersion'] -gt 30) {
        Add-ValidationError "hechao-pack.json javaMajorVersion must be between 8 and 30."
    }
    if ([string] $descriptor['loader'] -notin $supportedLoaders) {
        Add-ValidationError "hechao-pack.json loader must be one of: $($supportedLoaders -join ', ')."
    }
    foreach ($rootName in @("clientRoot", "serverRoot", "sharedRoot")) {
        $expected = $rootName.Substring(0, $rootName.Length - 4).ToLowerInvariant()
        if ([string] $descriptor[$rootName] -cne $expected) {
            Add-ValidationError "hechao-pack.json $rootName must be '$expected' in the standard layout."
        }
    }
}

$clientMetadataPath = Join-Path $clientRoot "hechao-profile.json"
$clientMetadata = Read-StrictJsonHashtable $clientMetadataPath "client/hechao-profile.json"
$versionId = $null
if ($null -ne $clientMetadata) {
    Test-JsonShape $clientMetadata @("schemaVersion", "versionId", "javaMajorVersion") "client/hechao-profile.json"
    if ($clientMetadata['schemaVersion'] -ne 1) {
        Add-ValidationError "client/hechao-profile.json schemaVersion must be 1."
    }
    if (Test-RequiredString $clientMetadata['versionId'] "client/hechao-profile.json versionId" 160) {
        $versionId = [string] $clientMetadata['versionId']
        if ($versionId -in @(".", "..") -or
            $versionId.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $versionId.EndsWith(" ") -or
            $versionId.EndsWith(".")) {
            Add-ValidationError "client/hechao-profile.json versionId is not a safe directory name."
            $versionId = $null
        }
    }
    if ($clientMetadata['javaMajorVersion'] -isnot [long] -and
        $clientMetadata['javaMajorVersion'] -isnot [int]) {
        Add-ValidationError "client/hechao-profile.json javaMajorVersion must be an integer."
    }
    elseif ([long] $clientMetadata['javaMajorVersion'] -lt 8 -or
            [long] $clientMetadata['javaMajorVersion'] -gt 99) {
        Add-ValidationError "client/hechao-profile.json javaMajorVersion must be between 8 and 99."
    }
    if ($null -ne $descriptor -and
        $clientMetadata.ContainsKey('javaMajorVersion') -and
        [long] $clientMetadata['javaMajorVersion'] -ne [long] $descriptor['javaMajorVersion']) {
        Add-ValidationError "Client and package javaMajorVersion values do not match."
    }
}

if (Test-Path -LiteralPath (Join-Path $clientRoot ".minecraft")) {
    Add-ValidationError "client must contain .minecraft contents directly; remove the nested client/.minecraft directory."
}
if ($null -eq (Get-FirstFile (Join-Path $clientRoot "assets\indexes"))) {
    Add-ValidationError "client/assets/indexes is missing or empty."
}
if ($null -eq (Get-FirstFile (Join-Path $clientRoot "assets\objects"))) {
    Add-ValidationError "client/assets/objects is missing or empty."
}
if ($null -eq (Get-FirstFile (Join-Path $clientRoot "libraries"))) {
    Add-ValidationError "client/libraries is missing or empty."
}

if ($null -ne $versionId) {
    $versionDirectory = Join-Path $clientRoot ("versions\" + $versionId)
    $versionJsonPath = Join-Path $versionDirectory ($versionId + ".json")
    $versionJarPath = Join-Path $versionDirectory ($versionId + ".jar")
    $versionJson = Read-StrictJsonHashtable $versionJsonPath "client version JSON"
    if (-not (Test-Path -LiteralPath $versionJarPath -PathType Leaf) -or
        (Get-Item -LiteralPath $versionJarPath -ErrorAction SilentlyContinue).Length -le 0) {
        Add-ValidationError "Client version JAR is missing or empty: versions/$versionId/$versionId.jar"
    }
    if ($null -ne $versionJson) {
        if ([string] $versionJson['id'] -cne $versionId) {
            Add-ValidationError "Client version JSON id does not match hechao-profile.json versionId."
        }
        $javaVersion = $versionJson['javaVersion']
        $versionJava = $null
        if ($javaVersion -is [hashtable]) {
            $versionJava = $javaVersion['majorVersion']
        }
        if ($null -eq $versionJava -or
            [long] $versionJava -ne [long] $clientMetadata['javaMajorVersion']) {
            Add-ValidationError "Client version JSON javaVersion.majorVersion does not match hechao-profile.json."
        }
    }
}

$serverRequiredFiles = @(
    "server.properties", "eula.txt", "user_jvm_args.txt", "start.bat"
)
foreach ($requiredFile in $serverRequiredFiles) {
    $requiredPath = Join-Path $serverRoot $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or
        (Get-Item -LiteralPath $requiredPath -ErrorAction SilentlyContinue).Length -le 0) {
        Add-ValidationError "Required server file is missing or empty: server/$requiredFile"
    }
}

$propertiesPath = Join-Path $serverRoot "server.properties"
if (Test-Path -LiteralPath $propertiesPath -PathType Leaf) {
    $properties = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($propertiesPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#") -or -not $trimmed.Contains("=")) {
            continue
        }
        $separator = $trimmed.IndexOf("=")
        $properties[$trimmed.Substring(0, $separator).Trim().ToLowerInvariant()] =
            $trimmed.Substring($separator + 1).Trim()
    }
    $expectedProperties = [ordered]@{
        "server-ip" = "127.0.0.1"
        "server-port" = "25568"
        "online-mode" = "false"
    }
    foreach ($pair in $expectedProperties.GetEnumerator()) {
        if (-not $properties.ContainsKey($pair.Key) -or
            [string] $properties[$pair.Key] -cne [string] $pair.Value) {
            Add-ValidationError "server/server.properties must contain $($pair.Key)=$($pair.Value)."
        }
    }
    $maximumPlayers = 0
    if (-not $properties.ContainsKey("max-players") -or
        -not [int]::TryParse([string] $properties['max-players'], [ref] $maximumPlayers) -or
        $maximumPlayers -lt 1 -or $maximumPlayers -gt 1000) {
        Add-ValidationError "server/server.properties max-players must be an integer from 1 to 1000."
    }
    if ($properties.ContainsKey("rcon.password") -and
        -not [string]::IsNullOrWhiteSpace([string] $properties['rcon.password'])) {
        Add-ValidationError "server/server.properties must not contain an RCON password."
    }
}

$eulaPath = Join-Path $serverRoot "eula.txt"
if (Test-Path -LiteralPath $eulaPath -PathType Leaf) {
    $accepted = [System.IO.File]::ReadAllLines($eulaPath) |
        Where-Object { $_.Trim() -match '^(?i:eula\s*=\s*true)$' }
    if (@($accepted).Count -ne 1) {
        Add-ValidationError "server/eula.txt must contain exactly one eula=true line."
    }
}

$memoryPath = Join-Path $serverRoot "user_jvm_args.txt"
if (Test-Path -LiteralPath $memoryPath -PathType Leaf) {
    $memoryText = [System.IO.File]::ReadAllText($memoryPath)
    $xmsMatches = [regex]::Matches($memoryText, '(?i)(?<!\S)-Xms(?<value>\d+)(?<unit>[KMGT])\b')
    $xmxMatches = [regex]::Matches($memoryText, '(?i)(?<!\S)-Xmx(?<value>\d+)(?<unit>[KMGT])\b')
    if ($xmsMatches.Count -ne 1 -or $xmxMatches.Count -ne 1) {
        Add-ValidationError "server/user_jvm_args.txt must contain exactly one -Xms and one -Xmx argument."
    }
    else {
        $xmsMiB = Get-MemoryMiB $xmsMatches[0]
        $xmxMiB = Get-MemoryMiB $xmxMatches[0]
        if ($xmsMiB -lt 512 -or $xmxMiB -lt 512 -or
            $xmsMiB -gt 65536 -or $xmxMiB -gt 65536 -or
            $xmsMiB % 256 -ne 0 -or $xmxMiB % 256 -ne 0 -or
            $xmsMiB -gt $xmxMiB) {
            Add-ValidationError "JVM memory must be 512-65536 MiB, aligned to 256 MiB, with Xms <= Xmx."
        }
    }
}

$startPath = Join-Path $serverRoot "start.bat"
$startText = ""
if (Test-Path -LiteralPath $startPath -PathType Leaf) {
    $startFile = Get-Item -LiteralPath $startPath
    $startText = [System.Text.Encoding]::Latin1.GetString(
        [System.IO.File]::ReadAllBytes($startPath))
    if ($startFile.Length -gt 1MB) {
        Add-ValidationError "server/start.bat cannot exceed 1 MiB."
    }
    if ($startText -notmatch '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*\r?$') {
        Add-ValidationError "server/start.bat is missing the exact HECHAO_MANAGED_START guard line."
    }
    if ($startText -notmatch '(?i)user_jvm_args\.txt') {
        Add-ValidationError "server/start.bat must reference user_jvm_args.txt."
    }
    if ($startText -notmatch '(?i)(?:^|[\s\"])(?:java|java\.exe)(?:[\s\"]|$)') {
        Add-ValidationError "server/start.bat must invoke Java explicitly."
    }
    if ($startText -match '(?i)(?:[A-Z]:\\|\\\\)' -or
        $startText -match '(?i)-Xm[sx]\d') {
        Add-ValidationError "server/start.bat must use relative paths and keep memory arguments only in user_jvm_args.txt."
    }
}

$serverJars = @(Get-ChildItem -LiteralPath $serverRoot -Filter "*.jar" -File -Recurse -Force -ErrorAction SilentlyContinue)
if ($serverJars.Count -eq 0) {
    Add-ValidationError "server does not contain a server core, loader, plugin, or mod JAR."
}

if ($null -ne $descriptor) {
    switch ([string] $descriptor['loader']) {
        "Fabric" {
            if (-not (Test-Path -LiteralPath (Join-Path $serverRoot "fabric-server-launch.jar") -PathType Leaf)) {
                Add-ValidationError "Fabric packages must include server/fabric-server-launch.jar."
            }
        }
        "NeoForge" {
            if ($null -eq (Get-FirstFile (Join-Path $serverRoot "libraries\net\neoforged\neoforge")) -or
                $startText -notmatch '(?i)win_args\.txt') {
                Add-ValidationError "NeoForge packages must include the NeoForge libraries tree and start it through win_args.txt."
            }
        }
        "Forge" {
            if ($null -eq (Get-FirstFile (Join-Path $serverRoot "libraries\net\minecraftforge\forge")) -or
                $startText -notmatch '(?i)win_args\.txt') {
                Add-ValidationError "Forge packages must include the Forge libraries tree and start it through win_args.txt."
            }
        }
        "Paper" {
            if (@(Get-ChildItem -LiteralPath $serverRoot -Filter "paper-*.jar" -File -ErrorAction SilentlyContinue).Count -ne 1) {
                Add-ValidationError "Paper packages must include exactly one root paper-*.jar core."
            }
        }
        "Vanilla" {
            $vanillaJars = @(Get-ChildItem -LiteralPath $serverRoot -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -eq "server.jar" -or $_.Name -like "minecraft_server*.jar" })
            if ($vanillaJars.Count -ne 1) {
                Add-ValidationError "Vanilla packages must include exactly one root server.jar or minecraft_server*.jar."
            }
        }
    }
}

$clientTargets = @{}
$serverTargets = @{}
$sharedRecords = @($records | Where-Object side -eq "Shared")
foreach ($record in @($records | Where-Object side -eq "Client")) {
    $clientTargets[$record.path.Substring("client/".Length).ToLowerInvariant()] = $record.path
}
foreach ($record in @($records | Where-Object side -eq "Server")) {
    $serverTargets[$record.path.Substring("server/".Length).ToLowerInvariant()] = $record.path
}
foreach ($record in $sharedRecords) {
    $target = $record.path.Substring("shared/".Length).ToLowerInvariant()
    $firstSegment = $target.Split('/')[0]
    if ($firstSegment -notin @(
            "mods", "config", "defaultconfigs", "kubejs", "scripts",
            "resourcepacks", "datapacks")) {
        Add-ValidationError "shared files must use a recognized shared root: $($record.path)"
    }
    if ($clientTargets.ContainsKey($target)) {
        Add-ValidationError "shared/client target collision: $($record.path) and $($clientTargets[$target])"
    }
    if ($serverTargets.ContainsKey($target)) {
        Add-ValidationError "shared/server target collision: $($record.path) and $($serverTargets[$target])"
    }
}

$clientModJars = @($records | Where-Object {
    $_.path -match '^(?i:client/mods/)[^/]+\.jar$'
})
$serverModJars = @($records | Where-Object {
    $_.path -match '^(?i:server/mods/)[^/]+\.jar$'
})
$commonJars = [System.Collections.Generic.List[object]]::new()
foreach ($clientJar in $clientModJars) {
    $name = [System.IO.Path]::GetFileName($clientJar.path)
    foreach ($serverJar in @($serverModJars | Where-Object {
                [System.IO.Path]::GetFileName($_.path) -ieq $name
            })) {
        if ($clientJar.sha256 -cne $serverJar.sha256) {
            Add-ValidationError "Client/server mod '$name' has different SHA-256 values. Build or copy one approved common JAR."
        }
        else {
            [void] $commonJars.Add([pscustomobject] [ordered]@{
                fileName = $name
                sha256 = $clientJar.sha256
                placement = "duplicated"
            })
        }
    }
}
foreach ($sharedJar in @($sharedRecords | Where-Object {
            $_.path -match '^(?i:shared/mods/)[^/]+\.jar$'
        })) {
    [void] $commonJars.Add([pscustomobject] [ordered]@{
        fileName = [System.IO.Path]::GetFileName($sharedJar.path)
        sha256 = $sharedJar.sha256
        placement = "shared"
    })
}

if (@($records | Where-Object { $_.path -match '^(?i:server/world(?:_nether|_the_end)?/)' }).Count -gt 0) {
    Add-ValidationWarning "The package contains world data. Record its source, license, cleanup, and rollback backup."
}
if ($sharedRecords.Count -gt 0) {
    Add-ValidationWarning "Every shared file will be copied to both client and server; verify both loaders accept it."
}
if ($commonJars.Count -eq 0) {
    Add-ValidationWarning "No common client/server JAR was detected. Confirm this activity intentionally has no shared activity code."
}

if ($errors.Count -gt 0) {
    $summary = ($errors | Select-Object -First 40 | ForEach-Object { "- $_" }) -join [Environment]::NewLine
    if ($errors.Count -gt 40) {
        $summary += [Environment]::NewLine + "- ... and $($errors.Count - 40) more error(s)."
    }
    throw "Hechao package source validation failed with $($errors.Count) error(s):$([Environment]::NewLine)$summary"
}

$clientRecords = @($records | Where-Object side -eq "Client")
$serverRecords = @($records | Where-Object side -eq "Server")
$metadataRecords = @($records | Where-Object side -eq "Metadata")
$report = [pscustomobject] [ordered]@{
    schemaVersion = 1
    reportKind = "hechao-package-import-source-validation"
    validatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    package = [pscustomobject] [ordered]@{
        id = [string] $descriptor['id']
        displayName = [string] $descriptor['displayName']
        version = [string] $descriptor['version']
        minecraftVersion = [string] $descriptor['minecraftVersion']
        javaMajorVersion = [int] $descriptor['javaMajorVersion']
        loader = [string] $descriptor['loader']
        loaderVersion = [string] $descriptor['loaderVersion']
        clientVersionId = [string] $clientMetadata['versionId']
    }
    totals = [pscustomobject] [ordered]@{
        fileCount = $records.Count
        expandedBytes = $expandedBytes
        clientFileCount = $clientRecords.Count
        clientBytes = [long] (($clientRecords | Measure-Object size -Sum).Sum ?? 0)
        serverFileCount = $serverRecords.Count
        serverBytes = [long] (($serverRecords | Measure-Object size -Sum).Sum ?? 0)
        sharedFileCount = $sharedRecords.Count
        sharedBytes = [long] (($sharedRecords | Measure-Object size -Sum).Sum ?? 0)
        metadataFileCount = $metadataRecords.Count
    }
    commonJars = @($commonJars)
    warnings = @($warnings)
    files = @($records | Sort-Object path)
}

if ($PassThru) {
    return $report
}

Write-Output "Hechao package source validation passed."
Write-Output "Package: $($report.package.id) $($report.package.version)"
Write-Output "Runtime: Minecraft $($report.package.minecraftVersion), $($report.package.loader) $($report.package.loaderVersion), Java $($report.package.javaMajorVersion)"
Write-Output "Files: $($report.totals.fileCount); bytes: $($report.totals.expandedBytes)"
Write-Output "Client: $($report.totals.clientFileCount); server: $($report.totals.serverFileCount); shared: $($report.totals.sharedFileCount)"
foreach ($warning in $warnings) {
    Write-Warning $warning
}
