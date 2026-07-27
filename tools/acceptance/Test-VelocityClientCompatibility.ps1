[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MinecraftUuid,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{3,16}$')]
    [string]$MinecraftName,

    [string]$ConfigurationPath =
        'E:\Velocity\plugins\hechao-velocity-authorizer\config.properties'
)

$ErrorActionPreference = 'Stop'

function Read-Properties {
    param([string]$Path)

    $properties = @{}
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $separator = $trimmed.IndexOf('=')
        if ($separator -le 0) {
            throw "Invalid property line in $Path."
        }

        $key = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        $properties[$key] = $value
    }

    return $properties
}

$configuration = Read-Properties -Path $ConfigurationPath
$endpoint = $configuration['api-url']
$token = $configuration['token']
$proxyInstance = $configuration['proxy-instance']
if ([string]::IsNullOrWhiteSpace($endpoint) -or
    [string]::IsNullOrWhiteSpace($token) -or
    [string]::IsNullOrWhiteSpace($proxyInstance)) {
    throw 'Velocity authorization configuration is incomplete.'
}

$cases = @(
    @{ Session = 'lobby'; Target = 'survival2'; Expected = 'Allowed' },
    @{ Session = 'lobby'; Target = 'survival1'; Expected = 'Allowed' },
    @{ Session = 'lobby'; Target = 'activity'; Expected = 'ClientProfileMismatch' },
    @{ Session = 'lobby'; Target = 'pvp'; Expected = 'MinecraftVersionMismatch' },
    @{ Session = 'activity'; Target = 'lobby'; Expected = 'Allowed' },
    @{ Session = 'activity'; Target = 'activity'; Expected = 'Allowed' },
    @{ Session = 'pvp'; Target = 'pvp'; Expected = 'Allowed' },
    @{ Session = 'pvp'; Target = 'lobby'; Expected = 'MinecraftVersionMismatch' }
)

$headers = @{
    Accept = 'application/json'
    'X-Hechao-Velocity-Token' = $token
}
$results = foreach ($case in $cases) {
    $body = @{
        minecraftUuid = $MinecraftUuid
        minecraftName = $MinecraftName
        velocityTarget = $case.Target
        initialConnection = $false
        remoteAddress = '127.0.0.1'
        proxyInstance = $proxyInstance
        sessionServerId = $case.Session
    } | ConvertTo-Json -Compress

    $response = Invoke-RestMethod `
        -Uri $endpoint `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $body `
        -TimeoutSec 10

    [pscustomobject]@{
        SessionServer = $case.Session
        TargetServer = $case.Target
        ExpectedReason = $case.Expected
        ActualReason = [string]$response.reason
        Allowed = [bool]$response.allowed
        Passed = [string]$response.reason -eq $case.Expected
    }
}

$results | ConvertTo-Json -Depth 3
if ($results.Passed -contains $false) {
    exit 1
}
