[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [ValidateSet("first", "verify")]
    [string]$Run = "first",

    [ValidateRange(5, 1440)]
    [int]$LinkMinutes = 1440
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publisher = Join-Path $repoRoot (
    "artifacts\publish\publisher-win-x64-0.9.0-provenance-rebuild\Hechao.Publisher.exe")
$expectedPublisherSha256 =
    "947480D3B3566542AECD84246B6C8C6CEE1128D7A3CE1675ED4C0D9089F60C93"
$installer = Join-Path $repoRoot (
    "artifacts\installer\Hechao-Launcher-Setup-$Version-win-x64.exe")
$secretRoot = Join-Path $env:LOCALAPPDATA "HechaoLauncherAdmin\secrets"
$credential = Join-Path $secretRoot "oss-publisher-credential.dpapi"
$resultPath = Join-Path $secretRoot "launcher-release-$Version-$Run.txt"

foreach ($requiredPath in @($publisher, $installer, $credential)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release input is missing: $requiredPath"
    }
}

$actualPublisherSha256 = (
    Get-FileHash -LiteralPath $publisher -Algorithm SHA256
).Hash
if (-not [string]::Equals(
        $actualPublisherSha256,
        $expectedPublisherSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "The publisher SHA-256 does not match the approved 0.9.0 rebuild."
}

$actualSha256 = (
    Get-FileHash -LiteralPath $installer -Algorithm SHA256
).Hash
if (-not [string]::Equals(
        $actualSha256,
        $ExpectedSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "The installer SHA-256 does not match the requested release."
}

$arguments = @(
    "upload-launcher-release",
    "--installer", $installer,
    "--version", $Version,
    "--sha256", $ExpectedSha256,
    "--bucket", "hechaoworld",
    "--region", "cn-shanghai",
    "--endpoint", "https://oss-cn-shanghai.aliyuncs.com",
    "--download-endpoint", "https://hechaoworld.oss-cn-shanghai.aliyuncs.com",
    "--credential-dpapi", $credential,
    "--dpapi-entropy-label", "HechaoLauncherAdmin/OssPublisherCredential/v1",
    "--link-minutes", $LinkMinutes.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
)

$publisherOutput = @(& $publisher @arguments 2>&1)
$exitCode = $LASTEXITCODE
$safeOutput = @(
    $publisherOutput |
        ForEach-Object {
            [regex]::Replace(
                [string]$_,
                'https://[^\s]+',
                '[private-link-redacted]')
        }
)
if ($exitCode -ne 0) {
    throw "Publisher failed with exit code $exitCode.`n$($safeOutput -join "`n")"
}

[IO.File]::WriteAllLines(
    $resultPath,
    [string[]]$publisherOutput,
    [Text.UTF8Encoding]::new($false))

$resultText = [IO.File]::ReadAllText($resultPath)
if (-not [regex]::IsMatch($resultText, 'https://[^\s]+')) {
    throw "The publisher result does not contain a private download link."
}

$status = [string]$publisherOutput[0]
$objectKey = (
    $publisherOutput |
        Where-Object { [string]$_ -like "Object: *" } |
        Select-Object -First 1
) -replace '^Object:\s*', ''
$expiresText = (
    $publisherOutput |
        Where-Object {
            [string]$_ -like "Internal download link expires: *"
        } |
        Select-Object -First 1
) -replace '^Internal download link expires:\s*', ''

[pscustomobject]@{
    Version = $Version
    Run = $Run
    UploadStatus = $status
    ObjectKey = $objectKey
    PrivateLinkExpiresAt = $expiresText
    InstallerBytes = (Get-Item -LiteralPath $installer).Length
    InstallerSha256 = $actualSha256
    ProtectedResult = $resultPath
    ProtectedResultBytes = (Get-Item -LiteralPath $resultPath).Length
    PrivateLinkCaptured = $true
}
