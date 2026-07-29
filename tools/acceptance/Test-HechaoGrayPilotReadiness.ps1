#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ApiHostName,

    [string]$ApiUserName = "root",

    [ValidateRange(1, 65535)]
    [int]$ApiSshPort = 22,

    [Parameter(Mandatory)]
    [string]$ApiIdentityFile,

    [Parameter(Mandatory)]
    [string]$ApiKnownHostsFile,

    [uri]$ApiBaseUrl = "https://launcher-api.hechao.world/",

    [ValidateSet("Readiness", "2", "3", "5", "20")]
    [string]$Stage = "Readiness",

    [string]$Target,

    [ValidateRange(15, 86400)]
    [int]$DurationSeconds = 60,

    [ValidateRange(5, 300)]
    [int]$SampleIntervalSeconds = 5,

    [ValidateRange(0, 20)]
    [double]$MinimumTps = 18.5,

    [ValidateRange(1, 60000)]
    [double]$MaximumMspt = 50,

    [ValidateRange(1, 60000)]
    [double]$MaximumApiP95Milliseconds = 750,

    [ValidateRange(0, 600000)]
    [double]$MaximumGcMillisecondsPerMinute = 5000,

    [ValidateRange(5, 600)]
    [int]$MaximumMetricAgeSeconds = 90,

    [string[]]$QuiescentWhenEmptyTargets = @("activity"),

    [string]$VelocityHostName,

    [ValidateRange(1, 65535)]
    [int]$VelocityPort = 22,

    [string]$VelocityUserName = "administrator",

    [string]$VelocityIdentityFile,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

foreach ($quiescentTarget in $QuiescentWhenEmptyTargets) {
    if ($quiescentTarget -notmatch "^[a-z0-9][a-z0-9._-]{0,63}$") {
        throw "QuiescentWhenEmptyTargets contains an invalid target."
    }
}

function Invoke-PostgresJsonQuery {
    param(
        [Parameter(Mandatory)]
        [string]$Sql
    )

    $sqlBase64 = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($Sql)
    )
    $remoteCommand = (
        "echo {0} | base64 -d | docker exec -i " +
        "hechao-launcher-postgres psql -X -q " +
        "-v ON_ERROR_STOP=1 -U hechao_db_admin " +
        "-d hechao_launcher -At"
    ) -f $sqlBase64
    $arguments = @(
        "-i", (Resolve-Path -LiteralPath $ApiIdentityFile).Path,
        "-p", $ApiSshPort.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        ),
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes",
        "-o", "UserKnownHostsFile=$(
            (Resolve-Path -LiteralPath $ApiKnownHostsFile).Path
        )",
        "$ApiUserName@$ApiHostName",
        $remoteCommand
    )
    $output = & ssh.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Production aggregate query failed with exit code $LASTEXITCODE."
    }

    $json = ($output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "Production aggregate query returned no data."
    }

    return $json | ConvertFrom-Json
}

function Invoke-ApiProbe {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $uri = [uri]::new($ApiBaseUrl, $RelativePath)
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = $script:ApiHttpClient.GetAsync(
            $uri,
            [Net.Http.HttpCompletionOption]::ResponseHeadersRead
        ).GetAwaiter().GetResult()
        try {
            $stopwatch.Stop()
            return [ordered]@{
                path = $RelativePath
                statusCode = [int]$response.StatusCode
                elapsedMilliseconds = [math]::Round(
                    $stopwatch.Elapsed.TotalMilliseconds,
                    3
                )
                succeeded = [int]$response.StatusCode -eq 200
            }
        } finally {
            $response.Dispose()
        }
    } catch {
        $stopwatch.Stop()
        return [ordered]@{
            path = $RelativePath
            statusCode = $null
            elapsedMilliseconds = [math]::Round(
                $stopwatch.Elapsed.TotalMilliseconds,
                3
            )
            succeeded = $false
            errorType = $_.Exception.GetType().Name
        }
    }
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)]
        [double[]]$Values,

        [Parameter(Mandatory)]
        [ValidateRange(0, 1)]
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return $null
    }
    $ordered = @($Values | Sort-Object)
    $index = [math]::Max(
        0,
        [math]::Ceiling($Percentile * $ordered.Count) - 1
    )
    return [double]$ordered[$index]
}

