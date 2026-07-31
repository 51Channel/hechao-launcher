[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$ServerDirectories,

    [string]$StartScript = 'start.bat',

    [string]$BackupRoot = 'E:\manual-backups'
)

$ErrorActionPreference = 'Stop'
$ascii = [System.Text.Encoding]::ASCII
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $BackupRoot "managed-start-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$results = foreach ($serverDirectory in $ServerDirectories) {
    $resolvedDirectory = (Resolve-Path -LiteralPath $serverDirectory).Path
    $scriptPath = (Resolve-Path -LiteralPath (
        Join-Path $resolvedDirectory $StartScript
    )).Path
    $serverName = Split-Path -Leaf $resolvedDirectory
    $backupPath = Join-Path $backupDirectory "$serverName-$StartScript"
    $beforeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $scriptPath).Hash
    $text = [System.IO.File]::ReadAllText($scriptPath, $ascii)

    if ($text -match '[^\u0000-\u007f]') {
        throw "Start script must be ASCII before automatic editing: $scriptPath"
    }

    $standalonePausePattern = '(?im)^([ \t]*)pause([ \t]*)(\r?)$'
    $managedMarkerPattern = '(?im)HECHAO_MANAGED_START'
    $pauseMatches = [regex]::Matches($text, $standalonePausePattern)
    $changed = $pauseMatches.Count -gt 0

    if ($changed) {
        Copy-Item -LiteralPath $scriptPath -Destination $backupPath -Force
        $updated = [regex]::Replace(
            $text,
            $standalonePausePattern,
            '$1if not defined HECHAO_MANAGED_START pause$2$3')
        [System.IO.File]::WriteAllText($scriptPath, $updated, $ascii)
    }
    elseif ($text -notmatch $managedMarkerPattern) {
        Copy-Item -LiteralPath $scriptPath -Destination $backupPath -Force
        $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $separator = if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) {
            $newline
        }
        else {
            ''
        }
        $updated = "${text}${separator}rem HECHAO_MANAGED_START: headless script has no pause${newline}"
        [System.IO.File]::WriteAllText($scriptPath, $updated, $ascii)
        $changed = $true
    }

    $afterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $scriptPath).Hash
    $updatedText = [System.IO.File]::ReadAllText($scriptPath, $ascii)
    if ($updatedText -match '(?im)^[ \t]*pause[ \t]*(?:\r)?$') {
        throw "A standalone pause statement remains: $scriptPath"
    }
    if ($updatedText -notmatch $managedMarkerPattern) {
        throw "Managed-start marker is missing after editing: $scriptPath"
    }

    [pscustomobject]@{
        server_directory = $resolvedDirectory
        start_script = $scriptPath
        changed = $changed
        pauses_updated = $pauseMatches.Count
        before_sha256 = $beforeHash
        after_sha256 = $afterHash
        backup = if ($changed) { $backupPath } else { $null }
    }
}

[pscustomobject]@{
    backup_directory = $backupDirectory
    servers = @($results)
    server_action = 'none'
} | ConvertTo-Json -Depth 5 -Compress
