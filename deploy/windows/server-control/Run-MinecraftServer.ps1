[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [Parameter(Mandatory)]
    [string]$StartScript
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

$env:HECHAO_MANAGED_START = '1'
Set-Location -LiteralPath $resolvedDirectory
& cmd.exe /d /c "`"$resolvedStartScript`""
exit $LASTEXITCODE
