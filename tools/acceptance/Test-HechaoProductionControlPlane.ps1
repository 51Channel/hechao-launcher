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

    [uri]$AdminBaseUrl = "https://admin.hechao.world/",

    [uri]$PublicSiteUrl = "https://hechao.world/",

    [uri]$RelayUrl = "http://api.hechao.world/",

    [string]$ExpectedRelease = "0.26.1-20260802T012527Z",

    [string]$ExpectedApiVersion = "0.26.1",

    [ValidateRange(1, 1000)]
    [int]$ExpectedMigration = 21,

    [ValidateRange(1, 100)]
    [int]$ExpectedMfaCredentialCount = 2,

    [ValidateRange(1, 1000)]
    [int]$ExpectedRecoveryCodeHashCount = 16,

    [ValidateRange(30, 3600)]
    [int]$MaximumRuntimeAgeSeconds = 120,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

foreach ($path in @($ApiIdentityFile, $ApiKnownHostsFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SSH file does not exist: $path"
    }
}

function Invoke-SshText {
    param(
        [Parameter(Mandatory)]
        [string]$RemoteCommand
    )

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
        $RemoteCommand
    )
    $output = & ssh.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Remote read-only command failed with exit code $LASTEXITCODE."
    }

    return ($output -join [Environment]::NewLine).Trim()
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
    $json = Invoke-SshText -RemoteCommand $remoteCommand
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "Production aggregate query returned no data."
    }

    return $json | ConvertFrom-Json
}

function Get-RemoteServiceSnapshot {
    $script = @'
set -euo pipefail
current="$(readlink -f /opt/hechao-launcher-api/current)"
active="$(systemctl is-active hechao-launcher-api.service)"
main_pid="$(systemctl show hechao-launcher-api.service -p MainPID --value)"
started="$(systemctl show hechao-launcher-api.service -p ExecMainStartTimestamp --value)"
admin_enabled="$(
  grep -E '^AdminWeb__Enabled=' \
    /etc/hechao-launcher-api/environment |
    tail -n1 |
    cut -d= -f2-
)"
auth_enforced="$(
  grep -E '^Authentication__EnforceCatalogAuthentication=' \
    /etc/hechao-launcher-api/environment |
    tail -n1 |
    cut -d= -f2-
)"
key_owner="$(
  stat -c '%U:%G' /var/lib/hechao-launcher-api/data-protection
)"
key_mode="$(
  stat -c '%a' /var/lib/hechao-launcher-api/data-protection
)"
key_files="$(
  find /var/lib/hechao-launcher-api/data-protection \
    -maxdepth 1 -type f |
    wc -l
)"
warning_count="$(
  journalctl -u hechao-launcher-api.service -p warning \
    --since '30 minutes ago' --no-pager -o cat |
    sed '/^$/d' |
    wc -l
)"
printf \
  '{"current":"%s","active":"%s","mainPid":%s,'\
'"started":"%s","adminEnabled":"%s",'\
'"catalogAuthEnforced":"%s",'\
'"keyRing":{"owner":"%s","mode":"%s","files":%s},'\
'"warningCount30m":%s}\n' \
  "$current" "$active" "$main_pid" "$started" \
  "$admin_enabled" "$auth_enforced" \
  "$key_owner" "$key_mode" "$key_files" "$warning_count"
'@
    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($script)
    )
    $json = Invoke-SshText -RemoteCommand (
        "echo $encoded | base64 -d | bash"
    )
    return $json | ConvertFrom-Json
}

function Get-HeaderValue {
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpResponseMessage]$Response,

        [Parameter(Mandatory)]
        [string]$Name
    )

    [string[]]$values = @()
    if ($Response.Headers.TryGetValues($Name, [ref]$values)) {
        return $values -join ", "
    }
    if ($Response.Content.Headers.TryGetValues($Name, [ref]$values)) {
        return $values -join ", "
    }
    return $null
}

