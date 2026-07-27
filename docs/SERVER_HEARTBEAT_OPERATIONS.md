# Server heartbeat operations

> Production state: API `0.20.1-20260727T145451Z` and collector `0.2.0` deployed.
> Since 2026-07-28, `owl5` collects its four local targets and `owl9` collects `pvp`.
> Both one-minute scheduled tasks are outbound-only and read-only.
> The pre-hardening collector binary is retained in
> `C:\ProgramData\Hechao\StatusCollector\backups\redirect-hardening-20260723T125705Z`.

The server heartbeat pipeline reports live Minecraft status to the launcher API without
starting, stopping, or restarting any game process. Each collector probes only Minecraft
ports on its own host and sends an outbound HTTPS request to the API.

## Runtime model

- API endpoint: `POST /v1/internal/server-heartbeats`
- Authentication header: `X-Hechao-Heartbeat-Token`
- API configuration:
  - `ServerHeartbeats__InternalTokenSha256`
  - `ServerHeartbeats__FreshnessSeconds` (default `180`)
- Windows task: `Hechao Launcher Server Heartbeats`
- Windows state directory on both game VPS hosts:
  `C:\ProgramData\Hechao\StatusCollector`
- Collection interval: one minute
- Collector executable SHA-256:
  `354186EF1D1B559D72107E80AD56467371CF7D59FCB31D5763E4C7B2B7F4A424`
- Collector ProductVersion:
  `0.2.0+7ba2eba1ee7b6c6307948da9e4084ea6f6406fb7`

Heartbeats are keyed by Velocity target rather than catalog server ID. This is required
because replacement activities can share a Velocity target with another catalog entry.
The catalog's configured `Maintenance` or `Closed` state always wins. A catalog
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

For a non-interactive deployment, pass the token only through standard input and add
`-ReadFromStandardInput`. The script never accepts the clear token as a command-line
parameter:

```powershell
$token | powershell.exe -NoProfile -File .\Protect-HeartbeatToken.ps1 `
    -ReadFromStandardInput
```

## Collector installation

1. Publish `Hechao.StatusCollector` as self-contained `win-x64`.
2. Copy the published files to `C:\ProgramData\Hechao\StatusCollector`.
3. Copy `server-heartbeats.example.json` as `server-heartbeats.json`.
4. Protect the token with `Protect-HeartbeatToken.ps1`.
5. Register the task with `Install-ServerHeartbeatCollector.ps1`.
6. Run the collector executable once and verify a successful API response.
7. Confirm the task's last result is `0`.

The production target ownership is:

| Collector instance | Velocity target | Local endpoint | Fallback maximum | Notes |
| --- | --- | --- | --- | --- |
| `mc-vps-primary` | `lobby` | `127.0.0.1:25566` | `300` | Lobby |
| `mc-vps-primary` | `survival2` | `127.0.0.1:25565` | `100` | Survival2 or its active replacement |
| `mc-vps-primary` | `survival1` | `127.0.0.1:19228` | `20` | Survival1 |
| `mc-vps-primary` | `activity` | `127.0.0.1:25568` | `30` | NeoForge activity server |
| `owl9-pvp` | `pvp` | `127.0.0.1:25565` | `20` | PVP Fabric backend on `owl9` |

Only add a target after it exists in `launcher.servers`; the API rejects unknown targets
atomically.

The pre-change configuration is retained at:

```text
C:\ProgramData\Hechao\StatusCollector\backups\server-heartbeats-before-targets-20260726-042625.json
C:\ProgramData\Hechao\StatusCollector\backups\server-heartbeats-before-owl9-split-20260727T165932Z.json
```

The first five-target manual production run observed:

| Target | Observation |
| --- | --- |
| `lobby` | `0/200`, Purpur 1.21.11, protocol 774 |
| `survival2` | `0/100`, online |
| `survival1` | `0/20`, Purpur 1.21.11, protocol 774 |
| `pvp` | `0/20`, Minecraft 1.20.1, protocol 763 |
| `activity` | offline; connection failure isolated to this target |

One offline target does not abort the remaining batch and is not a reason to start that server.

## Owl9 collector split

On 2026-07-28 the `pvp` probe was moved from `mc-vps-primary` to a dedicated
`owl9-pvp` collector. The clear heartbeat token was transferred through standard input,
protected immediately with machine-scope DPAPI, and was never placed in arguments,
configuration, logs, or repository files.

The `owl9` installation passed all of the following checks:

- collector and configuration hashes match the reviewed artifacts;
- the directory ACL allows only `SYSTEM` and local administrators;
- the one-minute task runs as `SYSTEM` and returns `0`;
- the API row remains owned by `owl9-pvp` after both collectors crossed a full schedule
  cycle;
- the stopped PVP server is reported with `ProcessNotRunning` and
  `MetricsFileMissing`, while the local disk capacity is still reported;
- Java process count and the local `25565` listener remained zero before and after
  deployment.

Machine-readable evidence is in
[`evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json).

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
    Where-Object LocalPort -In 19228,25565,25566,25568
```

Run the task checks separately on `owl5` and `owl9`. The final command is read-only. A
missing listener is reported as offline; it must not be used as a reason to start a server
automatically.

## Rollback

To roll back only the `owl9` collector, remove its heartbeat task and leave all Minecraft
processes untouched:

```powershell
Unregister-ScheduledTask -TaskName 'Hechao Launcher Server Heartbeats' -Confirm:$false
```

Restore the backed-up five-target JSON on `owl5` with a validated atomic replacement
before removing the `owl9` task, so the API does not lose the PVP heartbeat. Never run
both PVP probes long-term because the last writer owns the row.

If the API is rolled back to a release before migration 4, the heartbeat table can remain.
Database migrations are forward-only and must not be deleted manually.
