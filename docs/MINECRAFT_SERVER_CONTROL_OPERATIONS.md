# Minecraft server control operations

All commands in this runbook use PowerShell 7 (`pwsh.exe`). The automated,
allowlisted API and agent design is documented in
[`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md).

## Purpose

The Windows game hosts use visible Java console windows. The checked-in
server-control tools provide three narrowly scoped operations:

- send a Minecraft console command to a specific existing `java.exe` process;
- send one `Ctrl+C` event to that process console only as the structured stop fallback;
- register an on-demand Scheduled Task that starts one server in the logged-in
  Administrator desktop session.

Neither tool starts, stops, or restarts a server during installation.

## Install the command bridge

Copy the four console bridge scripts from
`deploy/windows/server-control` to:

```text
C:\ProgramData\Hechao\ServerControl
```

Then run `Install-MinecraftConsoleBridge.ps1`. It registers the fixed,
on-demand `Hechao-MinecraftConsoleBridge` task in the logged-in Administrator
desktop session. This session boundary matters: an SSH process cannot attach
directly to a console window in the RDP session.

The submitter writes an atomic request file and starts that task. The desktop
worker validates that the requested PID belongs to `java.exe`, rejects
multi-line input, attaches only to that process console, writes one command,
detaches, and records a success or failure JSON response.

Example:

```powershell
pwsh.exe -NoLogo -NoProfile -File `
  C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1 `
  -ProcessId 1234 `
  -Command list
```

Always verify the matching server's `logs/latest.log` after a command. A
successful console write means Windows accepted the input events; the log is
the authoritative proof that Minecraft processed the command.

The submitter's `-Interrupt` parameter is reserved for the agent's structured stop path.
It revalidates that the PID belongs to `java.exe`, disables QuickEdit in that console, and
triggers the JVM shutdown hook. Managed launches also disable QuickEdit before Java starts
so selecting console text cannot freeze server output. Managed stdout and stderr are
appended directly to `ServerControlAgent\logs\<serverId>-console.log`, with one 64 MiB
previous log retained, so Task Scheduler cannot block Java on an undrained output pipe. The interrupt
must not be exposed as a normal terminal command or replaced with forced process
termination.

## Register an on-demand launcher

First make each existing batch file managed-start aware:

```powershell
.\Enable-MinecraftManagedStart.ps1 -ServerDirectories `
  E:\Survival1,E:\Survival2,E:\LobbyServer,E:\ActivityNeoForge
```

The transformation changes each standalone `pause` statement to:

```batch
if not defined HECHAO_MANAGED_START pause
```

Manual double-click starts still pause when Java exits. A managed start sets
the environment marker, so the scheduled task exits cleanly after a graceful
server shutdown.

Then run the task installer from an elevated PowerShell 7 session:

```powershell
.\Install-MinecraftServerLaunchTask.ps1 `
  -ServerName Survival1 `
  -ServerId survival1 `
  -ServerDirectory E:\Survival1
```

The resulting task is named `Hechao-Server-Survival1`. It has no trigger and
therefore runs only after an explicit:

```powershell
Start-ScheduledTask -TaskName Hechao-Server-Survival1
```

The task uses the passwordless `S4U` logon type. It can therefore start while
the target Administrator has no interactive desktop session, including after
a VPS reboot. Do not change managed launch tasks back to `InteractiveToken`;
Task Scheduler can accept `/Run` without actually launching the runner when no
matching desktop session exists.

An existing task with the same name is exported below
`E:\manual-backups\server-control-<UTC timestamp>` before replacement.
The managed runner also writes a per-run identity marker below
`C:\ProgramData\Hechao\ServerControlAgent\runtime`. For targets sharing one
port, the automated agent requires this marker and verifies that the listening
Java process descends from the marked runner. An unknown port owner is never
treated as either configured server.

## Safe restart sequence

For one server at a time:

1. Send `list` and record the player count.
2. Notify connected players and wait for the agreed drain window.
3. Send `save-all flush` and verify `Saved the game` in `latest.log`.
4. Send `stop` and wait until both Java PID and listening port are gone. If the same
   managed Java PID still owns the port after 20 seconds, send the one-time console
   interrupt fallback and verify the JVM shutdown log.
5. Start only that server's on-demand task.
6. Wait for `Done (...)! For help, type "help"` and its port listener.
7. Verify required plugins or mods and the metrics output.

Never use process termination as the normal shutdown path.