function Invoke-HttpProbe {
    param(
        [Parameter(Mandatory)]
        [uri]$Uri,

        [string[]]$BodyMarkers = @()
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $response = $script:HttpClient.GetAsync(
        $Uri,
        [Net.Http.HttpCompletionOption]::ResponseContentRead
    ).GetAwaiter().GetResult()
    try {
        $stopwatch.Stop()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
        $markers = [ordered]@{}
        foreach ($marker in $BodyMarkers) {
            $markers[$marker] = $body.Contains(
                $marker,
                [StringComparison]::Ordinal
            )
        }

        return [ordered]@{
            uri = $Uri.AbsoluteUri
            statusCode = [int]$response.StatusCode
            elapsedMilliseconds = [math]::Round(
                $stopwatch.Elapsed.TotalMilliseconds,
                3
            )
            contentLength = $bodyBytes.Length
            contentSha256 = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($bodyBytes)
            ).ToLowerInvariant()
            contentType = Get-HeaderValue `
                -Response $response `
                -Name "Content-Type"
            cacheControl = Get-HeaderValue `
                -Response $response `
                -Name "Cache-Control"
            contentSecurityPolicy = Get-HeaderValue `
                -Response $response `
                -Name "Content-Security-Policy"
            xFrameOptions = Get-HeaderValue `
                -Response $response `
                -Name "X-Frame-Options"
            referrerPolicy = Get-HeaderValue `
                -Response $response `
                -Name "Referrer-Policy"
            xContentTypeOptions = Get-HeaderValue `
                -Response $response `
                -Name "X-Content-Type-Options"
            markers = $markers
        }
    } finally {
        $response.Dispose()
    }
}

function Add-Check {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[object]]$Checks,

        [Parameter(Mandatory)]
        [ValidateSet("Technical", "External", "Advisory")]
        [string]$Kind,

        [Parameter(Mandatory)]
        [string]$Code,

        [Parameter(Mandatory)]
        [bool]$Passed,

        [Parameter(Mandatory)]
        [string]$Message
    )

    $status = if ($Passed) {
        "Passed"
    } elseif ($Kind -eq "External") {
        "Blocked"
    } elseif ($Kind -eq "Advisory") {
        "Warning"
    } else {
        "Failed"
    }
    $Checks.Add([ordered]@{
        kind = $Kind
        code = $Code
        status = $status
        message = $Message
    })
}

