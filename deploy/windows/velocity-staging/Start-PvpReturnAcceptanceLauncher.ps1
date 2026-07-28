[CmdletBinding()]
param(
    [string]$LauncherPath = 'D:\Hechao Launcher\Hechao.Launcher.exe',

    [string]$ApiBaseUrl = 'http://127.0.0.1:28093/',

    [string]$MinecraftEndpoint = '127.0.0.1:25589'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LauncherPath -PathType Leaf)) {
    throw "Hechao launcher is missing: $LauncherPath"
}

$apiUri = [uri]$ApiBaseUrl
if (-not $apiUri.IsLoopback -or
    $apiUri.Scheme -ne [uri]::UriSchemeHttp -or
    $apiUri.Port -ne 28093 -or
    $apiUri.AbsolutePath -ne '/') {
    throw 'Acceptance API must be exactly loopback HTTP port 28093.'
}

if ($MinecraftEndpoint -ne '127.0.0.1:25589') {
    throw 'Acceptance Minecraft endpoint must be exactly 127.0.0.1:25589.'
}

$requiredPorts = 28093, 25589
foreach ($port in $requiredPorts) {
    $listeners = @(
        Get-NetTCPConnection -State Listen -LocalPort $port `
            -ErrorAction SilentlyContinue |
            Where-Object {
                $_.LocalAddress -eq '127.0.0.1'
            }
    )
    if ($listeners.Count -ne 1) {
        throw "Expected exactly one loopback listener on port $port."
    }
}

$launcherProcesses = @(
    Get-CimInstance Win32_Process |
        Where-Object {
            $_.ExecutablePath -ieq $LauncherPath
        }
)
if ($launcherProcesses.Count -ne 0) {
    throw 'Close the existing Hechao launcher before isolated acceptance.'
}

$previousApi = $env:HECHAO_LAUNCHER_API_BASE_URL
$previousMinecraft = $env:HECHAO_MINECRAFT_SERVER_ENDPOINT
try {
    $env:HECHAO_LAUNCHER_API_BASE_URL = $apiUri.AbsoluteUri
    $env:HECHAO_MINECRAFT_SERVER_ENDPOINT = $MinecraftEndpoint
    $process = Start-Process -FilePath $LauncherPath -PassThru
}
finally {
    $env:HECHAO_LAUNCHER_API_BASE_URL = $previousApi
    $env:HECHAO_MINECRAFT_SERVER_ENDPOINT = $previousMinecraft
}

[pscustomobject]@{
    ProcessId = $process.Id
    Launcher = $LauncherPath
    Api = $apiUri.AbsoluteUri
    Minecraft = $MinecraftEndpoint
    Scope = 'Process only'
}
