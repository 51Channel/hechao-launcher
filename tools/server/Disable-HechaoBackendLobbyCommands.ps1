[CmdletBinding()]
param(
    [string]$BackupRoot = 'E:\manual-backups',
    [string[]]$ServerRoots = @(
        'E:\Survival1',
        'E:\Survival2',
        'E:\DollNight',
        'E:\ActivityLocal',
        'E:\ActivityServer',
        'E:\MonsterActivity'
    )
)

$ErrorActionPreference = 'Stop'
$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path (
    [IO.Path]::GetFullPath($BackupRoot)
) "BackendLobbyCommands-$timestamp"
$records = [Collections.Generic.List[object]]::new()

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $normalizedPath = [IO.Path]::GetFullPath($Path)
    if (-not $normalizedPath.StartsWith(
            $normalizedRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes server root: $normalizedPath"
    }
}

foreach ($candidate in $ServerRoots) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
        continue
    }

    $root = [IO.Path]::GetFullPath($candidate)
    $scriptsDirectory = Join-Path $root 'plugins\Skript\scripts'
    if (-not (Test-Path -LiteralPath $scriptsDirectory -PathType Container)) {
        continue
    }

    $activeHub = Join-Path $scriptsDirectory 'hub.sk'
    $disabledHub = Join-Path $scriptsDirectory '-hub.sk'
    $broadcast = Join-Path $scriptsDirectory 'broadcast.sk'
    foreach ($path in @($activeHub, $disabledHub, $broadcast)) {
        Assert-ChildPath -Root $root -Path $path
    }

    if (-not (Test-Path -LiteralPath $activeHub -PathType Leaf) -and
        -not (Test-Path -LiteralPath $disabledHub -PathType Leaf) -and
        -not (Test-Path -LiteralPath $broadcast -PathType Leaf)) {
        continue
    }

    $name = Split-Path -Leaf $root
    $serverBackup = Join-Path $backupDirectory $name
    New-Item -ItemType Directory -Path $serverBackup -Force | Out-Null
    $record = [pscustomobject]@{
        Name = $name
        Root = $root
        ScriptsDirectory = $scriptsDirectory
        ActiveHub = Test-Path -LiteralPath $activeHub -PathType Leaf
        DisabledHub = Test-Path -LiteralPath $disabledHub -PathType Leaf
        Broadcast = Test-Path -LiteralPath $broadcast -PathType Leaf
        BroadcastChanged = $false
    }
    if ($record.ActiveHub) {
        Copy-Item -LiteralPath $activeHub -Destination (
            Join-Path $serverBackup 'hub.sk')
    }
    if ($record.DisabledHub) {
        Copy-Item -LiteralPath $disabledHub -Destination (
            Join-Path $serverBackup '-hub.sk')
    }
    if ($record.Broadcast) {
        Copy-Item -LiteralPath $broadcast -Destination (
            Join-Path $serverBackup 'broadcast.sk')
    }
    $records.Add($record)
}

if ($records.Count -eq 0) {
    [pscustomobject]@{
        Changed = $false
        BackupDirectory = $null
        Servers = @()
    } | ConvertTo-Json -Compress
    return
}

$metadataPath = Join-Path $backupDirectory 'state.json'
[IO.File]::WriteAllText(
    $metadataPath,
    ($records | ConvertTo-Json -Depth 4),
    [Text.UTF8Encoding]::new($false))

