#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CatalogPath,

    [uri]$ApiBaseUrl = "https://launcher-api.hechao.world/",

    [string]$ServerId = "activity-survival",

    [string]$TokenFile,

    [Guid]$ActorUuid = "4e3522e2-6f50-45ca-a5b1-84579d225bf2",

    [string]$ActorName = "skyrealm-catalog-v2-publisher",

    [ValidateSet("Validate", "Preview", "Apply", "Disable")]
    [string]$Mode = "Validate",

    [ValidateRange(1, 300)]
    [int]$RequestTimeoutSeconds = 15,

    [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$invariant = [Globalization.CultureInfo]::InvariantCulture

if (-not (Test-Path -LiteralPath $CatalogPath -PathType Leaf)) {
    throw "Catalog file does not exist: $CatalogPath"
}
if ($ApiBaseUrl.Scheme -ne "https" -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.UserInfo) -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.Query) -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.Fragment) -or
    $ApiBaseUrl.AbsolutePath -ne "/") {
    throw "ApiBaseUrl must be a plain HTTPS origin."
}
if ($ServerId -notmatch "^[a-z0-9][a-z0-9._-]{1,63}$") {
    throw "ServerId is invalid."
}
if ($ActorUuid -eq [Guid]::Empty -or
    [string]::IsNullOrWhiteSpace($ActorName) -or
    $ActorName.Length -gt 64 -or
    $ActorName.ToCharArray().Where({ [char]::IsControl($_) }).Count -gt 0) {
    throw "Catalog actor is invalid."
}

$rowPattern = [regex]::new(
    '^\|\s*(?<name>[^|]+?)\s*\|\s*`(?<id>[a-z0-9_.-]+:[a-z0-9_./-]+)`\s*\|' +
    '\s*`(?<price>[0-9]+(?:\.[0-9]{1,2})?)`\s*\|' +
    '\s*`(?<personal>[0-9,]+)`\s*\|\s*`(?<server>[0-9,]+)`\s*\|' +
    '\s*`(?<income>[0-9,.]+)`\s*\|$',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)

$products = @(
    foreach ($line in Get-Content -LiteralPath $CatalogPath) {
        $match = $rowPattern.Match($line)
        if (-not $match.Success) {
            continue
        }

        $price = [decimal]::Parse($match.Groups["price"].Value, $invariant)
        $personalLimit = [int]::Parse(
            $match.Groups["personal"].Value.Replace(",", ""),
            $invariant)
        $serverLimit = [int]::Parse(
            $match.Groups["server"].Value.Replace(",", ""),
            $invariant)
        $documentedIncome = [decimal]::Parse(
            $match.Groups["income"].Value.Replace(",", ""),
            $invariant)

        [pscustomobject]@{
            name = $match.Groups["name"].Value.Trim()
            itemId = $match.Groups["id"].Value
            unitPrice = $price
            personalDailyLimit = $personalLimit
            serverDailyLimit = $serverLimit
            documentedPersonalIncome = $documentedIncome
        }
    }
)

if ($products.Count -ne 85) {
    throw "Expected 85 catalog rows, found $($products.Count)."
}
$uniqueIds = @($products.itemId | Sort-Object -Unique)
if ($uniqueIds.Count -ne $products.Count) {
    throw "Catalog contains duplicate item IDs."
}

foreach ($product in $products) {
    if ($product.itemId -notmatch "^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$") {
        throw "Invalid item ID: $($product.itemId)"
    }
    if ($product.unitPrice -le 0 -or
        [decimal]::Round($product.unitPrice, 2) -ne $product.unitPrice) {
        throw "Invalid unit price for $($product.itemId)."
    }
    if ($product.personalDailyLimit -lt 1 -or
        $product.serverDailyLimit -ne $product.personalDailyLimit * 20) {
        throw "Invalid daily limits for $($product.itemId)."
    }
    $calculatedIncome = [decimal]::Round(
        $product.unitPrice * $product.personalDailyLimit,
        2)
    if ($calculatedIncome -ne $product.documentedPersonalIncome) {
        throw "Documented income is inconsistent for $($product.itemId)."
    }
}