function Add-Reason {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[object]]$Reasons,

        [Parameter(Mandatory)]
        [ValidateSet("External", "Technical")]
        [string]$Kind,

        [Parameter(Mandatory)]
        [string]$Code,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not ($Reasons | Where-Object code -EQ $Code)) {
        $Reasons.Add([ordered]@{
            kind = $Kind
            code = $Code
            message = $Message
        })
    }
}

foreach ($path in @($ApiIdentityFile, $ApiKnownHostsFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SSH file does not exist: $path"
    }
}
if ($Stage -ne "Readiness" -and [string]::IsNullOrWhiteSpace($Target)) {
    throw "Target is required for a player-count stage."
}
if (-not [string]::IsNullOrWhiteSpace($Target) -and
    $Target -notmatch "^[a-z0-9][a-z0-9._-]{0,63}$") {
    throw "Target is invalid."
}
if ($SampleIntervalSeconds -gt $DurationSeconds) {
    throw "SampleIntervalSeconds cannot exceed DurationSeconds."
}
if (-not [string]::IsNullOrWhiteSpace($VelocityHostName)) {
    if ([string]::IsNullOrWhiteSpace($VelocityIdentityFile) -or
        -not (Test-Path -LiteralPath $VelocityIdentityFile -PathType Leaf)) {
        throw "VelocityIdentityFile is required when VelocityHostName is set."
    }
}

$snapshotSql = @'
WITH latest_samples AS (
    SELECT DISTINCT ON (sample.velocity_target)
           sample.velocity_target,
           sample.is_online,
           sample.online_players,
           sample.max_players,
           sample.process_working_set_bytes,
           sample.process_private_bytes,
           sample.process_cpu_percent,
           sample.process_started_at,
           sample.disk_free_bytes,
           sample.disk_total_bytes,
           sample.tps_1m,
           sample.tps_5m,
           sample.tps_15m,
           sample.mspt_average,
           sample.gc_collection_time_ms,
           sample.metrics_captured_at,
           sample.probe_issues,
           sample.captured_at,
           sample.received_at
    FROM launcher.server_runtime_samples sample
    ORDER BY sample.velocity_target, sample.received_at DESC
),
account_counts AS (
    SELECT user_account.access_tier,
           count(*) FILTER (WHERE NOT user_account.is_disabled) AS enabled_count,
           count(identity_record.minecraft_uuid)
               FILTER (WHERE NOT user_account.is_disabled) AS linked_count
    FROM launcher.users user_account
    LEFT JOIN launcher.minecraft_identities identity_record
      ON identity_record.user_id = user_account.id
    GROUP BY user_account.access_tier
)
SELECT jsonb_build_object(
    'capturedAtUtc', now(),
    'accounts', COALESCE((
        SELECT jsonb_agg(
            jsonb_build_object(
                'tier', account_counts.access_tier,
                'enabledCount', account_counts.enabled_count,
                'linkedCount', account_counts.linked_count
            )
            ORDER BY account_counts.access_tier
        )
        FROM account_counts
    ), '[]'::jsonb),
    'targets', COALESCE((
        SELECT jsonb_agg(
            jsonb_build_object(
                'target', server.velocity_target,
                'serverId', server.id,
                'role', server.server_role,
                'catalogStatus', server.status,
                'monitoringEnabled', server.monitoring_enabled,
                'isOnline', latest.is_online,
                'onlinePlayers', latest.online_players,
                'maxPlayers', latest.max_players,
                'workingSetBytes', latest.process_working_set_bytes,
                'privateBytes', latest.process_private_bytes,
                'processCpuPercent', latest.process_cpu_percent,
                'processStartedAt', latest.process_started_at,
                'diskFreeBytes', latest.disk_free_bytes,
                'diskTotalBytes', latest.disk_total_bytes,
                'tps1m', latest.tps_1m,
                'tps5m', latest.tps_5m,
                'tps15m', latest.tps_15m,
                'msptAverage', latest.mspt_average,
                'gcCollectionTimeMs', latest.gc_collection_time_ms,
                'metricsCapturedAt', latest.metrics_captured_at,
                'probeIssues', latest.probe_issues,
                'capturedAt', latest.captured_at,
                'receivedAt', latest.received_at
            )
            ORDER BY server.velocity_target
        )
        FROM launcher.servers server
        LEFT JOIN latest_samples latest
          ON latest.velocity_target = server.velocity_target
        WHERE server.monitoring_enabled
    ), '[]'::jsonb),
    'activeAlerts', COALESCE((
        SELECT jsonb_agg(
            jsonb_build_object(
                'fingerprint', alert.fingerprint,
                'code', alert.code,
                'source', alert.source,
                'severity', alert.severity,
                'openedAt', alert.opened_at,
                'lastSeenAt', alert.last_seen_at
            )
            ORDER BY alert.severity, alert.fingerprint
        )
        FROM launcher.operational_alerts alert
        WHERE alert.status = 'Active'
    ), '[]'::jsonb)
)::text;
'@

