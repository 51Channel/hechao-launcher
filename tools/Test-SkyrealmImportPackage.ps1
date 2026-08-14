[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Archive,

    [string]$ExpectedPackageVersion = '1.0.3',

    [string]$ExpectedEconomyPluginVersion = '0.1.1',

    [string]$ExpectedEconomyScreenVersion = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required.'
}

$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$economyPluginPath =
    "server/plugins/HechaoEconomy-$ExpectedEconomyPluginVersion.jar"
$economyScreenFileName =
    "HechaoEconomyScreen-NeoForge-1.21.1-$ExpectedEconomyScreenVersion.jar"
$serverEconomyScreenPath = "server/mods/$economyScreenFileName"
$clientEconomyScreenPath = "client/.minecraft/mods/$economyScreenFileName"
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

    $manifestEntry = $entries['manifest/payload.sha256']
    $releaseEntry = $entries['manifest/release-manifest.json']
    if ($null -eq $manifestEntry -or $null -eq $releaseEntry) {
        throw 'Required manifests are missing.'
    }
    $manifestText = Read-ZipText $manifestEntry
    $expected = [Collections.Generic.SortedDictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($line in $manifestText -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64}) (.+)$') {
            throw "Invalid payload manifest line: $line"
        }
        $expected.Add($Matches[2], $Matches[1])
    }

    $actualPayloadPaths = $entries.Keys |
        Where-Object { -not $_.StartsWith('manifest/', [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object -CaseSensitive
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

    $manifestHash = ([Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                $utf8.GetBytes($manifestText)))).ToLowerInvariant()
    $release = (Read-ZipText $releaseEntry) | ConvertFrom-Json
    if ($release.payload.file_count -ne $expected.Count -or
        $release.payload.bytes -ne $totalBytes -or
        $release.payload.checksum_sha256 -ne $manifestHash) {
        throw 'Release manifest payload summary does not match the archive.'
    }
    if ($release.release_id -ne "hechao-skyrealm-economy-screen-$ExpectedPackageVersion" -or
        $release.versions.hechao_economy -ne $ExpectedEconomyPluginVersion -or
        $release.versions.hechao_economy_screen -ne $ExpectedEconomyScreenVersion) {
        throw 'Release manifest component versions do not match the expected versions.'
    }

    foreach ($forbidden in @(
        '启动本机服务端.cmd',
        'client/Plain Craft Launcher 2.2.exe',
        'server/plugins/LuckPerms/luckperms-h2-v2.mv.db',
        'server/plugins/SkyrealmCore/settings.db',
        'server/plugins/HechaoEconomy/economy-token.txt',
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
        'server/plugins/HechaoEconomy/config.yml'
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

    $pluginContract = Read-NestedZipContract `
        -Entry $entries[$economyPluginPath] `
        -RequiredPaths @(
            'plugin.yml',
            'world/hechao/economy/HechaoEconomyPlugin.class',
            'world/hechao/economy/commands/EconomyCommandRouter.class'
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

    $screenContract = Read-NestedZipContract `
        -Entry $entries[$serverEconomyScreenPath] `
        -RequiredPaths @(
            'META-INF/neoforge.mods.toml',
            'world/hechao/economyscreen/HechaoEconomyScreenMod.class',
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

    $start = Read-ZipText $entries['server/start.bat']
    $descriptor = (Read-ZipText $entries['hechao-pack.json']) | ConvertFrom-Json
    $essentials = Read-ZipText $entries['server/plugins/Essentials/config.yml']
    $worth = Read-ZipText $entries['server/plugins/Essentials/worth.yml']
    $tab = Read-ZipText $entries['server/plugins/TAB/config.yml']
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

    [PSCustomObject]@{
        Archive = $archivePath
        Sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        PayloadFiles = $expected.Count
        PayloadBytes = $totalBytes
        ClientServerScreenJarSha256 = $clientModHash.ToUpperInvariant()
        Status = 'Valid'
    }
}
finally {
    $zip.Dispose()
}