$databaseSql = @'
WITH account_counts AS (
    SELECT
        user_account.access_tier,
        count(*) FILTER (
            WHERE NOT user_account.is_disabled
        ) AS enabled_count,
        count(identity_record.minecraft_uuid) FILTER (
            WHERE NOT user_account.is_disabled
        ) AS linked_count
    FROM launcher.users user_account
    LEFT JOIN launcher.minecraft_identities identity_record
      ON identity_record.user_id = user_account.id
    GROUP BY user_account.access_tier
),
latest_runtime AS (
    SELECT DISTINCT ON (sample.velocity_target)
        sample.velocity_target,
        sample.is_online,
        sample.online_players,
        sample.max_players,
        sample.tps_1m,
        sample.mspt_average,
        sample.gc_collection_time_ms,
        sample.probe_issues,
        sample.captured_at,
        sample.received_at
    FROM launcher.server_runtime_samples sample
    ORDER BY sample.velocity_target, sample.received_at DESC
),
required_audits(action) AS (
    VALUES
        ('admin.mfa.enrollment.started'),
        ('admin.mfa.enabled'),
        ('admin.login_ticket.created'),
        ('admin.web_session.created'),
        ('catalog.server.updated'),
        ('diagnostic.upload.authorized'),
        ('diagnostic.upload.completed'),
        ('diagnostic.admin.downloaded'),
        ('velocity.launch_grant.created'),
        ('velocity.launch_grant.consumed')
),
audit_counts AS (
    SELECT
        required.action,
        count(audit.id) AS event_count,
        max(audit.created_at) AS last_at
    FROM required_audits required
    LEFT JOIN launcher.audit_logs audit
      ON audit.action = required.action
    GROUP BY required.action
),
group_counts AS (
    SELECT
        mapping.primary_group,
        mapping.access_tier,
        count(snapshot.minecraft_uuid) AS player_count,
        count(snapshot.minecraft_uuid) FILTER (
            WHERE snapshot.received_at >= now() - interval '10 minutes'
        ) AS fresh_count
    FROM launcher.luckperms_group_tier_mappings mapping
    LEFT JOIN launcher.luckperms_player_snapshots snapshot
      ON snapshot.primary_group = mapping.primary_group
    GROUP BY mapping.primary_group, mapping.access_tier
)
SELECT jsonb_build_object(
    'capturedAtUtc', now(),
    'migrations', (
        SELECT jsonb_build_object(
            'count', count(*),
            'maximumVersion', max(version)
        )
        FROM launcher.schema_migrations
    ),
    'accounts', jsonb_build_object(
        'total', (SELECT count(*) FROM launcher.users),
        'disabled', (
            SELECT count(*) FROM launcher.users WHERE is_disabled
        ),
        'tiers', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'tier', account_counts.access_tier,
                    'enabledCount', account_counts.enabled_count,
                    'linkedCount', account_counts.linked_count
                )
                ORDER BY account_counts.access_tier
            )
            FROM account_counts
        ), '[]'::jsonb)
    ),
    'luckPerms', jsonb_build_object(
        'snapshotTotal', (
            SELECT count(*)
            FROM launcher.luckperms_player_snapshots
        ),
        'snapshotFreshTenMinutes', (
            SELECT count(*)
            FROM launcher.luckperms_player_snapshots
            WHERE received_at >= now() - interval '10 minutes'
        ),
        'groups', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'primaryGroup', group_counts.primary_group,
                    'tier', group_counts.access_tier,
                    'playerCount', group_counts.player_count,
                    'freshCount', group_counts.fresh_count
                )
                ORDER BY group_counts.primary_group
            )
            FROM group_counts
        ), '[]'::jsonb)
    ),
    'administratorSecurity', jsonb_build_object(
        'mfaCredentials', (
            SELECT count(*) FROM launcher.admin_mfa_credentials
        ),
        'recoveryCodeHashes', (
            SELECT COALESCE(
                sum(jsonb_array_length(recovery_code_hashes)),
                0
            )
            FROM launcher.admin_mfa_credentials
        ),
        'activeEnrollments', (
            SELECT count(*)
            FROM launcher.admin_mfa_enrollments
            WHERE expires_at > now()
        ),
        'activeMfaSessions', (
            SELECT count(*)
            FROM launcher.admin_web_sessions
            WHERE revoked_at IS NULL
              AND expires_at > now()
              AND mfa_verified_at IS NOT NULL
        ),
        'activeUnverifiedSessions', (
            SELECT count(*)
            FROM launcher.admin_web_sessions
            WHERE revoked_at IS NULL
              AND expires_at > now()
              AND mfa_verified_at IS NULL
        ),
        'activeTickets', (
            SELECT count(*)
            FROM launcher.admin_login_tickets
            WHERE consumed_at IS NULL
              AND expires_at > now()
        )
    ),
    'catalog', jsonb_build_object(
        'servers', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'id', server.id,
                    'target', server.velocity_target,
                    'status', server.status,
                    'role', server.server_role,
                    'visible', server.is_visible,
                    'monitoringEnabled', server.monitoring_enabled,
                    'minimumTier', server.minimum_tier,
                    'profileId', server.client_profile_id,
                    'revision', server.revision,
                    'runtime', CASE
                        WHEN runtime.velocity_target IS NULL THEN NULL
                        ELSE jsonb_build_object(
                            'online', runtime.is_online,
                            'players', runtime.online_players,
                            'maxPlayers', runtime.max_players,
                            'tps1m', runtime.tps_1m,
                            'mspt', runtime.mspt_average,
                            'gcMilliseconds',
                                runtime.gc_collection_time_ms,
                            'probeIssues', runtime.probe_issues,
                            'capturedAt', runtime.captured_at,
                            'receivedAt', runtime.received_at
                        )
                    END
                )
                ORDER BY server.sort_order, server.id
            )
            FROM launcher.servers server
            LEFT JOIN latest_runtime runtime
              ON runtime.velocity_target = server.velocity_target
        ), '[]'::jsonb),
        'profiles', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'id', profile.id,
                    'active', profile.is_active,
                    'version', profile.version,
                    'revision', profile.revision,
                    'releaseCount', (
                        SELECT count(*)
                        FROM launcher.client_profile_releases release
                        WHERE release.profile_id = profile.id
                    ),
                    'channels', (
                        SELECT jsonb_agg(
                            jsonb_build_object(
                                'channel', channel.channel,
                                'rolloutPercentage',
                                    channel.rollout_percentage,
                                'releaseSha256',
                                    channel.release_sha256,
                                'releaseVersion',
                                    release.version,
                                'releasePaused',
                                    release.is_paused,
                                'revision', channel.revision
                            )
                            ORDER BY channel.channel
                        )
                        FROM launcher.client_profile_channels channel
                        LEFT JOIN launcher.client_profile_releases release
                          ON release.profile_id = channel.profile_id
                         AND release.manifest_sha256 =
                             channel.release_sha256
                        WHERE channel.profile_id = profile.id
                    )
                )
                ORDER BY profile.id
            )
            FROM launcher.client_profiles profile
        ), '[]'::jsonb)
    ),
    'telemetry', jsonb_build_object(
        'events24Hours', (
            SELECT count(*)
            FROM launcher.client_telemetry_events
            WHERE received_at >= now() - interval '24 hours'
        ),
        'users24Hours', (
            SELECT count(DISTINCT user_id)
            FROM launcher.client_telemetry_events
            WHERE received_at >= now() - interval '24 hours'
        ),
        'failures24Hours', (
            SELECT count(*)
            FROM launcher.client_telemetry_events
            WHERE received_at >= now() - interval '24 hours'
              AND outcome = 'Failure'
        ),
        'outcomes', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'eventType', grouped.event_type,
                    'outcome', grouped.outcome,
                    'count', grouped.event_count
                )
                ORDER BY grouped.event_type, grouped.outcome
            )
            FROM (
                SELECT event_type, outcome, count(*) AS event_count
                FROM launcher.client_telemetry_events
                WHERE received_at >= now() - interval '24 hours'
                GROUP BY event_type, outcome
            ) grouped
        ), '[]'::jsonb)
    ),
    'diagnostics', jsonb_build_object(
        'uploaded', (
            SELECT count(*)
            FROM launcher.diagnostic_uploads
            WHERE status = 'uploaded'
        ),
        'failed', (
            SELECT count(*)
            FROM launcher.diagnostic_uploads
            WHERE status = 'failed'
        )
    ),
    'queues', jsonb_build_object(
        'forumRevocationsPending', (
            SELECT count(*)
            FROM launcher.forum_session_revocation_outbox
            WHERE completed_at IS NULL
        ),
        'tierCommandsPending', (
            SELECT count(*)
            FROM launcher.luckperms_tier_change_commands
            WHERE status IN ('Pending', 'Claimed')
        ),
        'launchGrantsUnused', (
            SELECT count(*)
            FROM launcher.velocity_launch_grants
            WHERE consumed_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > now()
        )
    ),
    'alerts', jsonb_build_object(
        'active', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'fingerprint', alert.fingerprint,
                    'code', alert.code,
                    'source', alert.source,
                    'severity', alert.severity,
                    'openedAt', alert.opened_at,
                    'lastSeenAt', alert.last_seen_at,
                    'acknowledged',
                        alert.acknowledged_at IS NOT NULL
                )
                ORDER BY alert.severity, alert.fingerprint
            )
            FROM launcher.operational_alerts alert
            WHERE alert.status = 'Active'
        ), '[]'::jsonb)
    ),
    'audit', jsonb_build_object(
        'total', (SELECT count(*) FROM launcher.audit_logs),
        'lastAt', (SELECT max(created_at) FROM launcher.audit_logs),
        'requiredActions', COALESCE((
            SELECT jsonb_agg(
                jsonb_build_object(
                    'action', audit_counts.action,
                    'count', audit_counts.event_count,
                    'lastAt', audit_counts.last_at
                )
                ORDER BY audit_counts.action
            )
            FROM audit_counts
        ), '[]'::jsonb)
    )
)::text;
'@