$personalTheoreticalMaximum = [decimal](
    $products |
        Measure-Object -Property documentedPersonalIncome -Sum
).Sum
$serverTheoreticalMaximum = [decimal](
    $products |
        ForEach-Object {
            [decimal]::Round($_.unitPrice * $_.serverDailyLimit, 2)
        } |
        Measure-Object -Sum
).Sum
if ($personalTheoreticalMaximum -ne [decimal]"11056.00" -or
    $serverTheoreticalMaximum -ne [decimal]"221120.00") {
    throw "Catalog aggregate limits do not match the reviewed v2 document."
}

$sourceHash = (Get-FileHash -LiteralPath $CatalogPath -Algorithm SHA256).Hash
$result = [ordered]@{
    schemaVersion = 1
    capturedAt = [DateTimeOffset]::UtcNow.ToString("O")
    mode = $Mode
    sourcePath = [IO.Path]::GetFileName($CatalogPath)
    sourceSha256 = $sourceHash
    serverId = $ServerId
    actorUuid = $ActorUuid.ToString("D")
    actorName = $ActorName
    productCount = $products.Count
    uniqueItemCount = $uniqueIds.Count
    personalTheoreticalMaximum = $personalTheoreticalMaximum
    serverTheoreticalMaximum = $serverTheoreticalMaximum
    previousProductCount = $null
    previousEnabledCount = $null
    changedCount = 0
    verifiedCount = 0
    rollbackAttempted = $false
    rollbackSucceeded = $null
    status = "Validated"
}

function Write-Result {
    $json = $result | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $parent = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Set-Content -LiteralPath $EvidencePath -Value $json -Encoding utf8NoBOM
    }
    $json
}

if ($Mode -eq "Validate") {
    Write-Result
    return
}
if ([string]::IsNullOrWhiteSpace($TokenFile) -or
    -not (Test-Path -LiteralPath $TokenFile -PathType Leaf)) {
    throw "TokenFile is required for Preview, Apply, and Disable modes."
}

$token = (Get-Content -LiteralPath $TokenFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "TokenFile is empty."
}
$headers = @{
    Authorization = "Bearer $token"
    "X-Hechao-Server-Id" = $ServerId
}
$catalogUri = [uri]::new(
    $ApiBaseUrl,
    "v1/internal/economy/products?includeDisabled=true")

function Invoke-CatalogRequest {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("Get", "Put", "Post")]
        [string]$Method,

        [Parameter(Mandatory)]
        [uri]$Uri,

        [object]$Body
    )

    try {
        $parameters = @{
            Uri = $Uri
            Method = $Method
            Headers = $headers
            TimeoutSec = $RequestTimeoutSeconds
        }
        if ($null -ne $Body) {
            $parameters.ContentType = "application/json"
            $parameters.Body = $Body | ConvertTo-Json -Compress
        }
        Invoke-RestMethod @parameters
    } catch {
        $statusCode = if ($null -ne $_.Exception.Response) {
            [int]$_.Exception.Response.StatusCode
        } else {
            0
        }
        $details = if ([string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $_.Exception.Message
        } else {
            $_.ErrorDetails.Message
        }
        throw "Economy API request failed ($statusCode): $details"
    }
}

function Get-CatalogProducts {
    $response = Invoke-CatalogRequest -Method Get -Uri $catalogUri
    foreach ($product in @($response)) {
        $product
    }
}

function Set-CatalogProduct {
    param(
        [Parameter(Mandatory)]
        [object]$Product,

        [bool]$Enabled = $true
    )

    $itemSegment = [uri]::EscapeDataString([string]$Product.itemId)
    $productUri = [uri]::new(
        $ApiBaseUrl,
        "v1/internal/economy/products/$itemSegment")
    $body = [ordered]@{
        unitPrice = [decimal]$Product.unitPrice
        personalDailyLimit = [int]$Product.personalDailyLimit
        serverDailyLimit = [int]$Product.serverDailyLimit
        actorUuid = $ActorUuid
        actorName = $ActorName
    }
    $null = Invoke-CatalogRequest -Method Put -Uri $productUri -Body $body
    if (-not $Enabled) {
        Disable-CatalogProduct -ItemId ([string]$Product.itemId)
    }
}

