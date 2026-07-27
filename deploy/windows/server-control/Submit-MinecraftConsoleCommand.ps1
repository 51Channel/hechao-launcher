[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,

    [Parameter(Mandatory)]
    [ValidateScript({
        if ([string]::IsNullOrWhiteSpace($_)) {
            throw 'Command cannot be empty.'
        }
        if ($_.Length -gt 256 -or $_ -match "[`r`n`0]") {
            throw 'Command is too long or contains a forbidden character.'
        }
        $true
    })]
    [string]$Command,

    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30,

    [string]$RequestRoot = 'C:\ProgramData\Hechao\ServerControl\Requests',

    [string]$TaskName = 'Hechao-MinecraftConsoleBridge'
)

$ErrorActionPreference = 'Stop'

$pendingDirectory = Join-Path $RequestRoot 'Pending'
$completedDirectory = Join-Path $RequestRoot 'Completed'
$failedDirectory = Join-Path $RequestRoot 'Failed'
foreach ($directory in @($pendingDirectory, $completedDirectory, $failedDirectory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$requestId = [Guid]::NewGuid().ToString('N')
$requestPath = Join-Path $pendingDirectory "$requestId.json"
$temporaryRequestPath = "$requestPath.tmp"
$completedPath = Join-Path $completedDirectory "$requestId.json"
$failedPath = Join-Path $failedDirectory "$requestId.json"
$request = [ordered]@{
    request_id = $requestId
    process_id = $ProcessId
    command = $Command
    submitted_at_utc = (Get-Date).ToUniversalTime().ToString('o')
}

[System.IO.File]::WriteAllText(
    $temporaryRequestPath,
    ($request | ConvertTo-Json -Compress),
    [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryRequestPath -Destination $requestPath -Force

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastStartAttempt = [datetime]::MinValue
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $completedPath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $completedPath
        return
    }

    if (Test-Path -LiteralPath $failedPath -PathType Leaf) {
        $failure = Get-Content -Raw -LiteralPath $failedPath
        throw "Console command request failed: $failure"
    }

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    if ($task.State -ne 'Running' -and
        ((Get-Date) - $lastStartAttempt).TotalSeconds -ge 1) {
        Start-ScheduledTask -TaskName $TaskName
        $lastStartAttempt = Get-Date
    }

    Start-Sleep -Milliseconds 250
}

throw "Console command request $requestId timed out after $TimeoutSeconds seconds."