$handler = [Net.Http.SocketsHttpHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.PooledConnectionLifetime = [TimeSpan]::FromMinutes(2)
$script:HttpClient = [Net.Http.HttpClient]::new($handler, $true)
$script:HttpClient.Timeout = [TimeSpan]::FromSeconds(15)
$script:HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Hechao.ProductionControlPlane/1.0"
)

$startedAt = [DateTimeOffset]::UtcNow
$serviceBefore = Get-RemoteServiceSnapshot
$database = Invoke-PostgresJsonQuery -Sql $databaseSql

$healthProbe = Invoke-HttpProbe -Uri (
    [uri]::new($ApiBaseUrl, "healthz")
)
$readyProbe = Invoke-HttpProbe -Uri (
    [uri]::new($ApiBaseUrl, "readyz")
)
$adminIndexProbe = Invoke-HttpProbe `
    -Uri ([uri]::new($AdminBaseUrl, "admin/")) `
    -BodyMarkers @(
        '<div id="app"></div>',
        'type="module"',
        '/admin/assets/admin.js',
        '/admin/assets/admin.css'
    )
$adminScriptProbe = Invoke-HttpProbe `
    -Uri ([uri]::new($AdminBaseUrl, "admin/assets/admin.js")) `
    -BodyMarkers @(
        'chunk-ServersView.js',
        'chunk-UsersView.js',
        'chunk-ProfilesView.js',
        'chunk-TelemetryView.js',
        'chunk-RuntimeView.js',
        'chunk-ControlView.js',
        'chunk-AlertsView.js',
        'chunk-DiagnosticsView.js',
        'chunk-AuditView.js'
    )
