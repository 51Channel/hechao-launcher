#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$tasks = foreach ($task in Get-ScheduledTask) {
    foreach ($action in $task.Actions) {
        $usesPowerShell = $action.Execute -match "(?i)(powershell|pwsh)(\.exe)?$"
        $usesScript = $action.Arguments -match "(?i)\.ps1(?:\s|$|`")"
        if (-not $usesPowerShell -and -not $usesScript) {
            continue
        }

        [pscustomobject]@{
            taskName = $task.TaskName
            taskPath = $task.TaskPath
            state = $task.State.ToString()
            execute = $action.Execute
            arguments = $action.Arguments
            workingDirectory = $action.WorkingDirectory
        }
    }
}

[pscustomobject]@{
    computerName = $env:COMPUTERNAME
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    tasks = @($tasks)
} | ConvertTo-Json -Depth 5
