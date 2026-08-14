[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputArchive,

    [Parameter(Mandatory)]
    [string]$OutputArchive,

    [Parameter(Mandatory)]
    [string]$EconomyPluginJar,

    [Parameter(Mandatory)]
    [string]$EconomyScreenJar,

    [string]$ExpectedInputSha256 =
        'A0393BC880DE4E70181B244E8ED42774AEF582908E2F072D31552317931860E9'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required.'
}

foreach ($path in @($InputArchive, $EconomyPluginJar, $EconomyScreenJar)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file does not exist: $path"
    }
}

$inputPath = (Resolve-Path -LiteralPath $InputArchive).Path
$economyJarPath = (Resolve-Path -LiteralPath $EconomyPluginJar).Path
$screenJarPath = (Resolve-Path -LiteralPath $EconomyScreenJar).Path
$outputPath = [IO.Path]::GetFullPath($OutputArchive)
$outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw 'Output archive must include a directory.'
}
if (Test-Path -LiteralPath $outputPath) {
    throw "Output archive already exists and will not be overwritten: $outputPath"
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$actualInputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
if ($actualInputHash -ne $ExpectedInputSha256) {
    throw "Input archive SHA-256 mismatch. Expected $ExpectedInputSha256, got $actualInputHash."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$utf8 = [Text.UTF8Encoding]::new($false)
$fixedTimestamp = [DateTimeOffset]::new(2026, 8, 14, 4, 0, 0, [TimeSpan]::Zero)
$partialPath = $outputPath + '.partial'
$payload = [Collections.Generic.SortedDictionary[string, object]]::new(
    [StringComparer]::Ordinal)
$excluded = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
@(
    'manifest/payload.sha256',
    'manifest/release-manifest.json',
    'hechao-pack.json',
    'server/start.bat',
    'server/run.bat',
    'server/run.sh',
    '启动本机服务端.cmd',
    'client/Plain Craft Launcher 2.2.exe',
    'server/plugins/LuckPerms/luckperms-h2-v2.mv.db',
    'server/plugins/SkyrealmCore/settings.db',
    'server/plugins/Essentials/usermap.bin',
    'server/plugins/Essentials/uuids.bin',
    'server/plugins/Essentials/worth.yml',
    'server/plugins/Essentials/config.yml',
    'server/plugins/TAB/config.yml',
    '组件清单.txt'
) | ForEach-Object { [void]$excluded.Add($_) }

function Get-BytesHash {
    param([byte[]]$Bytes)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Add-BytesEntry {
    param(
        [IO.Compression.ZipArchive]$Archive,
        [string]$Path,
        [byte[]]$Bytes,
        [bool]$IncludeInPayload = $true
    )
    $entry = $Archive.CreateEntry($Path, [IO.Compression.CompressionLevel]::NoCompression)
    $entry.LastWriteTime = $fixedTimestamp
    $stream = $entry.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
    if ($IncludeInPayload) {
        $payload[$Path] = [PSCustomObject]@{
            Hash = Get-BytesHash $Bytes
            Length = [long]$Bytes.Length
        }
    }
}

function Add-FileEntry {
    param(
        [IO.Compression.ZipArchive]$Archive,
        [string]$Path,
        [string]$SourcePath
    )
    $entry = $Archive.CreateEntry($Path, [IO.Compression.CompressionLevel]::NoCompression)
    $entry.LastWriteTime = $fixedTimestamp
    $source = [IO.File]::OpenRead($SourcePath)
    $target = $entry.Open()
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(1024 * 1024)
    $length = 0L
    try {
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $target.Write($buffer, 0, $read)
            $hasher.AppendData($buffer, 0, $read)
            $length += $read
        }
    }
    finally {
        $source.Dispose()
        $target.Dispose()
    }
    $payload[$Path] = [PSCustomObject]@{
        Hash = [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
        Length = $length
    }
    $hasher.Dispose()
}

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

function Get-EssentialsConfiguration {
    param([string]$Original)
    $disabled = @'
disabled-commands:
  - balance
  - bal
  - ebalance
  - money
  - emoney
  - balancetop
  - baltop
  - ebaltop
  - pay
  - epay
  - sell
  - esell
  - sellall
  - esellall
  - worth
  - eworth
  - eco
  - economy

'@
    $pattern = '(?ms)^disabled-commands:.*?(?=^# Whether or not Essentials should show detailed command usages\.)'
    $updated = [Text.RegularExpressions.Regex]::Replace($Original, $pattern, $disabled)
    if ($updated -eq $Original) {
        throw 'Unable to replace Essentials disabled-commands safely.'
    }
    return $updated
}

function Get-TabConfiguration {
    param([string]$Original)
    $updated = $Original.Replace('&3&lServer name', '&6&l天域远征工业季')
    $updated = $updated.Replace(
        '    - "&2Ping: %ping%"',
        "    - `"&6金币: &f%hechao_balance%`"`n    - `"&2Ping: %ping%`"")
    $updated = $updated.Replace(
        '  "%vault_prefix%": 1000',
        "  `"%vault_prefix%`": 1000`n  `"%hechao_balance%`": 2000")
    if ($updated -eq $Original -or -not $updated.Contains('%hechao_balance%')) {
        throw 'Unable to add Hechao balance placeholder to TAB configuration.'
    }
    return $updated
}

$input = $null
$outputStream = $null
$output = $null
try {
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }
    $input = [IO.Compression.ZipFile]::OpenRead($inputPath)
    $checksumEntry = $input.GetEntry('manifest/payload.sha256')
    if ($null -eq $checksumEntry) {
        throw 'Input payload manifest is missing.'
    }
    $sourceHashes = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($line in (Read-ZipText $checksumEntry) -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64}) (.+)$') {
            throw "Invalid source checksum line: $line"
        }
        $sourceHashes[$Matches[2]] = $Matches[1]
    }

    $outputStream = [IO.File]::Open(
        $partialPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    $output = [IO.Compression.ZipArchive]::new(
        $outputStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $true,
        $utf8)

    foreach ($sourceEntry in $input.Entries) {
        $path = $sourceEntry.FullName.Replace('\', '/')
        if ($sourceEntry.Name.Length -eq 0 -or $excluded.Contains($path)) {
            continue
        }
        $entry = $output.CreateEntry($path, [IO.Compression.CompressionLevel]::NoCompression)
        $entry.LastWriteTime = $sourceEntry.LastWriteTime
        $source = $sourceEntry.Open()
        $target = $entry.Open()
        try {
            $source.CopyTo($target, 1024 * 1024)
        }
        finally {
            $source.Dispose()
            $target.Dispose()
        }
        if (-not $path.StartsWith('manifest/', [StringComparison]::OrdinalIgnoreCase)) {
            if (-not $sourceHashes.ContainsKey($path)) {
                throw "Source payload hash is missing for $path"
            }
            $payload[$path] = [PSCustomObject]@{
                Hash = $sourceHashes[$path]
                Length = [long]$sourceEntry.Length
            }
        }
    }

    $essentialsEntry = $input.GetEntry('server/plugins/Essentials/config.yml')
    $tabEntry = $input.GetEntry('server/plugins/TAB/config.yml')
    $componentsEntry = $input.GetEntry('组件清单.txt')
    if ($null -eq $essentialsEntry -or $null -eq $tabEntry -or $null -eq $componentsEntry) {
        throw 'A required source configuration is missing.'
    }
    Add-BytesEntry $output 'server/plugins/Essentials/config.yml' (
        $utf8.GetBytes((Get-EssentialsConfiguration (Read-ZipText $essentialsEntry))))
    Add-BytesEntry $output 'server/plugins/Essentials/worth.yml' (
        $utf8.GetBytes("# Disabled. HechaoEconomy owns the complete product catalog.`n"))
    Add-BytesEntry $output 'server/plugins/TAB/config.yml' (
        $utf8.GetBytes((Get-TabConfiguration (Read-ZipText $tabEntry))))

    $components = (Read-ZipText $componentsEntry).TrimEnd() + @'


赫朝平台新增组件：
- server/plugins/HechaoEconomy-0.1.1.jar
- server/mods/HechaoEconomyScreen-NeoForge-1.21.1-0.1.0.jar
- client/.minecraft/mods/HechaoEconomyScreen-NeoForge-1.21.1-0.1.0.jar
'@
    Add-BytesEntry $output '组件清单.txt' $utf8.GetBytes($components)

    Add-FileEntry $output `
        'server/plugins/HechaoEconomy-0.1.1.jar' `
        $economyJarPath
    Add-FileEntry $output `
        'server/mods/HechaoEconomyScreen-NeoForge-1.21.1-0.1.0.jar' `
        $screenJarPath
    Add-FileEntry $output `
        'client/.minecraft/mods/HechaoEconomyScreen-NeoForge-1.21.1-0.1.0.jar' `
        $screenJarPath

    $pluginConfig = @'
api-base-url: "https://launcher-api.hechao.world"
server-id: "skyrealm"
token-environment-variable: "HECHAO_ECONOMY_TOKEN"
token-file: "plugins/HechaoEconomy/economy-token.txt"
request-timeout-seconds: 3
balance-cache-seconds: 15
pay-confirm-threshold: 10000.00
default-personal-daily-limit: 2304
default-server-daily-limit: 23040
'@
    Add-BytesEntry $output 'server/plugins/HechaoEconomy/config.yml' $utf8.GetBytes($pluginConfig)
    Add-BytesEntry $output 'server/plugins/HechaoEconomy/部署说明.txt' $utf8.GetBytes(@'
经济 API 令牌不得放入整合包、Git 或聊天记录。
生产部署时通过 HECHAO_ECONOMY_TOKEN 环境变量提供；也可由部署器在停服状态下创建
plugins/HechaoEconomy/economy-token.txt，并将文件 ACL 限制为服务账号只读。
令牌缺失、API 不可用或 Vault 未由 HechaoEconomy 接管时，所有写交易会故障关闭。
'@)

    $startScript = @'
@echo off
setlocal
cd /d "%~dp0"
if not defined HECHAO_MANAGED_START pause
java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui
'@
    Add-BytesEntry $output 'server/start.bat' $utf8.GetBytes($startScript)

    $descriptor = [ordered]@{
        schemaVersion = 1
        id = 'skyrealm-industrial-neoforge-1.21.1'
        displayName = '天域远征工业季'
        version = '1.0.2'
        minecraftVersion = '1.21.1'
        javaMajorVersion = 21
        loader = 'NeoForge'
        loaderVersion = '21.1.228'
        clientRoot = 'client'
        serverRoot = 'server'
        sharedRoot = 'shared'
    }
    Add-BytesEntry $output 'hechao-pack.json' $utf8.GetBytes(
        ($descriptor | ConvertTo-Json -Depth 5))

    $readme = @'
# 天域远征工业季 - 赫朝一键导入包

- Minecraft 1.21.1 / Arclight NeoForge / NeoForge 21.1.228 / Java 21。
- 新增 HechaoEconomy 0.1.1 和双端 HechaoEconomyScreen 0.1.0。
- Essentials 经济命令和 worth.yml 已停用；Vault 必须由 HechaoEconomy 接管。
- TAB 余额使用 `%hechao_balance%`，没有远端余额时显示 `--`。
- 模组物品、命名物、附魔物、容器和带数据组件物品默认拒绝出售。
- 经济 API 令牌不在包内，部署时必须从外部秘密配置注入。
- 客户端菜单只回传短期会话和 action ID，不接受客户端命令文本。
- 本包可被赫朝后台识别、拆分和发布；长期生存目标仍需单独配置受控部署目标。
- 未启动本包服务端；Velocity、LuckPerms、语音 UDP、混合核心玩法和真人压力测试仍需验收。
'@
    Add-BytesEntry $output 'README-赫朝导入.md' $utf8.GetBytes($readme)

    $payloadLines = foreach ($item in $payload.GetEnumerator()) {
        "$($item.Value.Hash) $($item.Key)"
    }
    $payloadBytes = $utf8.GetBytes(($payloadLines -join "`n") + "`n")
    $payloadHash = Get-BytesHash $payloadBytes
    $payloadTotalBytes = ($payload.Values | Measure-Object -Property Length -Sum).Sum
    $releaseManifest = [ordered]@{
        schema_version = 1
        release_id = 'hechao-skyrealm-economy-screen-1.0.2'
        project = '天域远征工业季'
        created_utc = $fixedTimestamp.ToString('O')
        source_archive_sha256 = $actualInputHash.ToLowerInvariant()
        versions = [ordered]@{
            minecraft = '1.21.1'
            arclight = 'NeoForge 1.0.2-SNAPSHOT-8086b06'
            neoforge = '21.1.228'
            java = 21
            hechao_economy = '0.1.1'
            hechao_economy_screen = '0.1.0'
        }
        security = [ordered]@{
            economy_token_included = $false
            vault_fail_closed = $true
            vanilla_sell_allowlist_only = $true
            arbitrary_client_commands = $false
        }
        payload = [ordered]@{
            file_count = $payload.Count
            bytes = $payloadTotalBytes
            checksum_path = 'manifest/payload.sha256'
            checksum_sha256 = $payloadHash
        }
        runtime_boundary = [ordered]@{
            minecraft_started = $false
            exact_packaged_release_launch = 'not-tested'
            multiplayer = 'not-tested'
            production_velocity = 'not-configured'
            economy_api = 'source-and-contract-tested-not-deployed'
        }
    }
    Add-BytesEntry $output 'manifest/payload.sha256' $payloadBytes $false
    Add-BytesEntry $output 'manifest/release-manifest.json' $utf8.GetBytes(
        ($releaseManifest | ConvertTo-Json -Depth 8)) $false
}
catch {
    if ($null -ne $output) {
        $output.Dispose()
        $output = $null
    }
    if ($null -ne $outputStream) {
        $outputStream.Dispose()
        $outputStream = $null
    }
    if ($null -ne $input) {
        $input.Dispose()
        $input = $null
    }
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }
    throw
}
finally {
    if ($null -ne $output) {
        $output.Dispose()
    }
    if ($null -ne $outputStream) {
        $outputStream.Dispose()
    }
    if ($null -ne $input) {
        $input.Dispose()
    }
}

Move-Item -LiteralPath $partialPath -Destination $outputPath
$outputHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
$checksumPath = $outputPath + '.sha256'
[IO.File]::WriteAllText(
    $checksumPath,
    "$($outputHash.ToLowerInvariant())  $([IO.Path]::GetFileName($outputPath))`n",
    $utf8)

[PSCustomObject]@{
    OutputArchive = $outputPath
    Sha256 = $outputHash
    Bytes = (Get-Item -LiteralPath $outputPath).Length
    PayloadFiles = $payload.Count
    PayloadBytes = $payloadTotalBytes
    ChecksumFile = $checksumPath
}