$adminStyleProbe = Invoke-HttpProbe `
    -Uri ([uri]::new($AdminBaseUrl, "admin/assets/admin.css")) `
    -BodyMarkers @(
        '.console-shell',
        '.primary-nav',
        '.brand-mark'
    )
$adminRouteProbes = [ordered]@{}
foreach ($route in @(
    "servers",
    "users",
    "profiles",
    "telemetry",
    "runtime",
    "control",
    "alerts",
    "diagnostics",
    "audit"
)) {
    $adminRouteProbes[$route] = Invoke-HttpProbe -Uri (
        [uri]::new($AdminBaseUrl, "admin/$route")
    )
}
$adminUnauthorizedProbe = Invoke-HttpProbe -Uri (
    [uri]::new($AdminBaseUrl, "v1/admin/catalog/servers")
)
$sessionUnauthorizedProbe = Invoke-HttpProbe -Uri (
    [uri]::new($AdminBaseUrl, "v1/admin-auth/session")
)
$wrongHostProbe = Invoke-HttpProbe -Uri (
    [uri]::new($ApiBaseUrl, "admin/")
)
$publicSiteProbe = Invoke-HttpProbe -Uri $PublicSiteUrl
$relayProbe = Invoke-HttpProbe -Uri $RelayUrl
$serviceAfter = Get-RemoteServiceSnapshot
$script:HttpClient.Dispose()

$checks = [Collections.Generic.List[object]]::new()

$expectedReleasePath = "/opt/hechao-launcher-api/releases/$ExpectedRelease"
Add-Check -Checks $checks -Kind Technical -Code "api_release" `
    -Passed ($serviceAfter.current -eq $expectedReleasePath) `
    -Message "The production release must match $ExpectedRelease."
Add-Check -Checks $checks -Kind Technical -Code "api_service_active" `
    -Passed (
        $serviceAfter.active -eq "active" -and
        [int64]$serviceAfter.mainPid -gt 0
    ) `
    -Message "The production API service must be active with a valid PID."
Add-Check -Checks $checks -Kind Technical -Code "api_process_unchanged" `
    -Passed (
        $serviceBefore.current -eq $serviceAfter.current -and
        [int64]$serviceBefore.mainPid -eq [int64]$serviceAfter.mainPid
    ) `
    -Message "The read-only audit must not switch or restart the API."
Add-Check -Checks $checks -Kind Technical -Code "admin_web_enabled" `
    -Passed ($serviceAfter.adminEnabled -eq "true") `
    -Message "AdminWeb__Enabled must remain true."
Add-Check -Checks $checks -Kind Technical -Code "data_protection_acl" `
    -Passed (
        $serviceAfter.keyRing.owner -eq "hechao-api:hechao-api" -and
        $serviceAfter.keyRing.mode -eq "700" -and
        [int]$serviceAfter.keyRing.files -ge 1
    ) `
    -Message "The admin Data Protection key ring must be private and populated."
Add-Check -Checks $checks -Kind Technical -Code "api_warning_log" `
    -Passed ([int]$serviceAfter.warningCount30m -eq 0) `
    -Message "The API should have no warning-or-higher journal lines in 30 minutes."

$healthJson = $null
$readyJson = $null
if ($healthProbe.statusCode -eq 200) {
    $healthBody = Invoke-RestMethod `
        -Uri ([uri]::new($ApiBaseUrl, "healthz")) `
        -TimeoutSec 15
    $healthJson = $healthBody
}
if ($readyProbe.statusCode -eq 200) {
    $readyBody = Invoke-RestMethod `
        -Uri ([uri]::new($ApiBaseUrl, "readyz")) `
        -TimeoutSec 15
    $readyJson = $readyBody
}
Add-Check -Checks $checks -Kind Technical -Code "api_health" `
    -Passed (
        $healthProbe.statusCode -eq 200 -and
        $healthJson.status -eq "ok" -and
        $healthJson.version -eq $ExpectedApiVersion
    ) `
    -Message "The public health endpoint must report the expected API version."
