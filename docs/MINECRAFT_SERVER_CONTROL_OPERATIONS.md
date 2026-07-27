# Minecraft server control operations

## Purpose

The Windows game hosts use visible Java console windows. The checked-in
server-control tools provide two narrowly scoped operations:

- send a Minecraft console command to a specific existing `java.exe` process;
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
powershell.exe -NoProfile -File `
  C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1 `
  -ProcessId 1234 `
  -Command list
```

Always verify the matching server's `logs/latest.log` after a command. A
successful console write means Windows accepted the input events; the log is
the authoritative proof that Minecraft processed the command.

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

Then run the task installer while the target Administrator desktop session is
logged in:

```powershell
.\Install-MinecraftServerLaunchTask.ps1 `
  -ServerName Survival1 `
  -ServerDirectory E:\Survival1
```

The resulting task is named `Hechao-Server-Survival1`. It has no trigger and
therefore runs only after an explicit:

```powershell
Start-ScheduledTask -TaskName Hechao-Server-Survival1
```

An existing task with the same name is exported below
`E:\manual-backups\server-control-<UTC timestamp>` before replacement.

## Safe restart sequence

For one server at a time:

1. Send `list` and record the player count.
2. Notify connected players and wait for the agreed drain window.
3. Send `save-all flush` and verify `Saved the game` in `latest.log`.
4. Send `stop` and wait until both Java PID and listening port are gone.
5. Start only that server's on-demand task.
6. Wait for `Done (...)! For help, type "help"` and its port listener.
7. Verify required plugins or mods and the metrics output.

Never use process termination as the normal shutdown path.