$mutationStarted = $false
try {
    $mutationStarted = $true
    foreach ($record in $records) {
        $activeHub = Join-Path $record.ScriptsDirectory 'hub.sk'
        $disabledHub = Join-Path $record.ScriptsDirectory '-hub.sk'
        $broadcast = Join-Path $record.ScriptsDirectory 'broadcast.sk'

        if ($record.ActiveHub) {
            if ($record.DisabledHub) {
                throw "$($record.Name) has both hub.sk and -hub.sk."
            }
            Move-Item -LiteralPath $activeHub -Destination $disabledHub
        }

        if ($record.Broadcast) {
            $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
            [void]$strictUtf8.GetString(
                [IO.File]::ReadAllBytes($broadcast))
            $lines = [IO.File]::ReadAllLines(
                $broadcast,
                [Text.Encoding]::UTF8)
            $changed = $false
            $updated = @(
                foreach ($line in $lines) {
                    if ($line -match '(?i)/hub\b' -and
                        $line -match '\{broadcast\.msgs::\*\}') {
                        $indent = [regex]::Match($line, '^\s*').Value
                        $indent +
                            'add "&a请在 &e赫朝启动器 &a中选择并切换服务器" ' +
                            'to {broadcast.msgs::*}'
                        $changed = $true
                    }
                    else {
                        $line
                    }
                }
            )
            if ($changed) {
                $temporaryPath =
                    "$broadcast.$([guid]::NewGuid().ToString('N')).tmp"
                try {
                    [IO.File]::WriteAllLines(
                        $temporaryPath,
                        $updated,
                        [Text.UTF8Encoding]::new($false))
                    Move-Item `
                        -LiteralPath $temporaryPath `
                        -Destination $broadcast `
                        -Force
                }
                finally {
                    Remove-Item `
                        -LiteralPath $temporaryPath `
                        -Force `
                        -ErrorAction SilentlyContinue
                }
                $record.BroadcastChanged = $true
            }
        }
    }

    $activeViolations = @(
        foreach ($record in $records) {
            Get-ChildItem `
                -LiteralPath $record.ScriptsDirectory `
                -File `
                -Filter '*.sk' |
                Where-Object { -not $_.Name.StartsWith('-') } |
                Select-String `
                    -Pattern (
                        '(?i)command\s+/hub\b|' +
                        'connect\s+player\s+to\s+server\s+"lobby"'
                    ) `
                    -ErrorAction SilentlyContinue
        }
    )
    if ($activeViolations.Count -ne 0) {
        throw "Active Lobby command references remain: $($activeViolations.Count)."
    }

    $manifest = @(
        Get-ChildItem -LiteralPath $backupDirectory -Recurse -File |
            ForEach-Object {
                $relative = $_.FullName.Substring(
                    $backupDirectory.TrimEnd('\').Length + 1)
                $hash = Get-FileHash `
                    -LiteralPath $_.FullName `
                    -Algorithm SHA256
                "$($hash.Hash.ToLowerInvariant())  $relative"
            }
    )
    [IO.File]::WriteAllLines(
        (Join-Path $backupDirectory 'manifest.sha256'),
        $manifest,
        [Text.Encoding]::ASCII)
}
catch {
    $originalError = $_
    if ($mutationStarted) {
        foreach ($record in $records) {
            $serverBackup = Join-Path $backupDirectory $record.Name
            $activeHub = Join-Path $record.ScriptsDirectory 'hub.sk'
            $disabledHub = Join-Path $record.ScriptsDirectory '-hub.sk'
            $broadcast = Join-Path $record.ScriptsDirectory 'broadcast.sk'
            Remove-Item `
                -LiteralPath $activeHub,$disabledHub `
                -Force `
                -ErrorAction SilentlyContinue
            if ($record.ActiveHub) {
                Copy-Item `
                    -LiteralPath (Join-Path $serverBackup 'hub.sk') `
                    -Destination $activeHub
            }
            if ($record.DisabledHub) {
                Copy-Item `
                    -LiteralPath (Join-Path $serverBackup '-hub.sk') `
                    -Destination $disabledHub
            }
            if ($record.Broadcast) {
                Copy-Item `
                    -LiteralPath (Join-Path $serverBackup 'broadcast.sk') `
                    -Destination $broadcast `
                    -Force
            }
        }
    }
    throw $originalError
}

[pscustomobject]@{
    Changed = $true
    BackupDirectory = $backupDirectory
    Servers = @(
        $records | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                HubDisabled = $_.ActiveHub -or $_.DisabledHub
                BroadcastChanged = $_.BroadcastChanged
            }
        }
    )
} | ConvertTo-Json -Depth 4 -Compress
