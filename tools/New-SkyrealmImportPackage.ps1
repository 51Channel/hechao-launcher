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

    [string]$PackageVersion = '1.0.9',

    [string]$ExpectedInputSha256 =
        'A0393BC880DE4E70181B244E8ED42774AEF582908E2F072D31552317931860E9'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required.'
}
if ($PackageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw 'PackageVersion must use major.minor.patch format.'
}

foreach ($path in @($InputArchive, $EconomyPluginJar, $EconomyScreenJar)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file does not exist: $path"
    }
}

$inputPath = (Resolve-Path -LiteralPath $InputArchive).Path
$economyJarPath = (Resolve-Path -LiteralPath $EconomyPluginJar).Path
$screenJarPath = (Resolve-Path -LiteralPath $EconomyScreenJar).Path
$economyPluginVersion = '0.1.2'
$economyScreenVersion = '0.1.2'
$economyPluginFileName = "HechaoEconomy-$economyPluginVersion.jar"
$economyScreenFileName =
    "HechaoEconomyScreen-NeoForge-1.21.1-$economyScreenVersion.jar"
$clientVersionId = '天域远征工业季 1.21.1'
$clientVersionJsonSourcePath =
    "client/.minecraft/versions/$clientVersionId/$clientVersionId.json"
$clientVersionJsonOutputPath =
    "client/versions/$clientVersionId/$clientVersionId.json"
$clientVersionJarSourcePath =
    "client/.minecraft/versions/$clientVersionId/$clientVersionId.jar"
$clientVersionJarOutputPath =
    "client/versions/$clientVersionId/$clientVersionId.jar"
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
    'client/.minecraft/hechao-profile.json',
    'client/hechao-profile.json',
    $clientVersionJsonSourcePath,
    'server/plugins/LuckPerms/luckperms-h2-v2.mv.db',
    'server/plugins/SkyrealmCore/settings.db',
    'server/plugins/Essentials/usermap.bin',
    'server/plugins/Essentials/uuids.bin',
    'server/plugins/Essentials/worth.yml',
    'server/plugins/Essentials/config.yml',
    'server/plugins/TAB/config.yml',
    'server/server.properties',
    'server/usercache.json',
    '组件清单.txt',
    'README-交接.md'
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

