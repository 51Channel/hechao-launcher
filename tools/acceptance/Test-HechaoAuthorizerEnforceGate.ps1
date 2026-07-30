#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidencePath,

    [ValidateRange(1, 168)]
    [int]$MaximumEvidenceAgeHours = 24,

    [ValidateSet("monitor", "enforce")]
    [string]$ExpectedEvidenceAuthorizerMode = "monitor",

    [ValidateSet(
        "PlayerNotLinked",
        "PlayerDisabled",
        "MinecraftIdentityBanned",
        "ServerUnknown",
        "ServerUnavailable",
        "AccessDenied",
        "InsufficientTier",
        "PermissionDataStale",
        "LaunchGrantRequired",
        "LaunchGrantIpMismatch",
        "MinecraftVersionMismatch",
        "ClientProfileMismatch"
    )]
    [string[]]$RequiredDeniedReasons = @(
        "LaunchGrantRequired",
        "InsufficientTier",
        "AccessDenied",
        "ServerUnavailable"
    ),

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "Gray-pilot evidence does not exist: $EvidencePath"
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw -Encoding utf8 |
    ConvertFrom-Json
$checks = [Collections.Generic.List[object]]::new()

function Add-GateCheck {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [bool]$Passed,

        [Parameter(Mandatory)]
        [string]$Message
    )

    $script:checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        message = $Message
    })
}

$schemaVersion = if ($null -eq $evidence.schemaVersion) {
    0
} else {
    [int]$evidence.schemaVersion
}
Add-GateCheck `
    -Name "schema_version" `
    -Passed ($schemaVersion -ge 2) `
    -Message "Gray evidence must use schema version 2 or newer."

$completedAt = [DateTimeOffset]::MinValue
$completedAtValid = [DateTimeOffset]::TryParse(
    [string]$evidence.completedAtUtc,
    [ref]$completedAt
)
$evidenceAgeHours = if ($completedAtValid) {
    ([DateTimeOffset]::UtcNow - $completedAt.ToUniversalTime()).TotalHours
} else {
    [double]::PositiveInfinity
}
Add-GateCheck `
    -Name "evidence_freshness" `
    -Passed (
        $completedAtValid -and
        $evidenceAgeHours -ge 0 -and
        $evidenceAgeHours -le $MaximumEvidenceAgeHours
    ) `
    -Message (
        "Evidence must be no more than {0} hour(s) old." -f
            $MaximumEvidenceAgeHours
    )

Add-GateCheck `
    -Name "gray_stage_passed" `
    -Passed (
        [string]$evidence.status -eq "passed" -and
        [bool]$evidence.safeProgression -and
        [int]$evidence.technicalBlockerCount -eq 0 -and
        [int]$evidence.externalBlockerCount -eq 0
    ) `
    -Message "Gray evidence must be passed with no blockers."

$stagePlayers = 0
$stageValid = [int]::TryParse([string]$evidence.stage, [ref]$stagePlayers)
Add-GateCheck `
    -Name "minimum_player_stage" `
    -Passed ($stageValid -and $stagePlayers -ge 5) `
    -Message "At least the five-player stage must pass before enforce."

