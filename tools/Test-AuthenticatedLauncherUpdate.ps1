[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedMinimumVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$PreviousVersion,

    [Parameter(Mandatory)]
    [ValidateRange(1048576, 536870912)]
    [long]$ExpectedBytes,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [string]$LauncherAssemblyDirectory,

    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$ExpectedDownloadHost = 'download.hechao.world'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($LauncherAssemblyDirectory)) {
    $LauncherAssemblyDirectory = Join-Path $repoRoot (
        'src\Hechao.Launcher\bin\Release\net10.0-windows')
}

$assemblyRoot = (Resolve-Path -LiteralPath $LauncherAssemblyDirectory).Path
foreach ($dependency in @('Hechao.Contracts.dll', 'Hechao.Distribution.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $assemblyRoot $dependency))
}

$launcherAssembly = [Reflection.Assembly]::LoadFrom(
    (Join-Path $assemblyRoot 'Hechao.Launcher.dll'))
$sessionStoreType = $launcherAssembly.GetType(
    'Hechao.Launcher.Services.DpapiSessionStore',
    $true)
$apiClientType = $launcherAssembly.GetType(
    'Hechao.Launcher.Services.LauncherApiClient',
    $true)
$updateServiceType = $launcherAssembly.GetType(
    'Hechao.Launcher.Services.LauncherUpdateService',
    $true)

$sessionStore = [Activator]::CreateInstance($sessionStoreType)
$createDefault = @(
    $apiClientType.GetMethods(
        [Reflection.BindingFlags]'Public,Static') |
        Where-Object {
            $_.Name -eq 'CreateDefault' -and
            $_.GetParameters().Count -eq 2
        }
)
if ($createDefault.Count -ne 1) {
    throw 'The launcher API client factory contract is unavailable.'
}

$apiClient = $createDefault[0].Invoke($null, @($sessionStore, $false))
$cancellationToken = [Threading.CancellationToken]::None
$account = $apiClient.TryRestoreSessionAsync(
    $cancellationToken).GetAwaiter().GetResult()
if ($null -eq $account) {
    throw 'The existing launcher session could not be restored.'
}

$release = $apiClient.GetLauncherUpdateAsync(
    $cancellationToken).GetAwaiter().GetResult()
if ($null -eq $release) {
    throw 'The production update endpoint returned no release.'
}

$expectedDigest = $ExpectedSha256.ToLowerInvariant()
if ($release.Version -ne $ExpectedVersion -or
    $release.MinimumSupportedVersion -ne $ExpectedMinimumVersion -or
    $release.InstallerBytes -ne $ExpectedBytes -or
    $release.InstallerSha256 -ne $expectedDigest) {
    throw 'The authenticated launcher update metadata is unexpected.'
}

$installerUri = [Uri]$release.InstallerUrl
if ($installerUri.Scheme -ne 'https' -or
    $installerUri.Host -ne $ExpectedDownloadHost) {
    throw 'The authenticated launcher installer origin is unexpected.'
}

$createPlan = $updateServiceType.GetMethod(
    'CreatePlan',
    [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $createPlan) {
    throw 'The launcher update plan contract is unavailable.'
}

$previousPlan = $createPlan.Invoke(
    $null,
    @($release, $PreviousVersion))
$currentPlan = $createPlan.Invoke(
    $null,
    @($release, $ExpectedVersion))
if ($null -eq $previousPlan) {
    throw 'The previous launcher version did not produce an update plan.'
}
if ($null -ne $currentPlan) {
    throw 'The current launcher version produced a duplicate update plan.'
}

$validationRoot = Join-Path $repoRoot 'artifacts\validation'
[IO.Directory]::CreateDirectory($validationRoot) | Out-Null
$temporaryPath = Join-Path $validationRoot (
    'authenticated-launcher-update-' + [Guid]::NewGuid().ToString('N') + '.tmp')
$handler = [Net.Http.SocketsHttpHandler]::new()
$handler.UseProxy = $false
$httpClient = [Net.Http.HttpClient]::new($handler)
$httpClient.Timeout = [TimeSpan]::FromMinutes(5)

try {
    try {
        $response = $httpClient.GetAsync(
            $installerUri,
            [Net.Http.HttpCompletionOption]::ResponseHeadersRead
        ).GetAwaiter().GetResult()
        try {
            $downloadStatus = [int]$response.StatusCode
            if (-not $response.IsSuccessStatusCode) {
                throw 'The authenticated installer request was rejected.'
            }

            $stream = $response.Content.ReadAsStreamAsync(
            ).GetAwaiter().GetResult()
            try {
                $output = [IO.File]::Open(
                    $temporaryPath,
                    [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write,
                    [IO.FileShare]::None)
                try {
                    $stream.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $response.Dispose()
        }
    }
    catch {
        throw 'The authenticated installer readback failed.'
    }

    $downloadedBytes = (Get-Item -LiteralPath $temporaryPath).Length
    $downloadedSha256 = (
        Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($downloadedBytes -ne $ExpectedBytes -or
        $downloadedSha256 -ne $expectedDigest) {
        throw 'The authenticated installer readback did not match the release.'
    }

    [pscustomobject]@{
        SessionRestored = $true
        LatestVersion = $release.Version
        MinimumSupportedVersion = $release.MinimumSupportedVersion
        PreviousVersionPlanAvailable = $null -ne $previousPlan
        CurrentVersionPlanAbsent = $null -eq $currentPlan
        DownloadStatus = $downloadStatus
        DownloadedBytes = $downloadedBytes
        DownloadedSha256 = $downloadedSha256
        SignedUrlDisclosed = $false
        AccountIdentityDisclosed = $false
    }
}
finally {
    $httpClient.Dispose()
    $handler.Dispose()
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
