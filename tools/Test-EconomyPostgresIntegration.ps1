#Requires -Version 7.0

[CmdletBinding()]
param(
    [string]$ApiHost = 'root@8.148.207.171',
    [string]$IdentityFile = "$HOME\.ssh\hechao_deploy",
    [string]$KnownHostsFile = "$HOME\.ssh\hechao_old_rebuilt_known_hosts",
    [string]$PostgresContainer = 'hechao-launcher-postgres',
    [string]$PostgresAdmin = 'hechao_db_admin',
    [ValidateRange(1, 65535)]
    [int]$RemotePort = 5433,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($path in @($IdentityFile, $KnownHostsFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SSH file is missing: $path"
    }
}

$ssh = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
$dotnet = Join-Path $HOME '.dotnet\dotnet.exe'
foreach ($path in @($ssh, $dotnet)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required executable is missing: $path"
    }
}

$databaseName = 'hechao_economy_test_' +
    (Get-Date -Format 'yyyyMMddHHmmss') + '_' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$password = [Guid]::NewGuid().ToString('N') +
    [Guid]::NewGuid().ToString('N')
$commonSshArguments = @(
    '-i', (Resolve-Path -LiteralPath $IdentityFile).Path,
    '-o', 'IdentitiesOnly=yes',
    '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=$((Resolve-Path -LiteralPath $KnownHostsFile).Path)"
)

$listener = [Net.Sockets.TcpListener]::new(
    [Net.IPAddress]::Loopback,
    0)
$listener.Start()
$localPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()

$tunnel = $null
$databaseCreated = $false
try {
    $quote = [char]34
    $createCommand =
        "docker exec $PostgresContainer psql -U $PostgresAdmin -d postgres " +
        "-v ON_ERROR_STOP=1 -c $($quote)CREATE ROLE $databaseName LOGIN " +
        "PASSWORD '$password';$($quote) && " +
        "docker exec $PostgresContainer createdb -U $PostgresAdmin " +
        "-O $databaseName $databaseName"
    & $ssh @commonSshArguments $ApiHost $createCommand | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to create the isolated PostgreSQL database.'
    }
    $databaseCreated = $true

    $tunnelArguments = @(
        $commonSshArguments +
        @(
            '-N',
            '-L', "$($localPort):127.0.0.1:$RemotePort",
            $ApiHost
        )
    )
    $tunnel = Start-Process -FilePath $ssh -ArgumentList $tunnelArguments -PassThru -WindowStyle Hidden

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($tunnel.HasExited) {
            throw 'The PostgreSQL SSH tunnel exited unexpectedly.'
        }
        try {
            $client = [Net.Sockets.TcpClient]::new()
            $client.Connect('127.0.0.1', $localPort)
            $client.Dispose()
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 200
        }
    }
    if (-not $ready) {
        throw 'The PostgreSQL SSH tunnel did not become ready.'
    }

    $env:HECHAO_ECONOMY_TEST_DATABASE =
        "Host=127.0.0.1;Port=$localPort;Database=$databaseName;" +
        "Username=$databaseName;Password=$password;SSL Mode=Disable;" +
        'Pooling=false;Timeout=5;Command Timeout=20'
    $env:DOTNET_ROOT = Split-Path $dotnet -Parent
    & $dotnet test (Join-Path $RepositoryRoot 'tests\Hechao.Api.Tests\Hechao.Api.Tests.csproj') -c Release --no-restore -p:BuildAdminWeb=false --filter 'FullyQualifiedName~EconomyPostgresIntegrationTests'
    if ($LASTEXITCODE -ne 0) {
        throw 'The economy PostgreSQL integration test failed.'
    }
}
finally {
    Remove-Item Env:HECHAO_ECONOMY_TEST_DATABASE -ErrorAction SilentlyContinue
    if ($null -ne $tunnel -and -not $tunnel.HasExited) {
        Stop-Process -Id $tunnel.Id -Force -ErrorAction SilentlyContinue
        $tunnel.WaitForExit(5000) | Out-Null
    }
    if ($databaseCreated) {
        $quote = [char]34
        $dropCommand =
            "docker exec $PostgresContainer dropdb --if-exists --force " +
            "-U $PostgresAdmin $databaseName && " +
            "docker exec $PostgresContainer psql -U $PostgresAdmin -d postgres " +
            "-v ON_ERROR_STOP=1 -c $($quote)DROP ROLE IF EXISTS " +
            "$databaseName;$($quote)"
        & $ssh @commonSshArguments $ApiHost $dropCommand | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error 'Failed to remove the isolated PostgreSQL database or role.'
        }
    }
}
