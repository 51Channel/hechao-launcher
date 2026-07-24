[CmdletBinding(DefaultParameterSetName = "Sign")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Sign")]
    [ValidateNotNullOrEmpty()]
    [string]$Path,

    [Parameter(Mandatory = $true, ParameterSetName = "Validate")]
    [switch]$ValidateEnvironment,

    [string]$CertificateThumbprint = $env:HECHAO_SIGNING_CERT_THUMBPRINT,

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStoreLocation = "CurrentUser",

    [string]$TimestampUrl = $env:HECHAO_SIGNING_TIMESTAMP_URL,
    [string]$SignToolPath = $env:HECHAO_SIGNTOOL_PATH,
    [string]$Description = "Hechao Launcher",
    [string]$DescriptionUrl = "https://hechao.world"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $PSBoundParameters.ContainsKey("CertificateStoreLocation") -and
    -not [string]::IsNullOrWhiteSpace($env:HECHAO_SIGNING_CERT_STORE)) {
    if ($env:HECHAO_SIGNING_CERT_STORE -notin @("CurrentUser", "LocalMachine")) {
        throw "HECHAO_SIGNING_CERT_STORE must be CurrentUser or LocalMachine."
    }

    $CertificateStoreLocation = $env:HECHAO_SIGNING_CERT_STORE
}

function Resolve-SignTool {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "SignTool was not found at '$RequestedPath'."
        }

        return [System.IO.Path]::GetFullPath($RequestedPath)
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $repoToolRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot "..\artifacts\tools\windows-sdk-buildtools"))
    if (Test-Path -LiteralPath $repoToolRoot -PathType Container) {
        $candidate = Get-ChildItem `
            -LiteralPath $repoToolRoot `
            -Filter signtool.exe `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq "x64" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    $windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $windowsKitsRoot -PathType Container) {
        $candidate = Get-ChildItem `
            -LiteralPath $windowsKitsRoot `
            -Filter signtool.exe `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    throw "SignTool was not found. Install the Windows SDK signing tools or set HECHAO_SIGNTOOL_PATH."
}

function Resolve-CodeSigningCertificate {
    param(
        [string]$Thumbprint,
        [string]$StoreLocation
    )

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        throw "CertificateThumbprint is required."
    }

    $normalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    if ($normalizedThumbprint -notmatch '^[0-9A-F]{40}$') {
        throw "The signing certificate thumbprint must contain exactly 40 hexadecimal characters."
    }

    $storeRoot = "Cert:\$StoreLocation\My"
    $certificate = Get-Item `
        -LiteralPath "$storeRoot\$normalizedThumbprint" `
        -ErrorAction SilentlyContinue
    if ($null -eq $certificate) {
        throw "The signing certificate was not found in $storeRoot."
    }

    if (-not $certificate.HasPrivateKey) {
        throw "The signing certificate does not expose a private key."
    }

    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $enhancedKeyUsages = @(
        $certificate.EnhancedKeyUsageList |
            ForEach-Object { $_.ObjectId.Value }
    )
    if ($enhancedKeyUsages -notcontains $codeSigningOid) {
        throw "The selected certificate is not valid for code signing."
    }

    $keyUsageExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq "2.5.29.15" } |
        Select-Object -First 1
    if ($null -ne $keyUsageExtension) {
        $digitalSignatureUsage =
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature
        if (($keyUsageExtension.KeyUsages -band $digitalSignatureUsage) -eq 0) {
            throw "The selected certificate does not allow digital signatures."
        }
    }

    $now = Get-Date
    if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
        throw "The selected code-signing certificate is not currently valid."
    }

    $rsaPublicKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey(
        $certificate)
    if ($null -eq $rsaPublicKey) {
        throw "The code-signing certificate must use RSA for Windows Smart App Control compatibility."
    }
    $rsaPublicKey.Dispose()

    $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode =
            [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $chain.ChainPolicy.RevocationFlag =
            [System.Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
        $chain.ChainPolicy.VerificationFlags =
            [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
        $chain.ChainPolicy.UrlRetrievalTimeout = [System.TimeSpan]::FromSeconds(15)

        if (-not $chain.Build($certificate)) {
            $chainErrors = @(
                $chain.ChainStatus |
                    ForEach-Object { $_.Status.ToString() }
            ) -join ", "
            throw "The code-signing certificate chain is not valid: $chainErrors"
        }

        $rootCertificate = $chain.ChainElements[
            $chain.ChainElements.Count - 1].Certificate
        $trustedRoot = Get-Item `
            -LiteralPath "Cert:\LocalMachine\AuthRoot\$($rootCertificate.Thumbprint)" `
            -ErrorAction SilentlyContinue
        if ($null -eq $trustedRoot) {
            throw "The code-signing certificate does not chain to the Microsoft trusted root program."
        }
    }
    finally {
        $chain.Dispose()
    }

    return $certificate
}

function Resolve-AbsoluteHttpUri {
    param(
        [string]$Value,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name is required."
    }

    $uri = $null
    if (-not [System.Uri]::TryCreate(
            $Value,
            [System.UriKind]::Absolute,
            [ref]$uri) -or
        ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https")) {
        throw "$Name must be an absolute HTTP or HTTPS URL."
    }

    return $uri.AbsoluteUri
}

$certificate = Resolve-CodeSigningCertificate `
    -Thumbprint $CertificateThumbprint `
    -StoreLocation $CertificateStoreLocation
$resolvedSignTool = Resolve-SignTool -RequestedPath $SignToolPath
$signToolSignature = Get-AuthenticodeSignature -LiteralPath $resolvedSignTool
if ($signToolSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $null -eq $signToolSignature.SignerCertificate -or
    $signToolSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw "SignTool must have a valid Microsoft Authenticode signature."
}
$resolvedTimestampUrl = Resolve-AbsoluteHttpUri `
    -Value $TimestampUrl `
    -Name "TimestampUrl"
$resolvedDescriptionUrl = Resolve-AbsoluteHttpUri `
    -Value $DescriptionUrl `
    -Name "DescriptionUrl"

if ($ValidateEnvironment) {
    Write-Host "Code-signing environment is ready."
    Write-Host "Certificate: $($certificate.Subject)"
    Write-Host "SignTool:    $resolvedSignTool"
    return
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "The artifact to sign was not found at '$Path'."
}

$artifactPath = (Resolve-Path -LiteralPath $Path).Path
$signArguments = @(
    "sign",
    "/sha1", $certificate.Thumbprint,
    "/s", "My"
)
if ($CertificateStoreLocation -eq "LocalMachine") {
    $signArguments += "/sm"
}
$signArguments += @(
    "/fd", "SHA256",
    "/tr", $resolvedTimestampUrl,
    "/td", "SHA256",
    "/d", $Description,
    "/du", $resolvedDescriptionUrl,
    "/v",
    $artifactPath
)

& $resolvedSignTool @signArguments
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed to sign '$artifactPath' with exit code $LASTEXITCODE."
}

& $resolvedSignTool verify /pa /all /v $artifactPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool could not verify '$artifactPath' with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -LiteralPath $artifactPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "The Authenticode signature is not valid: $($signature.StatusMessage)"
}

if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "The artifact signer does not match the requested certificate."
}

if ($null -eq $signature.TimeStamperCertificate) {
    throw "The Authenticode signature does not contain a trusted timestamp."
}

Write-Host "Signed:      $artifactPath"
Write-Host "Publisher:   $($signature.SignerCertificate.Subject)"
Write-Host "Timestamped: $($signature.TimeStamperCertificate.Subject)"