$velocityStatus = $null
if (-not [string]::IsNullOrWhiteSpace($VelocityHostName)) {
    $statusTool = Join-Path (
        Split-Path -Parent (Split-Path -Parent $PSCommandPath)
    ) "server\Get-HechaoLauncherOnlyProductionStatus.ps1"
    $velocityStatus = & $statusTool `
        -HostName $VelocityHostName `
        -Port $VelocityPort `
        -UserName $VelocityUserName `
        -IdentityFile $VelocityIdentityFile
}

$apiHttpHandler = [Net.Http.SocketsHttpHandler]::new()
$apiHttpHandler.AllowAutoRedirect = $false
$apiHttpHandler.PooledConnectionLifetime = [TimeSpan]::FromMinutes(5)
$script:ApiHttpClient = [Net.Http.HttpClient]::new(
    $apiHttpHandler,
    $true
)
$script:ApiHttpClient.Timeout = [TimeSpan]::FromSeconds(10)
$script:ApiHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Hechao.GrayPilotReadiness/1.0"
)

$startedAt = [DateTimeOffset]::UtcNow
$samples = [Collections.Generic.List[object]]::new()
$apiProbes = [Collections.Generic.List[object]]::new()
$deadline = $startedAt.AddSeconds($DurationSeconds)
do {
    $apiProbes.Add((Invoke-ApiProbe -RelativePath "healthz"))
    $apiProbes.Add((Invoke-ApiProbe -RelativePath "readyz"))
    $samples.Add((Invoke-PostgresJsonQuery -Sql $snapshotSql))

    $remaining = $deadline - [DateTimeOffset]::UtcNow
    if ($remaining.TotalSeconds -gt 0) {
        Start-Sleep -Seconds (
            [math]::Min($SampleIntervalSeconds, $remaining.TotalSeconds)
        )
    }
} while ([DateTimeOffset]::UtcNow -lt $deadline)
$script:ApiHttpClient.Dispose()

$completedAt = [DateTimeOffset]::UtcNow
$reasons = [Collections.Generic.List[object]]::new()

$failedApiProbes = @($apiProbes | Where-Object { -not $_.succeeded })
if ($failedApiProbes.Count -gt 0) {
    Add-Reason `
        -Reasons $reasons `
        -Kind Technical `
        -Code "api_probe_failed" `
        -Message "One or more API health/readiness probes failed."
}
$apiLatencyValues = @(
    $apiProbes |
        Where-Object succeeded |
        ForEach-Object { [double]$_.elapsedMilliseconds }
)
$apiP95 = Get-Percentile -Values $apiLatencyValues -Percentile 0.95
if ($null -eq $apiP95 -or $apiP95 -gt $MaximumApiP95Milliseconds) {
    Add-Reason `
        -Reasons $reasons `
        -Kind Technical `
        -Code "api_latency_exceeded" `
        -Message (
            "API p95 latency exceeded {0} ms." -f
                $MaximumApiP95Milliseconds
        )
}

$requiredTiers = @(
    "Member",
    "Participant",
    "Collaborator",
    "Administrator"
)
$finalAccounts = @($samples[-1].accounts)
foreach ($tier in $requiredTiers) {
    $account = $finalAccounts | Where-Object tier -EQ $tier
    if ($null -eq $account -or [int64]$account.linkedCount -lt 1) {
        Add-Reason `
            -Reasons $reasons `
            -Kind External `
            -Code "missing_linked_tier_$($tier.ToLowerInvariant())" `
            -Message "No enabled, Minecraft-linked $tier account is available."
    }
}

