[CmdletBinding()]
param(
    [string]$RequestRoot = 'C:\ProgramData\Hechao\ServerControl\Requests',

    [string]$BridgeScript = 'C:\ProgramData\Hechao\ServerControl\Send-MinecraftConsoleCommand.ps1'
)

$ErrorActionPreference = 'Stop'

$pendingDirectory = Join-Path $RequestRoot 'Pending'
$completedDirectory = Join-Path $RequestRoot 'Completed'
$failedDirectory = Join-Path $RequestRoot 'Failed'
foreach ($directory in @($pendingDirectory, $completedDirectory, $failedDirectory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$mutex = [System.Threading.Mutex]::new(
    $false,
    'Global\HechaoMinecraftConsoleBridge')
$lockAcquired = $false
try {
    $lockAcquired = $mutex.WaitOne(0)
    if (-not $lockAcquired) {
        return
    }

    $requests = Get-ChildItem -LiteralPath $pendingDirectory -Filter '*.json' -File |
        Sort-Object CreationTimeUtc, Name

    foreach ($requestFile in $requests) {
        $requestId = [System.IO.Path]::GetFileNameWithoutExtension(
            $requestFile.Name)
        $resultPath = Join-Path $completedDirectory "$requestId.json"
        $failedPath = Join-Path $failedDirectory "$requestId.json"
        $requestArchivePath = Join-Path $completedDirectory (
            "$requestId.request.json"
        )

        try {
            if ($requestId -notmatch '^[a-f0-9]{32}$') {
                throw 'Request filename is not a valid request identifier.'
            }

            $request = Get-Content -Raw -LiteralPath $requestFile.FullName |
                ConvertFrom-Json
            if ([string]$request.request_id -ne $requestId) {
                throw 'Request identifier does not match its filename.'
            }

            $processId = [int]$request.process_id
            $command = [string]$request.command
            $bridgeResult = & $BridgeScript `
                -ProcessId $processId `
                -Command $command |
                ConvertFrom-Json

            $response = [ordered]@{
                request_id = $requestId
                status = 'succeeded'
                process_id = $processId
                command = $command
                bridge = $bridgeResult
                completed_at_utc = (
                    Get-Date
                ).ToUniversalTime().ToString('o')
            }
            $temporaryResult = "$resultPath.tmp"
            [System.IO.File]::WriteAllText(
                $temporaryResult,
                ($response | ConvertTo-Json -Depth 6 -Compress),
                [System.Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporaryResult -Destination $resultPath -Force
            Move-Item `
                -LiteralPath $requestFile.FullName `
                -Destination $requestArchivePath `
                -Force
        }
        catch {
            $failure = [ordered]@{
                request_id = $requestId
                status = 'failed'
                error = $_.Exception.Message
                completed_at_utc = (
                    Get-Date
                ).ToUniversalTime().ToString('o')
            }
            $temporaryFailure = "$failedPath.tmp"
            [System.IO.File]::WriteAllText(
                $temporaryFailure,
                ($failure | ConvertTo-Json -Depth 4 -Compress),
                [System.Text.UTF8Encoding]::new($false))
            Move-Item `
                -LiteralPath $temporaryFailure `
                -Destination $failedPath `
                -Force
            Move-Item `
                -LiteralPath $requestFile.FullName `
                -Destination (
                    Join-Path $failedDirectory "$requestId.request.json"
                ) `
                -Force
        }
    }
}
finally {
    if ($lockAcquired) {
        [void]$mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
