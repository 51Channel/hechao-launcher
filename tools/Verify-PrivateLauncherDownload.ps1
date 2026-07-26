[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublisherResultPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$ExpectedBytes
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$resultPath = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($PublisherResultPath))
if (-not [System.IO.File]::Exists($resultPath)) {
    throw 'The protected publisher result does not exist.'
}

$resultText = [System.IO.File]::ReadAllText($resultPath)
$urlMatch = [regex]::Match($resultText, 'https://[^\s]+')
if (-not $urlMatch.Success) {
    throw 'The protected publisher result does not contain a signed download link.'
}

$temporaryPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "hechao-launcher-verify-$([guid]::NewGuid().ToString('N')).exe")
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(10)
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = $client.GetAsync(
        $urlMatch.Value,
        [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    $statusCode = [int]$response.StatusCode
    if ($statusCode -ne 200) {
        throw "The signed download returned HTTP $statusCode."
    }

    $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $output = [System.IO.FileStream]::new(
        $temporaryPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        128KB,
        [System.IO.FileOptions]::SequentialScan)
    try {
        $input.CopyTo($output)
    }
    finally {
        $output.Dispose()
        $input.Dispose()
    }

    $stopwatch.Stop()
    $actualBytes = ([System.IO.FileInfo]::new($temporaryPath)).Length
    $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
    if ($actualBytes -ne $ExpectedBytes) {
        throw "The signed download length is $actualBytes, expected $ExpectedBytes."
    }

    if (-not [string]::Equals(
            $actualSha256,
            $ExpectedSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The signed download SHA-256 does not match the release artifact.'
    }

    [pscustomobject]@{
        SignedStatus = 200
        DownloadedBytes = $actualBytes
        DownloadedSha256 = $actualSha256
        ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
    }
}
finally {
    $client.Dispose()
    [System.IO.File]::Delete($temporaryPath)
}