$initialAlertFingerprints = @(
    $samples[0].activeAlerts |
        Select-Object -ExpandProperty fingerprint
)
$newCriticalAlerts = @(
    $samples[-1].activeAlerts |
        Where-Object {
            $_.severity -eq "Critical" -and
            $_.fingerprint -notin $initialAlertFingerprints
        }
)
$finalActiveAlerts = @($samples[-1].activeAlerts)
$finalCriticalAlerts = @(
    $finalActiveAlerts |
        Where-Object severity -EQ "Critical"
)
if ($finalCriticalAlerts.Count -gt 0) {
    Add-Reason `
        -Reasons $reasons `
        -Kind Technical `
        -Code "active_critical_alert" `
        -Message "One or more Critical operational alerts remain active."
}

$targetNames = @(
    $samples |
        ForEach-Object targets |
        Select-Object -ExpandProperty target -Unique
)
$targetSummaries = [Collections.Generic.List[object]]::new()
foreach ($targetName in $targetNames) {
    $targetSamples = @(
        $samples |
            ForEach-Object targets |
            Where-Object target -EQ $targetName
    )
    $lastTarget = $targetSamples[-1]
    $onlineSamples = @($targetSamples | Where-Object isOnline)
    $tpsValues = @(
        $onlineSamples |
            Where-Object { $null -ne $_.tps1m } |
            ForEach-Object { [double]$_.tps1m }
    )
    $msptValues = @(
        $onlineSamples |
            Where-Object { $null -ne $_.msptAverage } |
            ForEach-Object { [double]$_.msptAverage }
    )
    $playerValues = @(
        $targetSamples |
            ForEach-Object { [int]$_.onlinePlayers }
    )
    $maximumOnlinePlayers = if ($playerValues.Count -eq 0) {
        0
    } else {
        [int]($playerValues | Measure-Object -Maximum).Maximum
    }
    $tickMetricIssues = @(
        $lastTarget.probeIssues |
            Where-Object {
                $_ -in @(
                    "MetricsNotConfigured",
                    "MetricsFileMissing",
                    "MetricsFileStale",
                    "MetricsFileInvalid"
                )
            }
    )
    $isQuiescentWhenEmpty =
        $targetName -in $QuiescentWhenEmptyTargets -and
        [bool]$lastTarget.isOnline -and
        $maximumOnlinePlayers -eq 0 -and
        $tpsValues.Count -eq 0 -and
        $msptValues.Count -eq 0 -and
        $tickMetricIssues.Count -eq 0
    $gcSamples = @(
        $onlineSamples |
            Where-Object { $null -ne $_.gcCollectionTimeMs }
    )
    [double]$gcPerMinute = 0
    if ($gcSamples.Count -ge 2) {
        $gcElapsedMinutes = (
            [DateTimeOffset]$gcSamples[-1].receivedAt -
            [DateTimeOffset]$gcSamples[0].receivedAt
        ).TotalMinutes
        if ($gcElapsedMinutes -gt 0) {
            $gcDelta = [math]::Max(
                0,
                [int64]$gcSamples[-1].gcCollectionTimeMs -
                    [int64]$gcSamples[0].gcCollectionTimeMs
            )
            $gcPerMinute = $gcDelta / $gcElapsedMinutes
        }
    }

    $metricAge = if ($null -eq $lastTarget.receivedAt) {
        [double]::PositiveInfinity
    } else {
        ($completedAt - [DateTimeOffset]$lastTarget.receivedAt).TotalSeconds
    }

    if ($lastTarget.role -eq "Infrastructure" -and
        $maximumOnlinePlayers -gt 0) {
        Add-Reason `
            -Reasons $reasons `
            -Kind Technical `
            -Code "infrastructure_target_has_players" `
            -Message "An infrastructure target reported one or more players."
    }
    if ($Stage -eq "Readiness" -and
        $lastTarget.role -eq "Player" -and
        $maximumOnlinePlayers -gt 0) {
        Add-Reason `
            -Reasons $reasons `
            -Kind External `
            -Code "readiness_target_not_empty_$targetName" `
            -Message (
                "Readiness requires an empty baseline, but {0} reached {1} players." -f
                    $targetName,
                    $maximumOnlinePlayers
            )
    }

    $isSelectedTarget = (
        [string]::IsNullOrWhiteSpace($Target) -or $targetName -eq $Target
    )
    if ($isSelectedTarget -and $lastTarget.isOnline) {
        if ($metricAge -gt $MaximumMetricAgeSeconds) {
            Add-Reason `
                -Reasons $reasons `
                -Kind Technical `
                -Code "stale_metrics_$targetName" `
                -Message "Metrics for $targetName are stale."
        }
        if (-not $isQuiescentWhenEmpty -and (
            $tpsValues.Count -eq 0 -or
            ($tpsValues | Measure-Object -Minimum).Minimum -lt $MinimumTps)) {
            Add-Reason `
                -Reasons $reasons `
                -Kind Technical `
                -Code "tps_below_threshold_$targetName" `
                -Message "TPS for $targetName fell below $MinimumTps."
        }
        if (-not $isQuiescentWhenEmpty -and (
            $msptValues.Count -eq 0 -or
            ($msptValues | Measure-Object -Maximum).Maximum -gt $MaximumMspt)) {
            Add-Reason `
                -Reasons $reasons `
                -Kind Technical `
                -Code "mspt_above_threshold_$targetName" `
                -Message "MSPT for $targetName exceeded $MaximumMspt ms."
        }
        if ($gcPerMinute -gt $MaximumGcMillisecondsPerMinute) {
            Add-Reason `
                -Reasons $reasons `
                -Kind Technical `
                -Code "gc_above_threshold_$targetName" `
                -Message (
                    "GC pause growth for {0} exceeded {1} ms/min." -f
                        $targetName,
                        $MaximumGcMillisecondsPerMinute
                )
        }
    }

    $targetSummaries.Add([ordered]@{
        target = $targetName
        role = [string]$lastTarget.role
        catalogStatus = [string]$lastTarget.catalogStatus
        isOnline = [bool]$lastTarget.isOnline
        metricAgeSeconds = if ([double]::IsPositiveInfinity($metricAge)) {
            $null
        } else {
            [math]::Round($metricAge, 3)
        }
        minimumTps1m = if ($tpsValues.Count -eq 0) {
            $null
        } else {
            [double]($tpsValues | Measure-Object -Minimum).Minimum
        }
        maximumMspt = if ($msptValues.Count -eq 0) {
            $null
        } else {
            [double]($msptValues | Measure-Object -Maximum).Maximum
        }
        maximumOnlinePlayers = $maximumOnlinePlayers
        tickMetricsState = if ($isQuiescentWhenEmpty) {
            "paused-when-empty"
        } elseif ($tpsValues.Count -gt 0 -and $msptValues.Count -gt 0) {
            "live"
        } else {
            "unavailable"
        }
        gcMillisecondsPerMinute = [math]::Round($gcPerMinute, 3)
        finalWorkingSetBytes = $lastTarget.workingSetBytes
        finalProcessCpuPercent = $lastTarget.processCpuPercent
        finalDiskFreeBytes = $lastTarget.diskFreeBytes
        probeIssues = @($lastTarget.probeIssues)
    })
}