$requiredTiers = @(
    "Member",
    "Participant",
    "Collaborator",
    "Administrator"
)
$declaredTiers = @($evidence.requiredAuthorizationTiers)
$authorizationByTier = @($evidence.authorization.byTier)
foreach ($tier in $requiredTiers) {
    $tierEvidence = @(
        $authorizationByTier |
            Where-Object tier -EQ $tier
    )
    Add-GateCheck `
        -Name "authorization_tier_$($tier.ToLowerInvariant())" `
        -Passed (
            $tier -in $declaredTiers -and
            $tierEvidence.Count -eq 1 -and
            [int]$tierEvidence[0].successfulIdentityCount -ge 1
        ) `
        -Message (
            "A fresh successful authorization for the $tier tier is required."
        )
}

$selectedTarget = [string]$evidence.target
$targetAuthorization = @(
    $evidence.authorization.successByTarget |
        Where-Object target -EQ $selectedTarget
)
Add-GateCheck `
    -Name "fresh_target_authorizations" `
    -Passed (
        -not [string]::IsNullOrWhiteSpace($selectedTarget) -and
        $targetAuthorization.Count -eq 1 -and
        [int]$targetAuthorization[0].successfulIdentityCount -ge $stagePlayers
    ) `
    -Message (
        "The selected target must record one fresh authorization per staged player."
    )

$declaredDeniedReasons = @($evidence.requiredDeniedReasons)
$deniedByReason = @($evidence.authorization.deniedByReason)
foreach ($reason in $RequiredDeniedReasons) {
    $reasonEvidence = @(
        $deniedByReason |
            Where-Object reason -EQ $reason
    )
    Add-GateCheck `
        -Name "denial_$($reason.ToLowerInvariant())" `
        -Passed (
            $reason -in $declaredDeniedReasons -and
            $reasonEvidence.Count -eq 1 -and
            [int]$reasonEvidence[0].deniedCount -ge 1
        ) `
        -Message "A real $reason denial is required."
}

Add-GateCheck `
    -Name "authorizer_mode" `
    -Passed (
        [string]$evidence.authorizer.mode -eq
            $ExpectedEvidenceAuthorizerMode -and
        [string]$evidence.authorizer.expectedMode -eq
            $ExpectedEvidenceAuthorizerMode
    ) `
    -Message (
        "The gray stage must run in $ExpectedEvidenceAuthorizerMode mode."
    )

Add-GateCheck `
    -Name "lobby_configuration" `
    -Passed (
        [string]$evidence.authorizer.lobbyServerIp -eq "127.0.0.1" -and
        [string]$evidence.authorizer.lobbyWhitelistEnabled -eq "true" -and
        [string]$evidence.authorizer.lobbyEnforceWhitelist -eq "true" -and
        [int]$evidence.authorizer.lobbyWhitelistEntries -eq 0
    ) `
    -Message "Lobby must remain loopback-only with an enforced empty whitelist."

$lobbyEvidence = @(
    $evidence.targets |
        Where-Object {
            $_.role -eq "Infrastructure" -and $_.target -eq "lobby"
        }
)
Add-GateCheck `
    -Name "lobby_zero_players" `
    -Passed (
        $lobbyEvidence.Count -eq 1 -and
        [int]$lobbyEvidence[0].maximumOnlinePlayers -eq 0
    ) `
    -Message "Lobby must report zero players throughout the gray window."

Add-GateCheck `
    -Name "no_critical_alerts" `
    -Passed (@($evidence.finalCriticalAlerts).Count -eq 0) `
    -Message "No Critical operational alert may remain active."

$failedChecks = @($checks | Where-Object { -not $_.passed })
$passed = $failedChecks.Count -eq 0
$result = [ordered]@{
    schemaVersion = 1
    passed = $passed
    evaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    evidencePath = [IO.Path]::GetFullPath($EvidencePath)
    evidenceCompletedAtUtc = if ($completedAtValid) {
        $completedAt.ToUniversalTime().ToString("O")
    } else {
        $null
    }
    evidenceAgeHours = if ([double]::IsPositiveInfinity($evidenceAgeHours)) {
        $null
    } else {
        [math]::Round($evidenceAgeHours, 3)
    }
    target = if ([string]::IsNullOrWhiteSpace($selectedTarget)) {
        $null
    } else {
        $selectedTarget
    }
    stage = if ($stageValid) { $stagePlayers } else { $null }
    checkCount = $checks.Count
    failedCheckCount = $failedChecks.Count
    checks = @($checks)
    nextAction = if ($passed) {
        if ($ExpectedEvidenceAuthorizerMode -eq "monitor") {
            "eligible-for-controlled-enforce-maintenance-window"
        } else {
            "eligible-for-catalog-authentication-maintenance-window"
        }
    } else {
        if ($ExpectedEvidenceAuthorizerMode -eq "monitor") {
            "keep-monitor-and-do-not-enable-catalog-authentication"
        } else {
            "keep-catalog-authentication-disabled"
        }
    }
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 8 -Compress
} else {
    [pscustomobject]$result
}

if (-not $passed) {
    exit 2
}
