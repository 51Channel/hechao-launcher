[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory)]
    [ValidateRange(1, 2147483647)]
    [long]$ExpectedBytes,

    [ValidateSet('first', 'verify')]
    [string]$Run = 'verify',

    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$ExpectedDownloadHost = 'download.hechao.world'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validationRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot 'artifacts\validation'))
$secretRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'HechaoLauncherAdmin\secrets'))
$resultPath = Join-Path $secretRoot "launcher-release-$Version-$Run.txt"
$fileName = "Hechao-Launcher-Setup-$Version-win-x64.exe"
$objectPath = "/releases/launcher/$Version/$fileName"
$expectedHost = $ExpectedDownloadHost.Trim().ToLowerInvariant()
$anonymousUri = [Uri]::new("https://$expectedHost$objectPath")
$temporaryPath = Join-Path $validationRoot (
    "launcher-$Version-private-download-" + [Guid]::NewGuid().ToString('N') + '.tmp')

function Assert-ProtectedResultAcl {
    param([Parameter(Mandatory)][string]$Path)

    $allowedSids = @(
        'S-1-5-18',
        'S-1-5-32-544',
        [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    ) | Sort-Object -Unique
    $acl = Get-Acl -LiteralPath $Path
    $rules = @($acl.Access)
    if ($rules.Count -eq 0) {
        throw 'The protected publisher result has no access rules.'
    }
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Translate(
            [Security.Principal.SecurityIdentifier]).Value
        if ($allowedSids -notcontains $sid -or
            $rule.AccessControlType -ne
                [Security.AccessControl.AccessControlType]::Allow) {
            throw 'The protected publisher result has an unexpected access rule.'
        }
    }

    if (-not $acl.AreAccessRulesProtected) {
        $parent = [IO.Path]::GetFullPath(
            (Split-Path -Parent $Path))
        if (-not $parent.Equals(
                $secretRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The publisher result inherits outside the protected secret root.'
        }

        $parentAcl = Get-Acl -LiteralPath $parent
        if (-not $parentAcl.AreAccessRulesProtected) {
            throw 'The publisher secret root still inherits access rules.'
        }
        foreach ($rule in @($parentAcl.Access)) {
            $sid = $rule.IdentityReference.Translate(
                [Security.Principal.SecurityIdentifier]).Value
            if ($allowedSids -notcontains $sid -or
                $rule.AccessControlType -ne
                    [Security.AccessControl.AccessControlType]::Allow) {
                throw 'The publisher secret root has an unexpected access rule.'
            }
        }
    }
}

function Copy-BoundedStream {
    param(
        [Parameter(Mandatory)][IO.Stream]$InputStream,
        [Parameter(Mandatory)][IO.Stream]$OutputStream,
        [Parameter(Mandatory)][long]$ExpectedLength
    )

    $buffer = [byte[]]::new(128 * 1024)
    [long]$total = 0
    while (($read = $InputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $total += $read
        if ($total -gt $ExpectedLength) {
            throw 'The private download exceeded the expected length.'
        }
        $OutputStream.Write($buffer, 0, $read)
    }

    if ($total -ne $ExpectedLength) {
        throw "Expected $ExpectedLength downloaded bytes, received $total."
    }
}

if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    throw "Protected publisher result is missing: $resultPath"
}
[IO.Directory]::CreateDirectory($validationRoot) | Out-Null
Assert-ProtectedResultAcl -Path $resultPath

$resultText = [IO.File]::ReadAllText($resultPath)
$signedUri = @(
    [regex]::Matches($resultText, 'https://[^\s]+') |
        ForEach-Object {
            $candidate = [Uri]::new($_.Value)
            if ($candidate.Scheme -eq 'https' -and
                $candidate.Host -eq $expectedHost -and
                $candidate.AbsolutePath -eq $objectPath -and
                [string]::IsNullOrEmpty($candidate.UserInfo)) {
                $candidate
            }
        }
) | Select-Object -First 1
if ($null -eq $signedUri) {
    throw 'The protected publisher result has no valid private download URL.'
}

Add-Type -AssemblyName System.Net.Http
$handler = [Net.Http.HttpClientHandler]::new()
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(5)
try {
    $anonymousResponse = $client.GetAsync(
        $anonymousUri,
        [Net.Http.HttpCompletionOption]::ResponseHeadersRead
    ).GetAwaiter().GetResult()
    try {
        $anonymousStatus = [int]$anonymousResponse.StatusCode
    }
    finally {
        $anonymousResponse.Dispose()
    }
    if ($anonymousStatus -ne 403) {
        throw "Anonymous release request returned HTTP $anonymousStatus."
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $signedResponse = $client.GetAsync(
        $signedUri,
        [Net.Http.HttpCompletionOption]::ResponseHeadersRead
    ).GetAwaiter().GetResult()
    try {
        $signedStatus = [int]$signedResponse.StatusCode
        if (-not $signedResponse.IsSuccessStatusCode) {
            throw "Private release request returned HTTP $signedStatus."
        }
        $contentLength = $signedResponse.Content.Headers.ContentLength
        if ($null -ne $contentLength -and
            [long]$contentLength -ne $ExpectedBytes) {
            throw 'The private release Content-Length is unexpected.'
        }

        $inputStream = $signedResponse.Content.ReadAsStreamAsync(
        ).GetAwaiter().GetResult()
        try {
            $outputStream = [IO.FileStream]::new(
                $temporaryPath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None)
            try {
                Copy-BoundedStream `
                    -InputStream $inputStream `
                    -OutputStream $outputStream `
                    -ExpectedLength $ExpectedBytes
            }
            finally {
                $outputStream.Dispose()
            }
        }
        finally {
            $inputStream.Dispose()
        }
    }
    finally {
        $signedResponse.Dispose()
    }
    $stopwatch.Stop()

    $downloadedSha256 = (
        Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256
    ).Hash
    if (-not [string]::Equals(
            $downloadedSha256,
            $ExpectedSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The private release SHA-256 does not match.'
    }

    [pscustomobject]@{
        Version = $Version
        ProtectedResultAclRestricted = $true
        AnonymousStatus = $anonymousStatus
        SignedStatus = $signedStatus
        DownloadedBytes = (Get-Item -LiteralPath $temporaryPath).Length
        DownloadedSha256 = $downloadedSha256
        ElapsedSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        PrivateUrlDisclosed = $false
    }
}
finally {
    $client.Dispose()
    $handler.Dispose()
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