$expectedPlayers = if ($Stage -eq "Readiness") {
    0
} else {
    [int]$Stage
}
if ($expectedPlayers -gt 0) {
    $selectedSummary = $targetSummaries |
        Where-Object target -EQ $Target
    if ($null -eq $selectedSummary) {
        Add-Reason `
            -Reasons $reasons `
            -Kind Technical `
            -Code "target_not_monitored" `
            -Message "The selected target is not present in monitored targets."
    } elseif (-not $selectedSummary.isOnline) {
        Add-Reason `
            -Reasons $reasons `
            -Kind Technical `
            -Code "target_offline" `
            -Message "The selected target is offline."
    } elseif ($selectedSummary.maximumOnlinePlayers -lt $expectedPlayers) {
        Add-Reason `
            -Reasons $reasons `
            -Kind External `
            -Code "player_stage_not_reached" `
            -Message (
                "The selected target reached {0}/{1} players." -f
                    $selectedSummary.maximumOnlinePlayers,
                    $expectedPlayers
            )
    }
}

if ($null -ne $velocityStatus) {
    if ($velocityStatus.authorizerMode -ne "monitor") {
        Add-Reason `
            -Reasons $reasons `
            -Kind Technical `
            -Code "authorizer_not_monitor" `
            -Message "Velocity Authorizer is not in monitor mode."
    }
    if ($velocityStatus.lobbyWhitelistEntries -ne 0 -or
        $velocityStatus.lobbyServerIp -ne "127.0.0.1" -or
        $velocityStatus.lobbyWhitelistEnabled -ne "true" -or
        $velocityStatus.lobbyEnforceWhitelist -ne "true") {
        Add-Reason `
            -Reasons $reasons `
            -Kind Technical `
            -Code "lobby_isolation_drift" `
            -Message "Lobby isolation settings have drifted."
    }
}

