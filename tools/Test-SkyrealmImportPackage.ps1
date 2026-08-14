[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Archive,

    [string]$ExpectedPackageVersion = '1.0.9',

    [string]$ExpectedEconomyPluginVersion = '0.1.2',

    [string]$ExpectedEconomyScreenVersion = '0.1.2'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required.'
}

$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$archiveChecksumPath = $archivePath + '.sha256'
$payloadChecksumPath = $archivePath + '.payload.sha256'
$economyPluginPath =
    "server/plugins/HechaoEconomy-$ExpectedEconomyPluginVersion.jar"
$economyScreenFileName =
    "HechaoEconomyScreen-NeoForge-1.21.1-$ExpectedEconomyScreenVersion.jar"
$serverEconomyScreenPath = "server/mods/$economyScreenFileName"
$clientEconomyScreenPath = "client/mods/$economyScreenFileName"
$clientVersionId = '天域远征工业季 1.21.1'
$clientProfilePath = 'client/hechao-profile.json'
$clientVersionJsonPath = "client/versions/$clientVersionId/$clientVersionId.json"
$clientVersionJarPath = "client/versions/$clientVersionId/$clientVersionId.jar"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = [Text.UTF8Encoding]::new($false)

function Read-ZipText {
    param([IO.Compression.ZipArchiveEntry]$Entry)
    $reader = [IO.StreamReader]::new($Entry.Open(), $utf8, $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Read-ZipBytes {
    param([IO.Compression.ZipArchiveEntry]$Entry)
    $memory = [IO.MemoryStream]::new()
    $stream = $Entry.Open()
    try {
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $stream.Dispose()
        $memory.Dispose()
    }
}

function Get-EntryHash {
    param([IO.Compression.ZipArchiveEntry]$Entry)
    $stream = $Entry.Open()
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(1024 * 1024)
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $hasher.AppendData($buffer, 0, $read)
        }
        return [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
        $stream.Dispose()
    }
}

function Read-NestedZipContract {
    param(
        [IO.Compression.ZipArchiveEntry]$Entry,
        [string[]]$RequiredPaths,
        [string[]]$TextPaths
    )
    $memory = [IO.MemoryStream]::new()
    $source = $Entry.Open()
    try {
        $source.CopyTo($memory)
    }
    finally {
        $source.Dispose()
    }
    $memory.Position = 0
    $nested = [IO.Compression.ZipArchive]::new(
        $memory,
        [IO.Compression.ZipArchiveMode]::Read,
        $false,
        $utf8)
    try {
        $result = @{}
        foreach ($path in $RequiredPaths) {
            $nestedEntry = $nested.GetEntry($path)
            if ($null -eq $nestedEntry) {
                throw "Nested archive entry is missing from $($Entry.FullName): $path"
            }
            if ($TextPaths -contains $path) {
                $result[$path] = Read-ZipText $nestedEntry
            }
            else {
                $result[$path] = Read-ZipBytes $nestedEntry
            }
        }
        return $result
    }
    finally {
        $nested.Dispose()
    }
}

$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new(
        [StringComparer]::Ordinal)
    $caseInsensitive = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $zip.Entries) {
        $path = $entry.FullName.Replace('\', '/')
        if ($path.StartsWith('/') -or $path.Contains('../') -or $path.Contains('/../')) {
            throw "Unsafe ZIP entry: $path"
        }
        if (-not $caseInsensitive.Add($path)) {
            throw "Case-insensitive path collision: $path"
        }
        if ($entry.Name.Length -gt 0) {
            $entries.Add($path, $entry)
        }
    }

    $nestedMinecraftPaths = @($entries.Keys | Where-Object {
            $_.StartsWith(
                'client/.minecraft/',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($nestedMinecraftPaths.Count -gt 0) {
        throw 'Client payload must be rooted directly below client/, not client/.minecraft/.'
    }

    $allowedRoots = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    @('hechao-pack.json', 'client', 'server', 'shared') |
        ForEach-Object { [void]$allowedRoots.Add($_) }
    foreach ($path in $entries.Keys) {
        $rootName = ($path -split '/', 2)[0]
        if (-not $allowedRoots.Contains($rootName)) {
            throw "Unexpected root entry: $rootName"
        }
    }

    if (-not (Test-Path -LiteralPath $archiveChecksumPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $payloadChecksumPath -PathType Leaf)) {
        throw 'Archive or payload checksum sidecar is missing.'
    }
    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    $archiveChecksum = [IO.File]::ReadAllText($archiveChecksumPath).Trim()
    if ($archiveChecksum -notmatch '^([0-9a-fA-F]{64})  (.+)$' -or
        $Matches[1] -ine $actualArchiveHash -or
        $Matches[2] -cne [IO.Path]::GetFileName($archivePath)) {
        throw 'Archive checksum sidecar does not match the ZIP.'
    }

    $expected = [Collections.Generic.SortedDictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($line in [IO.File]::ReadAllLines($payloadChecksumPath)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64}) (.+)$') {
            throw "Invalid payload manifest line: $line"
        }
        $expected.Add($Matches[2], $Matches[1])
    }

    $actualPayloadPaths = $entries.Keys | Sort-Object -CaseSensitive
    if ($actualPayloadPaths.Count -ne $expected.Count) {
        throw "Payload count mismatch. Manifest=$($expected.Count), ZIP=$($actualPayloadPaths.Count)."
    }

    $totalBytes = 0L
    foreach ($item in $expected.GetEnumerator()) {
        if (-not $entries.ContainsKey($item.Key)) {
            throw "Payload entry is missing: $($item.Key)"
        }
        $entry = $entries[$item.Key]
        $actualHash = Get-EntryHash $entry
        if ($actualHash -ne $item.Value) {
            throw "Payload hash mismatch: $($item.Key)"
        }
        $totalBytes += $entry.Length
    }

    foreach ($forbidden in @(
        '启动本机服务端.cmd',
        'client/Plain Craft Launcher 2.2.exe',
        'server/plugins/LuckPerms/luckperms-h2-v2.mv.db',
        'server/plugins/SkyrealmCore/settings.db',
        'server/plugins/HechaoEconomy/economy-token.txt',
        'server/usercache.json',
        'server/run.bat',
        'server/run.sh'
    )) {
        if ($entries.ContainsKey($forbidden)) {
            throw "Forbidden runtime or secret file is present: $forbidden"
        }
    }

    foreach ($required in @(
        'hechao-pack.json',
        'server/start.bat',
        $economyPluginPath,
        $serverEconomyScreenPath,
        $clientEconomyScreenPath,
        $clientProfilePath,
        $clientVersionJsonPath,
        $clientVersionJarPath,
        'server/plugins/HechaoEconomy/config.yml',
        'server/plugins/HechaoEconomy/服主快捷设置.txt'
    )) {
        if (-not $entries.ContainsKey($required)) {
            throw "Required integration file is missing: $required"
        }
    }

    $serverModHash = Get-EntryHash $entries[$serverEconomyScreenPath]
    $clientModHash = Get-EntryHash $entries[$clientEconomyScreenPath]
    if ($serverModHash -ne $clientModHash) {
        throw 'Client and server economy screen JARs are not identical.'
    }

    $clientProfile = (Read-ZipText $entries[$clientProfilePath]) |
        ConvertFrom-Json
    $clientVersion = (Read-ZipText $entries[$clientVersionJsonPath]) |
        ConvertFrom-Json
    if ($clientProfile.schemaVersion -ne 1 -or
        $clientProfile.versionId -cne $clientVersionId -or
        $clientProfile.javaMajorVersion -ne 21 -or
        $clientVersion.id -cne $clientVersionId -or
        $clientVersion.javaVersion.majorVersion -ne 21 -or
        $entries[$clientVersionJarPath].Length -le 0) {
        throw 'Client launch metadata does not match the version JSON and JAR.'
    }

    $pluginContract = Read-NestedZipContract `
        -Entry $entries[$economyPluginPath] `
        -RequiredPaths @(
            'plugin.yml',
            'world/hechao/economy/HechaoEconomyPlugin.class',
            'world/hechao/economy/commands/EconomyCommandRouter.class',
            'world/hechao/economy/commands/ProductAdminPrompt.class'
        ) `
        -TextPaths @('plugin.yml')
    $pluginYaml = $pluginContract['plugin.yml']
    if ($pluginYaml -notmatch "(?m)^version: '$([regex]::Escape($ExpectedEconomyPluginVersion))'$" -or
        $pluginYaml -notmatch '(?m)^main: world\.hechao\.economy\.HechaoEconomyPlugin$' -or
        $pluginYaml -notmatch '(?m)^  hechao\.economy\.admin:$') {
        throw 'Economy plugin identity or administrator permission contract is invalid.'
    }
    foreach ($command in @('money', 'pay', 'sell', 'shop', 'heco')) {
        $commandPattern = '(?m)^  ' + [regex]::Escape($command) + ':$'
        if ($pluginYaml -notmatch $commandPattern) {
            throw "Economy plugin command is missing: $command"
        }
    }
    $promptClass = [Text.Encoding]::Latin1.GetString(
        $pluginContract['world/hechao/economy/commands/ProductAdminPrompt.class'])
    foreach ($contractText in @(
        '/heco product set ',
        '/heco product remove'
    )) {
        if (-not $promptClass.Contains($contractText, [StringComparison]::Ordinal)) {
            throw "Economy owner quick-management class is missing: $contractText"
        }
    }

    $screenContract = Read-NestedZipContract `
        -Entry $entries[$serverEconomyScreenPath] `
        -RequiredPaths @(
            'META-INF/neoforge.mods.toml',
            'world/hechao/economyscreen/HechaoEconomyScreenMod.class',
            'world/hechao/economyscreen/MenuActions.class',
            'world/hechao/economyscreen/client/HechaoNavigationScreen.class',
            'world/hechao/economyscreen/network/OpenMenuPayload.class',
            'world/hechao/economyscreen/network/MenuActionPayload.class'
        ) `
        -TextPaths @('META-INF/neoforge.mods.toml')
    $modsToml = $screenContract['META-INF/neoforge.mods.toml']
    if (-not $modsToml.Contains('modId="hechao_economy_screen"') -or
        -not $modsToml.Contains('versionRange="[21.1.228,22)"') -or
        -not $modsToml.Contains('versionRange="[1.21.1,1.21.2)"') -or
        (($modsToml -split 'side="BOTH"').Count - 1) -ne 2) {
        throw 'Economy screen NeoForge or Minecraft compatibility contract is invalid.'
    }
    $menuActionsClass = [Text.Encoding]::Latin1.GetString(
        $screenContract['world/hechao/economyscreen/MenuActions.class'])
    if (-not $menuActionsClass.Contains('admin_product', [StringComparison]::Ordinal) -or
        -not $menuActionsClass.Contains('heco product', [StringComparison]::Ordinal)) {
        throw 'Economy screen owner product action is missing.'
    }

    $start = Read-ZipText $entries['server/start.bat']
    $descriptor = (Read-ZipText $entries['hechao-pack.json']) | ConvertFrom-Json
    $essentials = Read-ZipText $entries['server/plugins/Essentials/config.yml']
    $worth = Read-ZipText $entries['server/plugins/Essentials/worth.yml']
    $tab = Read-ZipText $entries['server/plugins/TAB/config.yml']
    $serverProperties = Read-ZipText $entries['server/server.properties']
    $ownerGuide = Read-ZipText `
        $entries['server/plugins/HechaoEconomy/服主快捷设置.txt']
    if (-not $start.Contains('if not defined HECHAO_MANAGED_START pause') -or
        -not $start.Contains('21.1.228/win_args.txt nogui')) {
        throw 'Managed start script contract is invalid.'
    }
    if ($descriptor.version -ne $ExpectedPackageVersion -or
        $descriptor.minecraftVersion -ne '1.21.1' -or
        $descriptor.loader -ne 'NeoForge' -or
        $descriptor.loaderVersion -ne '21.1.228' -or
        $descriptor.javaMajorVersion -ne 21) {
        throw 'Package descriptor versions do not match the expected runtime.'
    }
    foreach ($command in @('balance', 'pay', 'sell', 'worth', 'eco')) {
        if ($essentials -notmatch "(?m)^  - $command$") {
            throw "Essentials command is not disabled: $command"
        }
    }
    if (-not $worth.Contains('HechaoEconomy owns') -or
        -not $tab.Contains('%hechao_balance%')) {
        throw 'Economy ownership or TAB placeholder configuration is incomplete.'
    }
    foreach ($property in @(
        'server-ip=127.0.0.1',
        'server-port=25565',
        'online-mode=false'
    )) {
        if ($serverProperties -notmatch "(?m)^$([regex]::Escape($property))$") {
            throw "Managed server property is missing: $property"
        }
    }
    foreach ($guideContract in @(
        'hechao.economy.admin',
        '/heco product set <单价> [个人日限] [全服日限]',
        '/heco product remove',
        '无自定义数据的模组物品'
    )) {
        if (-not $ownerGuide.Contains($guideContract, [StringComparison]::Ordinal)) {
            throw "Owner quick-management guide is incomplete: $guideContract"
        }
    }
    [PSCustomObject]@{
        Archive = $archivePath
        Sha256 = $actualArchiveHash
        PayloadFiles = $expected.Count
        PayloadBytes = $totalBytes
        ClientServerScreenJarSha256 = $clientModHash.ToUpperInvariant()
        Status = 'Valid'
    }
}
finally {
    $zip.Dispose()
}