Add-Check -Checks $checks -Kind Technical -Code "api_readiness" `
    -Passed (
        $readyProbe.statusCode -eq 200 -and
        $readyJson.status -eq "ready" -and
        $readyJson.database -eq "ready" -and
        $readyJson.version -eq $ExpectedApiVersion
    ) `
    -Message "The public readiness endpoint and database must be ready."

$adminIndexMarkersPassed = @(
    $adminIndexProbe.markers.GetEnumerator() |
        Where-Object { -not $_.Value }
).Count -eq 0
$adminScriptMarkersPassed = @(
    $adminScriptProbe.markers.GetEnumerator() |
        Where-Object { -not $_.Value }
).Count -eq 0
$adminStyleMarkersPassed = @(
    $adminStyleProbe.markers.GetEnumerator() |
        Where-Object { -not $_.Value }
).Count -eq 0
$adminRoutesPassed = @(
    $adminRouteProbes.GetEnumerator() |
        Where-Object {
            $_.Value.statusCode -ne 200 -or
            $_.Value.contentSha256 -ne $adminIndexProbe.contentSha256
        }
).Count -eq 0
Add-Check -Checks $checks -Kind Technical -Code "admin_static_assets" `
    -Passed (
        $adminIndexProbe.statusCode -eq 200 -and
        $adminScriptProbe.statusCode -eq 200 -and
        $adminStyleProbe.statusCode -eq 200 -and
        $adminIndexMarkersPassed -and
        $adminScriptMarkersPassed -and
        $adminStyleMarkersPassed -and
        $adminRoutesPassed
    ) `
    -Message "The Vue entry, nine lazy routes, styles, and rendering chunks must be deployed."
Add-Check -Checks $checks -Kind Technical -Code "admin_security_headers" `
    -Passed (
        $adminIndexProbe.cacheControl -match "no-store" -and
        $adminIndexProbe.xFrameOptions -eq "DENY" -and
        $adminIndexProbe.referrerPolicy -eq "no-referrer" -and
        $adminIndexProbe.xContentTypeOptions -eq "nosniff" -and
        $adminIndexProbe.contentSecurityPolicy -match "default-src 'self'"
    ) `
    -Message "The admin entry page must retain its browser security boundary."
Add-Check -Checks $checks -Kind Technical -Code "admin_anonymous_rejected" `
    -Passed (
        $adminUnauthorizedProbe.statusCode -eq 401 -and
        $sessionUnauthorizedProbe.statusCode -eq 401
    ) `
    -Message "Anonymous admin API and session requests must be rejected."
Add-Check -Checks $checks -Kind Technical -Code "admin_wrong_host_rejected" `
    -Passed ($wrongHostProbe.statusCode -eq 404) `
    -Message "The launcher API hostname must not expose the admin site."
Add-Check -Checks $checks -Kind Technical -Code "legacy_public_entries" `
    -Passed (
        $publicSiteProbe.statusCode -eq 200 -and
        $relayProbe.statusCode -eq 200
    ) `
    -Message "The public site and legacy relay entry must remain reachable."

Add-Check -Checks $checks -Kind Technical -Code "database_migrations" `
    -Passed (
        [int]$database.migrations.count -eq $ExpectedMigration -and
        [int]$database.migrations.maximumVersion -eq $ExpectedMigration
    ) `
    -Message "The production database must contain migrations 1 through $ExpectedMigration."
Add-Check -Checks $checks -Kind Technical -Code "administrator_mfa" `
    -Passed (
        [int]$database.administratorSecurity.mfaCredentials -eq
            $ExpectedMfaCredentialCount -and
        [int]$database.administratorSecurity.recoveryCodeHashes -eq
            $ExpectedRecoveryCodeHashCount -and
        [int]$database.administratorSecurity.activeEnrollments -eq 0
    ) `
    -Message "The real administrator MFA credential and recovery hashes must remain intact."
Add-Check -Checks $checks -Kind External -Code "active_mfa_browser_session" `
    -Passed (
        [int]$database.administratorSecurity.activeMfaSessions -ge 1
    ) `
    -Message "A fresh MFA-verified browser session is required for visual page acceptance."