function Get-OutputPayloadPath {
    param([string]$SourcePath)

    $clientMinecraftRoot = 'client/.minecraft/'
    if ($SourcePath.StartsWith(
            $clientMinecraftRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        return 'client/' + $SourcePath.Substring($clientMinecraftRoot.Length)
    }

    return $SourcePath
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

function Get-ServerPropertiesConfiguration {
    param([string]$Original)

    $required = [ordered]@{
        'server-ip' = '127.0.0.1'
        'server-port' = '25565'
        'online-mode' = 'false'
    }
    $written = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $output = [Collections.Generic.List[string]]::new()
    foreach ($line in $Original -split "`r?`n") {
        $trimmed = $line.Trim()
        $separator = $line.IndexOf('=')
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#') -or $separator -lt 1) {
            $output.Add($line)
            continue
        }
        $key = $line.Substring(0, $separator).Trim()
        if ($required.Contains($key)) {
            if (-not $written.Add($key)) {
                throw "Duplicate managed server property: $key"
            }
            $output.Add("$key=$($required[$key])")
            continue
        }
        $output.Add($line)
    }
    foreach ($item in $required.GetEnumerator()) {
        if ($written.Add($item.Key)) {
            $output.Add("$($item.Key)=$($item.Value)")
        }
    }
    return ($output -join "`n").TrimEnd() + "`n"
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
    $copiedOutputPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)

    foreach ($sourceEntry in $input.Entries) {
        $sourcePath = $sourceEntry.FullName.Replace('\', '/')
        if ($sourceEntry.Name.Length -eq 0 -or $excluded.Contains($sourcePath)) {
            continue
        }
        $path = Get-OutputPayloadPath $sourcePath
        if (-not $copiedOutputPaths.Add($path)) {
            throw "Multiple source entries map to the same output path: $path"
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
            if (-not $sourceHashes.ContainsKey($sourcePath)) {
                throw "Source payload hash is missing for $sourcePath"
            }
            $payload[$path] = [PSCustomObject]@{
                Hash = $sourceHashes[$sourcePath]
                Length = [long]$sourceEntry.Length
            }
        }
    }

    $clientVersionEntry = $input.GetEntry($clientVersionJsonSourcePath)
    if ($null -eq $clientVersionEntry -or
        $null -eq $input.GetEntry($clientVersionJarSourcePath) -or
        -not $copiedOutputPaths.Contains($clientVersionJarOutputPath)) {
        throw 'The launchable client version JSON or JAR is missing.'
    }
    $clientVersion = (Read-ZipText $clientVersionEntry) |
        ConvertFrom-Json -AsHashtable
    if ($clientVersion['id'] -cne $clientVersionId) {
        throw 'The launchable client version JSON ID does not match its directory.'
    }
    $clientVersion['javaVersion'] = [ordered]@{
        component = 'java-runtime-delta'
        majorVersion = 21
    }
    Add-BytesEntry $output $clientVersionJsonOutputPath $utf8.GetBytes(
        ($clientVersion | ConvertTo-Json -Depth 100))
    Add-BytesEntry $output 'client/hechao-profile.json' $utf8.GetBytes(
        ([ordered]@{
                schemaVersion = 1
                versionId = $clientVersionId
                javaMajorVersion = 21
            } | ConvertTo-Json -Depth 5))

    $essentialsEntry = $input.GetEntry('server/plugins/Essentials/config.yml')
    $tabEntry = $input.GetEntry('server/plugins/TAB/config.yml')
    $serverPropertiesEntry = $input.GetEntry('server/server.properties')
    if ($null -eq $essentialsEntry -or $null -eq $tabEntry -or
        $null -eq $serverPropertiesEntry) {
        throw 'A required source configuration is missing.'
    }
    Add-BytesEntry $output 'server/plugins/Essentials/config.yml' (
        $utf8.GetBytes((Get-EssentialsConfiguration (Read-ZipText $essentialsEntry))))
    Add-BytesEntry $output 'server/plugins/Essentials/worth.yml' (
        $utf8.GetBytes("# Disabled. HechaoEconomy owns the complete product catalog.`n"))
    Add-BytesEntry $output 'server/plugins/TAB/config.yml' (
        $utf8.GetBytes((Get-TabConfiguration (Read-ZipText $tabEntry))))
    Add-BytesEntry $output 'server/server.properties' (
        $utf8.GetBytes((Get-ServerPropertiesConfiguration (
                    Read-ZipText $serverPropertiesEntry))))

    Add-FileEntry $output `
        "server/plugins/$economyPluginFileName" `
        $economyJarPath
    Add-FileEntry $output `
        "server/mods/$economyScreenFileName" `
        $screenJarPath
    Add-FileEntry $output `
        "client/mods/$economyScreenFileName" `
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
    Add-BytesEntry $output 'server/plugins/HechaoEconomy/服主快捷设置.txt' $utf8.GetBytes(@'
服主快捷设置回收物品

1. 使用 LuckPerms 授予服主权限：hechao.economy.admin。
2. 在游戏内把要配置的物品拿到主手。
3. 打开“天域远征”自定义屏幕，点击“服主回收设置”；也可输入 /heco product。
4. 点击常用价格即可启用回收；需要自定义时补全：
   /heco product set <单价> [个人日限] [全服日限]
5. 暂停该物品回收：/heco product remove
6. 使用 /shop 检查玩家实际看到的启用目录。

支持原版物品和无自定义数据的模组物品。命名、附魔、容器、带组件或其他数据的
物品会被拒绝，防止不同内容共用同一物品 ID 后被错误回收。
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
        version = $PackageVersion
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

    $payloadTotalBytes = ($payload.Values | Measure-Object -Property Length -Sum).Sum
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
$payloadChecksumPath = $outputPath + '.payload.sha256'
$payloadLines = foreach ($item in $payload.GetEnumerator()) {
    "$($item.Value.Hash) $($item.Key)"
}
[IO.File]::WriteAllText(
    $payloadChecksumPath,
    ($payloadLines -join "`n") + "`n",
    $utf8)

[PSCustomObject]@{
    OutputArchive = $outputPath
    Sha256 = $outputHash
    Bytes = (Get-Item -LiteralPath $outputPath).Length
    PayloadFiles = $payload.Count
    PayloadBytes = $payloadTotalBytes
    ChecksumFile = $checksumPath
    PayloadChecksumFile = $payloadChecksumPath
}