$technicalReasons = @($reasons | Where-Object kind -EQ "Technical")
$externalReasons = @($reasons | Where-Object kind -EQ "External")
$status = if ($technicalReasons.Count -gt 0) {
    "failed"
} elseif ($externalReasons.Count -gt 0) {
    "waiting-for-external-participants"
} else {
    "passed"
}

$evidence = [ordered]@{
    schemaVersion = 1
    status = $status
    stage = $Stage
    target = if ([string]::IsNullOrWhiteSpace($Target)) { $null } else { $Target }
    startedAtUtc = $startedAt.ToString("O")
    completedAtUtc = $completedAt.ToString("O")
    durationSeconds = [math]::Round(
        ($completedAt - $startedAt).TotalSeconds,
        3
    )
    sampleIntervalSeconds = $SampleIntervalSeconds
    sampleCount = $samples.Count
    thresholds = [ordered]@{
        minimumTps = $MinimumTps
        maximumMspt = $MaximumMspt
        maximumApiP95Milliseconds = $MaximumApiP95Milliseconds
        maximumGcMillisecondsPerMinute = $MaximumGcMillisecondsPerMinute
        maximumMetricAgeSeconds = $MaximumMetricAgeSeconds
        quiescentWhenEmptyTargets = @($QuiescentWhenEmptyTargets)
    }
    api = [ordered]@{
        probeCount = $apiProbes.Count
        failedProbeCount = $failedApiProbes.Count
        p50Milliseconds = Get-Percentile `
            -Values $apiLatencyValues `
            -Percentile 0.50
        p95Milliseconds = $apiP95
        maximumMilliseconds = if ($apiLatencyValues.Count -eq 0) {
            $null
        } else {
            [double]($apiLatencyValues | Measure-Object -Maximum).Maximum
        }
    }
    accounts = $finalAccounts
    targets = @($targetSummaries)
    authorizer = if ($null -eq $velocityStatus) {
        $null
    } else {
        [ordered]@{
            mode = [string]$velocityStatus.authorizerMode
            infrastructureTargets = [string]$velocityStatus.infrastructureTargets
            lobbyServerIp = [string]$velocityStatus.lobbyServerIp
            lobbyWhitelistEnabled = [string]$velocityStatus.lobbyWhitelistEnabled
            lobbyEnforceWhitelist = [string]$velocityStatus.lobbyEnforceWhitelist
            lobbyWhitelistEntries = $velocityStatus.lobbyWhitelistEntries
        }
    }
    initialActiveAlertCount = @($samples[0].activeAlerts).Count
    finalActiveAlertCount = $finalActiveAlerts.Count
    finalActiveAlerts = $finalActiveAlerts
    finalCriticalAlerts = $finalCriticalAlerts
    newCriticalAlerts = $newCriticalAlerts
    blockingReasons = @($reasons)
    technicalBlockerCount = $technicalReasons.Count
    externalBlockerCount = $externalReasons.Count
    safeProgression = $status -eq "passed"
    automaticAction = if ($status -eq "passed") {
        "none-required"
    } else {
        "stop-expansion-and-keep-current-production-mode"
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) |
    Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($evidence | ConvertTo-Json -Depth 12) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)

$result = [ordered]@{
    status = $status
    stage = $Stage
    target = $evidence.target
    outputPath = $resolvedOutput
    sampleCount = $samples.Count
    apiP95Milliseconds = $apiP95
    technicalBlockerCount = $technicalReasons.Count
    externalBlockerCount = $externalReasons.Count
    safeProgression = $evidence.safeProgression
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4 -Compress
} else {
    [pscustomobject]$result
}