$requiredTiers = @(
    "Member",
    "Participant",
    "Collaborator",
    "Administrator"
)
foreach ($tier in $requiredTiers) {
    $tierRow = @(
        $database.accounts.tiers |
            Where-Object tier -EQ $tier
    ) | Select-Object -First 1
    $linked = if ($null -eq $tierRow) {
        0
    } else {
        [int]$tierRow.linkedCount
    }
    Add-Check -Checks $checks -Kind External `
        -Code "linked_tier_$($tier.ToLowerInvariant())" `
        -Passed ($linked -ge 1) `
        -Message "At least one legitimate Minecraft-linked $tier account is required."
}

$groups = @($database.luckPerms.groups)
$requiredGroups = @("default", "vip", "admin", "owner")
$allGroupsPresent = $true
foreach ($group in $requiredGroups) {
    $groupRow = @(
        $groups | Where-Object primaryGroup -EQ $group
    ) | Select-Object -First 1
    if ($null -eq $groupRow -or
        [int]$groupRow.playerCount -lt 1 -or
        [int]$groupRow.freshCount -ne [int]$groupRow.playerCount) {
        $allGroupsPresent = $false
    }
}
Add-Check -Checks $checks -Kind Technical -Code "luckperms_snapshot" `
    -Passed (
        [int]$database.luckPerms.snapshotTotal -gt 0 -and
        [int]$database.luckPerms.snapshotFreshTenMinutes -eq
            [int]$database.luckPerms.snapshotTotal -and
        $allGroupsPresent
    ) `
    -Message "The LuckPerms snapshot must be fresh and contain all mapped groups."

$servers = @($database.catalog.servers)
$lobby = @(
    $servers | Where-Object id -EQ "lobby"
) | Select-Object -First 1
Add-Check -Checks $checks -Kind Technical -Code "lobby_isolation" `
    -Passed (
        $null -ne $lobby -and
        $lobby.role -eq "Infrastructure" -and
        -not [bool]$lobby.visible -and
        [bool]$lobby.monitoringEnabled -and
        [bool]$lobby.runtime.online -and
        [int]$lobby.runtime.players -eq 0
    ) `
    -Message "Lobby must remain hidden, monitored, online, and empty."

$runtimeNow = [DateTimeOffset]$database.capturedAtUtc
$runtimeFailures = @(
    $servers |
        Where-Object monitoringEnabled |
        Where-Object {
            $null -eq $_.runtime -or
            $null -eq $_.runtime.receivedAt -or
            ($runtimeNow - [DateTimeOffset]$_.runtime.receivedAt).
                TotalSeconds -gt $MaximumRuntimeAgeSeconds
        }
)
Add-Check -Checks $checks -Kind Technical -Code "runtime_freshness" `
    -Passed ($runtimeFailures.Count -eq 0) `
    -Message "Every monitored target must have a fresh runtime sample."

$infrastructurePlayers = @(
    $servers |
        Where-Object {
            $_.role -eq "Infrastructure" -and
            [int]$_.runtime.players -gt 0
        }
)
Add-Check -Checks $checks -Kind Technical `
    -Code "infrastructure_target_empty" `
    -Passed ($infrastructurePlayers.Count -eq 0) `
    -Message "Infrastructure targets must not contain players."

$activeAlerts = @($database.alerts.active)
$criticalAlerts = @(
    $activeAlerts | Where-Object severity -EQ "Critical"
)
Add-Check -Checks $checks -Kind Technical -Code "no_critical_alerts" `
    -Passed ($criticalAlerts.Count -eq 0) `
    -Message "No Critical operational alert may remain active."
$warningAlerts = @(
    $activeAlerts | Where-Object severity -EQ "Warning"
)
Add-Check -Checks $checks -Kind Advisory -Code "active_warning_alerts" `
    -Passed ($warningAlerts.Count -eq 0) `
    -Message "Active Warning alerts require observation before larger player stages."

Add-Check -Checks $checks -Kind Technical -Code "work_queues_empty" `
    -Passed (
        [int]$database.queues.forumRevocationsPending -eq 0 -and
        [int]$database.queues.tierCommandsPending -eq 0 -and
        [int]$database.queues.launchGrantsUnused -eq 0
    ) `
    -Message "Security and authorization work queues must not contain abandoned work."

$profiles = @($database.catalog.profiles)
$profileFailures = @(
    $profiles |
        Where-Object active |
        Where-Object {
            $production = @(
                $_.channels |
                    Where-Object channel -EQ "production"
            ) | Select-Object -First 1
            $null -eq $production -or
            [string]::IsNullOrWhiteSpace($production.releaseSha256) -or
            [int]$production.rolloutPercentage -ne 100 -or
            [bool]$production.releasePaused
        }
)
Add-Check -Checks $checks -Kind Technical `
    -Code "production_profile_channels" `
    -Passed ($profiles.Count -eq 6 -and $profileFailures.Count -eq 0) `
    -Message "All six active profiles need an unpaused 100 percent production release."

Add-Check -Checks $checks -Kind Technical -Code "telemetry_samples" `
    -Passed (
        [int]$database.telemetry.events24Hours -gt 0 -and
        [int]$database.telemetry.users24Hours -gt 0
    ) `
    -Message "Production launcher telemetry must contain recent real-client samples."
Add-Check -Checks $checks -Kind Technical -Code "diagnostic_chain" `
    -Passed (
        [int]$database.diagnostics.uploaded -ge 1 -and
        @(
            $database.audit.requiredActions |
                Where-Object action -EQ "diagnostic.admin.downloaded" |
                Where-Object { [int]$_.count -ge 1 }
        ).Count -eq 1
    ) `
    -Message "At least one verified diagnostic upload and administrator download audit must exist."

$missingAuditActions = @(
    $database.audit.requiredActions |
        Where-Object { [int]$_.count -lt 1 }
)
Add-Check -Checks $checks -Kind Technical -Code "required_audit_actions" `
    -Passed ($missingAuditActions.Count -eq 0) `
    -Message "Required MFA, catalog, diagnostic, and launch-grant audit actions must exist."

Add-Check -Checks $checks -Kind External `
    -Code "catalog_authentication_enforced" `
    -Passed ($serviceAfter.catalogAuthEnforced -eq "true") `
    -Message "Catalog authentication remains intentionally disabled until real-account and Velocity enforcement acceptance."

$technicalFailures = @(
    $checks |
        Where-Object {
            $_.kind -eq "Technical" -and
            $_.status -ne "Passed"
        }
)
$externalBlockers = @(
    $checks |
        Where-Object {
            $_.kind -eq "External" -and
            $_.status -ne "Passed"
        }
)
$advisories = @(
    $checks |
        Where-Object {
            $_.kind -eq "Advisory" -and
            $_.status -ne "Passed"
        }
)

$completedAt = [DateTimeOffset]::UtcNow
$result = [ordered]@{
    schemaVersion = 1
    scope = "Read-only production control-plane acceptance"
    startedAtUtc = $startedAt
    completedAtUtc = $completedAt
    durationSeconds = [math]::Round(
        ($completedAt - $startedAt).TotalSeconds,
        3
    )
    readiness = [ordered]@{
        technicalReady = $technicalFailures.Count -eq 0
        externalReady = $externalBlockers.Count -eq 0
        technicalFailureCount = $technicalFailures.Count
        externalBlockerCount = $externalBlockers.Count
        advisoryCount = $advisories.Count
    }
    service = [ordered]@{
        before = $serviceBefore
        after = $serviceAfter
    }
    publicProbes = [ordered]@{
        health = $healthProbe
        readiness = $readyProbe
        adminIndex = $adminIndexProbe
        adminScript = $adminScriptProbe
        adminStyle = $adminStyleProbe
        adminRoutes = $adminRouteProbes
        anonymousAdmin = $adminUnauthorizedProbe
        anonymousSession = $sessionUnauthorizedProbe
        wrongHostAdmin = $wrongHostProbe
        publicSite = $publicSiteProbe
        relay = $relayProbe
    }
    database = $database
    checks = $checks
    invariants = [ordered]@{
        databaseWritesPerformed = $false
        catalogStateChanged = $false
        apiRestarted = (
            [int64]$serviceBefore.mainPid -ne
            [int64]$serviceAfter.mainPid
        )
        gameServerControlPerformed = $false
        credentialsOrTokensIncluded = $false
        personalAccountDetailsIncluded = $false
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    $outputDirectory = (Get-Location).Path
}
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$result |
    ConvertTo-Json -Depth 16 |
    Set-Content -LiteralPath $OutputPath -Encoding utf8

if ($AsJson) {
    $result | ConvertTo-Json -Depth 16
} else {
    $result
}