function Disable-CatalogProduct {
    param(
        [Parameter(Mandatory)]
        [string]$ItemId
    )

    $itemSegment = [uri]::EscapeDataString($ItemId)
    $disableUri = [uri]::new(
        $ApiBaseUrl,
        "v1/internal/economy/products/$itemSegment/disable")
    $body = [ordered]@{
        actorUuid = $ActorUuid
        actorName = $ActorName
    }
    $null = Invoke-CatalogRequest -Method Post -Uri $disableUri -Body $body
}

function Test-ProductMatches {
    param(
        [Parameter(Mandatory)]
        [object]$Expected,

        [Parameter(Mandatory)]
        [object]$Actual,

        [bool]$ExpectedEnabled = $true
    )

    [string]$Actual.itemId -eq [string]$Expected.itemId -and
    [decimal]$Actual.unitPrice -eq [decimal]$Expected.unitPrice -and
    [int]$Actual.personalDailyLimit -eq [int]$Expected.personalDailyLimit -and
    [int]$Actual.serverDailyLimit -eq [int]$Expected.serverDailyLimit -and
    [bool]$Actual.enabled -eq $ExpectedEnabled
}

try {
    $before = @(Get-CatalogProducts)
    $result.previousProductCount = $before.Count
    $result.previousEnabledCount = @(
        $before | Where-Object { [bool]$_.enabled }
    ).Count
    $beforeById = @{}
    foreach ($product in $before) {
        $beforeById[[string]$product.itemId] = $product
    }

    if ($Mode -eq "Preview") {
        $result.changedCount = @(
            $products | Where-Object {
                $actual = $beforeById[[string]$_.itemId]
                $null -eq $actual -or
                -not (Test-ProductMatches -Expected $_ -Actual $actual)
            }
        ).Count
        $result.status = "Previewed"
        Write-Result
        return
    }

    $appliedIds = [Collections.Generic.List[string]]::new()
    try {
        foreach ($product in $products) {
            if ($Mode -eq "Apply") {
                $existing = $beforeById[[string]$product.itemId]
                if ($null -ne $existing -and
                    (Test-ProductMatches -Expected $product -Actual $existing)) {
                    continue
                }
                Set-CatalogProduct -Product $product
            } else {
                $existing = $beforeById[[string]$product.itemId]
                if ($null -ne $existing -and [bool]$existing.enabled) {
                    Disable-CatalogProduct -ItemId ([string]$product.itemId)
                } else {
                    continue
                }
            }
            $appliedIds.Add([string]$product.itemId)
        }
        $result.changedCount = $appliedIds.Count
    } catch {
        $result.rollbackAttempted = $true
        try {
            $rollbackIds = @($appliedIds)
            [array]::Reverse($rollbackIds)
            foreach ($itemId in $rollbackIds) {
                $old = $beforeById[$itemId]
                if ($null -eq $old) {
                    Disable-CatalogProduct -ItemId $itemId
                } else {
                    Set-CatalogProduct -Product $old -Enabled ([bool]$old.enabled)
                }
            }
            $result.rollbackSucceeded = $true
        } catch {
            $result.rollbackSucceeded = $false
            throw "Catalog update failed and automatic rollback also failed: $($_.Exception.Message)"
        }
        throw
    }

    $after = @(Get-CatalogProducts)
    $afterById = @{}
    foreach ($product in $after) {
        $afterById[[string]$product.itemId] = $product
    }
    $expectedEnabled = $Mode -eq "Apply"
    foreach ($product in $products) {
        $actual = $afterById[[string]$product.itemId]
        if ($null -eq $actual -or
            -not (Test-ProductMatches `
                -Expected $product `
                -Actual $actual `
                -ExpectedEnabled $expectedEnabled)) {
            throw "Post-write verification failed for $($product.itemId)."
        }
        $result.verifiedCount++
    }
    $result.status = if ($Mode -eq "Apply") { "Enabled" } else { "Disabled" }
    Write-Result
} finally {
    $token = $null
    $headers.Authorization = $null
}
