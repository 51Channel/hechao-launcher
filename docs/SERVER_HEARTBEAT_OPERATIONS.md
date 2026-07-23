# Server heartbeat operations

> Production state: API `0.6.0-20260723T123346Z` and collector `0.1.0` deployed
> on 2026-07-23. The first scheduled run completed with result `0`.
> The pre-hardening collector binary is retained in
> `C:\ProgramData\Hechao\StatusCollector\backups\redirect-hardening-20260723T125705Z`.

The server heartbeat pipeline reports live Minecraft status to the launcher API without
starting, stopping, or restarting any game process. The collector only performs the
Minecraft Server List Ping against loopback ports.

## Runtime model

- API endpoint: `POST /v1/internal/server-heartbeats`
- Authentication header: `X-Hechao-Heartbeat-Token`
- API configuration:
  - `ServerHeartbeats__InternalTokenSha256`
  - `ServerHeartbeats__FreshnessSeconds` (default `180`)
- Windows task: `Hechao Launcher Server Heartbeats`
- Windows state directory: `C:\ProgramData\Hechao\StatusCollector`
- Collection interval: one minute
- Collector executable SHA-256:
  `D24201646D13CF0CA4F7261DC06C0EFAF2BC9B425A9B539BD3689095EFAD8DFF`

Heartbeats are keyed by Velocity target rather than catalog server ID. This is required
because `survival2` and replacement activities such as DollNight can share one Velocity
target. The catalog's configured `Maintenance` or `Closed` state always wins. A catalog
row configured as `Online` uses a fresh target heartbeat; an expired or offline heartbeat
is shown as `Closed`.

Before the first heartbeat exists, the API preserves the configured catalog values. This
allows the API migration and collector installation to be deployed independently.

## Token provisioning

Generate one random URL-safe token. Store only its lowercase SHA-256 digest in the API
environment:

```text
ServerHeartbeats__InternalTokenSha256=<64-character SHA-256 digest>
ServerHeartbeats__FreshnessSeconds=180
```

On the game VPS, use `Protect-HeartbeatToken.ps1` from an elevated Windows PowerShell
session. It protects the token with machine-scope DPAPI and restricts the state directory
to `SYSTEM` and local administrators.

Never place the clear token in the repository, command history, task arguments, or
configuration JSON.

## Collector installation

1. Publish `Hechao.StatusCollector` as self-contained `win-x64`.
2. Copy the published files to `C:\ProgramData\Hechao\StatusCollector`.
3. Copy `server-heartbeats.example.json` as `server-heartbeats.json`.
4. Protect the token with `Protect-HeartbeatToken.ps1`.
5. Register the task with `Install-ServerHeartbeatCollector.ps1`.
6. Run the collector executable once and verify a successful API response.
7. Confirm the task's last result is `0`.

The production target list is:

| Velocity target | Loopback endpoint | Notes |
| --- | --- | --- |
| `lobby` | `127.0.0.1:25566` | Lobby |
| `survival2` | `127.0.0.1:25565` | Survival or its active replacement |
| `activity` | `127.0.0.1:25568` | NeoForge activity server |

Only add a target after it exists in `launcher.servers`; the API rejects unknown targets
atomically.

## Verification

API checks:

```bash
curl -fsS https://launcher-api.hechao.world/healthz
curl -fsS https://launcher-api.hechao.world/readyz
```

Database checks:

```sql
SELECT velocity_target, is_online, online_players, max_players,
       collector_instance, captured_at, received_at
FROM launcher.velocity_target_heartbeats
ORDER BY velocity_target;
```

Windows checks:

```powershell
Get-ScheduledTask -TaskName 'Hechao Launcher Server Heartbeats'
Get-ScheduledTaskInfo -TaskName 'Hechao Launcher Server Heartbeats'
Get-NetTCPConnection -State Listen |
    Where-Object LocalPort -In 25565,25566,25568
```

The final command is read-only. A missing listener is reported as offline; it must not be
used as a reason to start a server automatically.

## Rollback

Disable or remove only the heartbeat task and leave all Minecraft processes untouched:

```powershell
Unregister-ScheduledTask -TaskName 'Hechao Launcher Server Heartbeats' -Confirm:$false
```

If the API is rolled back to a release before migration 4, the heartbeat table can remain.
Database migrations are forward-only and must not be deleted manually.
