#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$PowerShellPath = "C:\Program Files\PowerShell\7\pwsh.exe",
    [string]$BackupRoot = "C:\ProgramData\Hechao\Backups"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PowerShellPath)) {
    throw "PowerShell 7 executable not found: $PowerShellPath"
}

$candidates = @(
    foreach ($task in Get-ScheduledTask) {
        if (
            $task.TaskName -notlike "Hechao-*" -and
            $task.TaskName -notlike "Hechao *"
        ) {
            continue
        }

        foreach ($action in $task.Actions) {
            if (
                $action.Execute -notmatch "(?i)(powershell|pwsh)(\.exe)?$" -or
                $action.Arguments -notmatch "(?i)\.ps1(?:\s|$|`")"
            ) {
                continue
            }

            [pscustomobject]@{
                Task = $task
                Action = $action
            }
        }
    }
)

if ($candidates.Count -eq 0) {
    [pscustomobject]@{
        status = "no-migration-needed"
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        tasks = @()
    } | ConvertTo-Json -Depth 5
    return
}

foreach ($candidate in $candidates) {
    if (@($candidate.Task.Actions).Count -ne 1) {
        throw "Task $($candidate.Task.TaskName) has multiple actions and requires manual migration."
    }

    $arguments = $candidate.Action.Arguments
    $scriptPath = $null
    if ($arguments -match '(?i)-File\s+"(?<path>[^"]+\.ps1)"') {
        $scriptPath = $Matches.path
    } elseif ($arguments -match "(?i)-File\s+(?<path>[^\s]+\.ps1)") {
        $scriptPath = $Matches.path
    }

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw "Could not resolve the script path for task $($candidate.Task.TaskName)."
    }

    if (-not (Test-Path -LiteralPath $scriptPath)) {
        throw "Task script not found for $($candidate.Task.TaskName): $scriptPath"
    }

    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors
    ) | Out-Null

    if ($parseErrors.Count -gt 0) {
        throw "PowerShell parse check failed for $scriptPath."
    }
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$backupDirectory = Join-Path $BackupRoot "PowerShell7TaskMigration-$timestamp"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$backups = @()
foreach ($candidate in $candidates) {
    $task = $candidate.Task
    $safeName = ($task.TaskPath.Trim("\") + "_" + $task.TaskName) -replace '[\\/:*?"<>| ]', "_"
    $xmlPath = Join-Path $backupDirectory "$safeName.xml"
    Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath |
        Set-Content -LiteralPath $xmlPath -Encoding utf8NoBOM

    $backups += [pscustomobject]@{
        TaskName = $task.TaskName
        TaskPath = $task.TaskPath
        XmlPath = $xmlPath
    }
}

$changed = @()
try {
    foreach ($candidate in $candidates) {
        $task = $candidate.Task
        $action = $candidate.Action
        $actionParameters = @{
            Execute = $PowerShellPath
            Argument = $action.Arguments
        }

        if (-not [string]::IsNullOrWhiteSpace($action.WorkingDirectory)) {
            $actionParameters.WorkingDirectory = $action.WorkingDirectory
        }

        $newAction = New-ScheduledTaskAction @actionParameters
        Set-ScheduledTask `
            -TaskName $task.TaskName `
            -TaskPath $task.TaskPath `
            -Action $newAction | Out-Null

        $changed += $task.TaskName
    }

    foreach ($backup in $backups) {
        $task = Get-ScheduledTask -TaskName $backup.TaskName -TaskPath $backup.TaskPath
        $action = @($task.Actions)
        if (
            $action.Count -ne 1 -or
            $action[0].Execute -ne $PowerShellPath
        ) {
            throw "Task verification failed for $($backup.TaskName)."
        }
    }
} catch {
    foreach ($backup in $backups) {
        $xml = Get-Content -LiteralPath $backup.XmlPath -Raw
        Register-ScheduledTask `
            -TaskName $backup.TaskName `
            -TaskPath $backup.TaskPath `
            -Xml $xml `
            -Force | Out-Null
    }

    throw
}

[pscustomobject]@{
    status = "migrated"
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    backupDirectory = $backupDirectory
    tasks = $changed
} | ConvertTo-Json -Depth 5
