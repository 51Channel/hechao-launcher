[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$ServerId,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [Parameter(Mandatory)]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter(Mandatory)]
    [string]$RuntimeTaskName,

    [string]$RuntimeMarkerDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent\runtime",

    [switch]$Replace
)

$ErrorActionPreference = 'Stop'

function Test-ContainsOrdinalIgnoreCase {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Value
    )

    return $Text.IndexOf(
        $Value,
        [System.StringComparison]::OrdinalIgnoreCase
    ) -ge 0
}

$resolvedDirectory = (Resolve-Path -LiteralPath $ServerDirectory).Path.
    TrimEnd('\')
$task = Get-ScheduledTask `
    -TaskName $RuntimeTaskName `
    -ErrorAction Stop
if ($task.State -ne 'Running') {
    throw "Runtime task is not running: $RuntimeTaskName"
}

$taskIdentity = @($task.Actions | ForEach-Object {
    "$($_.Execute) $($_.Arguments) $($_.WorkingDirectory)"
}) -join ' '
if (-not (Test-ContainsOrdinalIgnoreCase `
        -Text $taskIdentity `
        -Value $resolvedDirectory)) {
    throw (
        "Runtime task $RuntimeTaskName does not identify " +
        "$resolvedDirectory."
    )
}

$listenerProcessIds = @(
    Get-NetTCPConnection `
        -State Listen `
        -LocalPort $Port `
        -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
)
if ($listenerProcessIds.Count -ne 1 -or $listenerProcessIds[0] -le 0) {
    throw "Port $Port does not have exactly one listening process."
}

$listenerProcessId = [int]$listenerProcessIds[0]
$listenerProcess = Get-Process `
    -Id $listenerProcessId `
    -ErrorAction Stop
if ($listenerProcess.ProcessName -notin @('java', 'javaw')) {
    throw "Port $Port is not owned by a Java process."
}

$runnerRow = $null
$currentProcessId = $listenerProcessId
for ($index = 0; $index -lt 24 -and $currentProcessId -gt 0; $index++) {
    $row = Get-CimInstance `
        -ClassName Win32_Process `
        -Filter "ProcessId = $currentProcessId" `
        -ErrorAction SilentlyContinue
    if ($null -eq $row) {
        break
    }

    $commandLine = [string]$row.CommandLine
    if ($row.Name -in @('pwsh.exe', 'powershell.exe', 'cmd.exe') -and
        (Test-ContainsOrdinalIgnoreCase `
            -Text $commandLine `
            -Value $resolvedDirectory)) {
        $runnerRow = $row
        break
    }

    $currentProcessId = [int]$row.ParentProcessId
}

if ($null -eq $runnerRow) {
    throw (
        "The Java listener on port $Port is not descended from a verified " +
        "runtime process for $resolvedDirectory."
    )
}

$runnerProcess = Get-Process `
    -Id ([int]$runnerRow.ProcessId) `
    -ErrorAction Stop
$resolvedMarkerDirectory = [System.IO.Path]::GetFullPath(
    $RuntimeMarkerDirectory
)
$markerPath = Join-Path $resolvedMarkerDirectory "$ServerId.json"
$marker = [ordered]@{
    schemaVersion = 1
    serverId = $ServerId
    runId = [Guid]::NewGuid().ToString('N')
    runnerProcessId = [int]$runnerRow.ProcessId
    runnerStartedAtUtcTicks =
        $runnerProcess.StartTime.ToUniversalTime().Ticks
    serverDirectory = $resolvedDirectory
    startedAt = $runnerProcess.StartTime.ToUniversalTime().ToString('o')
}

if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
    $existing = Get-Content -Raw -LiteralPath $markerPath |
        ConvertFrom-Json
    $matchesCurrentRuntime =
        [string]$existing.serverId -ceq $ServerId -and
        [int]$existing.runnerProcessId -eq [int]$runnerRow.ProcessId -and
        [long]$existing.runnerStartedAtUtcTicks -eq
            $marker.runnerStartedAtUtcTicks -and
        [string]::Equals(
            [System.IO.Path]::GetFullPath(
                [string]$existing.serverDirectory
            ).TrimEnd('\'),
            $resolvedDirectory,
            [System.StringComparison]::OrdinalIgnoreCase)

    if ($matchesCurrentRuntime) {
        [ordered]@{
            server_id = $ServerId
            port = $Port
            listener_process_id = $listenerProcessId
            runner_process_id = [int]$runnerRow.ProcessId
            runtime_task = $RuntimeTaskName
            marker = $markerPath
            result = 'already-adopted'
            server_action = 'none'
        } | ConvertTo-Json -Compress
        return
    }

    if (-not $Replace) {
        throw (
            "A different runtime marker already exists for $ServerId. " +
            'Use -Replace only after independently verifying the new runtime.'
        )
    }
}

if ($PSCmdlet.ShouldProcess(
        $markerPath,
        "Adopt verified Java listener $listenerProcessId")) {
    [System.IO.Directory]::CreateDirectory(
        $resolvedMarkerDirectory
    ) | Out-Null
    $temporaryPath = Join-Path $resolvedMarkerDirectory (
        ".$ServerId-$([Guid]::NewGuid().ToString('N')).tmp"
    )
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            ($marker | ConvertTo-Json -Compress),
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $markerPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

[ordered]@{
    server_id = $ServerId
    port = $Port
    listener_process_id = $listenerProcessId
    runner_process_id = [int]$runnerRow.ProcessId
    runtime_task = $RuntimeTaskName
    marker = $markerPath
    result = if ($WhatIfPreference) { 'verified-only' } else { 'adopted' }
    server_action = 'none'
} | ConvertTo-Json -Compress
