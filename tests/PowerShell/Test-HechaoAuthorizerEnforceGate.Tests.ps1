#requires -Version 7.4

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gateScript = Join-Path $repositoryRoot (
    "tools\acceptance\Test-HechaoAuthorizerEnforceGate.ps1"
)
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "hechao-enforce-gate-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

function New-PassingEvidence {
    $tiers = @(
        "Member",
        "Participant",
        "Collaborator",
        "Administrator"
    )
    $deniedReasons = @(
        "LaunchGrantRequired",
        "InsufficientTier",
        "AccessDenied",
        "ServerUnavailable"
    )

    return [ordered]@{
        schemaVersion = 2
        status = "passed"
        stage = "5"
        target = "activity"
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        safeProgression = $true
        technicalBlockerCount = 0
        externalBlockerCount = 0
        requiredAuthorizationTiers = $tiers
        requiredDeniedReasons = $deniedReasons
        authorization = [ordered]@{
            byTier = @(
                $tiers | ForEach-Object {
                    [ordered]@{
                        tier = $_
                        successfulIdentityCount = 1
                    }
                }
            )
            successByTarget = @(
                [ordered]@{
                    target = "activity"
                    successfulIdentityCount = 5
                }
            )
            deniedByReason = @(
                $deniedReasons | ForEach-Object {
                    [ordered]@{
                        reason = $_
                        deniedCount = 1
                    }
                }
            )
        }
        authorizer = [ordered]@{
            mode = "monitor"
            expectedMode = "monitor"
            lobbyServerIp = "127.0.0.1"
            lobbyWhitelistEnabled = "true"
            lobbyEnforceWhitelist = "true"
            lobbyWhitelistEntries = 0
        }
        targets = @(
            [ordered]@{
                target = "activity"
                role = "Player"
                maximumOnlinePlayers = 5
            },
            [ordered]@{
                target = "lobby"
                role = "Infrastructure"
                maximumOnlinePlayers = 0
            }
        )
        finalCriticalAlerts = @()
    }
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory)]
        [object]$Evidence,

        [Parameter(Mandatory)]
        [string]$Name,

        [ValidateSet("monitor", "enforce")]
        [string]$ExpectedMode = "monitor"
    )

    $path = Join-Path $temporaryRoot "$Name.json"
    [IO.File]::WriteAllText(
        $path,
        ($Evidence | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false)
    )
    $output = & (Join-Path $PSHOME "pwsh.exe") `
        -NoLogo `
        -NoProfile `
        -File $gateScript `
        -EvidencePath $path `
        -ExpectedEvidenceAuthorizerMode $ExpectedMode `
        -AsJson
    return [ordered]@{
        exitCode = $LASTEXITCODE
        result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    }
}

try {
    $passed = 0

    $validResult = Invoke-Gate `
        -Evidence (New-PassingEvidence) `
        -Name "passing"
    if ($validResult.exitCode -ne 0 -or -not $validResult.result.passed) {
        throw "Passing evidence did not pass the enforce gate."
    }
    $passed++

    $missingTier = New-PassingEvidence
    $missingTier.authorization.byTier = @(
        $missingTier.authorization.byTier |
            Where-Object tier -NE "Participant"
    )
    $missingTierResult = Invoke-Gate `
        -Evidence $missingTier `
        -Name "missing-tier"
    if ($missingTierResult.exitCode -ne 2 -or $missingTierResult.result.passed) {
        throw "Missing tier evidence did not fail closed."
    }
    if ("authorization_tier_participant" -notin @(
        $missingTierResult.result.checks |
            Where-Object { -not $_.passed } |
            Select-Object -ExpandProperty name
    )) {
        throw "Missing tier evidence did not identify the Participant gate."
    }
    $passed++

    $lobbyPlayer = New-PassingEvidence
    $lobbyPlayer.targets[1].maximumOnlinePlayers = 1
    $lobbyResult = Invoke-Gate `
        -Evidence $lobbyPlayer `
        -Name "lobby-player"
    if ($lobbyResult.exitCode -ne 2 -or $lobbyResult.result.passed) {
        throw "Lobby player evidence did not fail closed."
    }
    if ("lobby_zero_players" -notin @(
        $lobbyResult.result.checks |
            Where-Object { -not $_.passed } |
            Select-Object -ExpandProperty name
    )) {
        throw "Lobby player evidence did not identify the lobby gate."
    }
    $passed++

    $enforceEvidence = New-PassingEvidence
    $enforceEvidence.authorizer.mode = "enforce"
    $enforceEvidence.authorizer.expectedMode = "enforce"
    $enforceResult = Invoke-Gate `
        -Evidence $enforceEvidence `
        -Name "enforce" `
        -ExpectedMode "enforce"
    if ($enforceResult.exitCode -ne 0 -or
        -not $enforceResult.result.passed -or
        $enforceResult.result.nextAction -ne
            "eligible-for-catalog-authentication-maintenance-window") {
        throw "Passing enforce evidence did not open the catalog-auth gate."
    }
    $passed++

    [pscustomobject]@{
        passed = $passed
        total = 4
        status = "passed"
    } | ConvertTo-Json -Compress
} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}
