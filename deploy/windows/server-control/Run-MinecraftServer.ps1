[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$ServerId,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [Parameter(Mandatory)]
    [string]$StartScript,

    [string]$RuntimeMarkerDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent\runtime"
)

$ErrorActionPreference = 'Stop'

$resolvedDirectory = (Resolve-Path -LiteralPath $ServerDirectory).Path
$resolvedStartScript = (Resolve-Path -LiteralPath $StartScript).Path
if (-not $resolvedStartScript.StartsWith(
        "$resolvedDirectory\",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'StartScript must be inside ServerDirectory.'
}

$scriptText = [System.IO.File]::ReadAllText($resolvedStartScript)
if ($scriptText -notmatch '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*(?:\r)?$') {
    throw 'StartScript is not configured for a managed start.'
}

$resolvedMarkerDirectory = [System.IO.Path]::GetFullPath(
    $RuntimeMarkerDirectory
)
[System.IO.Directory]::CreateDirectory($resolvedMarkerDirectory) | Out-Null
$markerPath = Join-Path $resolvedMarkerDirectory "$ServerId.json"
$temporaryMarkerPath = Join-Path $resolvedMarkerDirectory (
    ".$ServerId-$([Guid]::NewGuid().ToString('N')).tmp"
)
$runId = [Guid]::NewGuid().ToString('N')
$runner = Get-Process -Id $PID
$marker = [ordered]@{
    schemaVersion = 1
    serverId = $ServerId
    runId = $runId
    runnerProcessId = $PID
    runnerStartedAtUtcTicks = $runner.StartTime.ToUniversalTime().Ticks
    serverDirectory = $resolvedDirectory
    startedAt = (Get-Date).ToUniversalTime().ToString('o')
}
[System.IO.File]::WriteAllText(
    $temporaryMarkerPath,
    ($marker | ConvertTo-Json -Compress),
    [System.Text.UTF8Encoding]::new($false)
)
[System.IO.File]::Move($temporaryMarkerPath, $markerPath, $true)

$exitCode = 1
try {
    $env:HECHAO_MANAGED_START = '1'
    Set-Location -LiteralPath $resolvedDirectory
    & cmd.exe /d /c "`"$resolvedStartScript`""
    $exitCode = $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $temporaryMarkerPath) {
        Remove-Item -LiteralPath $temporaryMarkerPath -Force
    }
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $currentMarker = Get-Content -Raw -LiteralPath $markerPath |
                ConvertFrom-Json
            if ([string]$currentMarker.runId -eq $runId) {
                Remove-Item -LiteralPath $markerPath -Force
            }
        }
        catch {
            # Keep an unrecognized marker so the agent fails closed.
        }
    }
}

exit $exitCode
