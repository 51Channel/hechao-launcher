[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Prepare', 'Start', 'Status', 'Stop', 'Remove')]
    [string]$Action,

    [string]$ProductionRoot = 'E:\Velocity',

    [string]$StagingRoot = 'E:\Velocity-PvpReturn-Staging',

    [string]$TaskName = 'Hechao-Velocity-PvpReturn-Staging',

    [string]$JavaExecutable =
        'E:\server-artifacts\java\temurin-jre-25.0.4+7\bin\java.exe',

    [string]$StagingVelocitySource =
        'E:\server-artifacts\velocity\velocity-4.0.0-6.jar',

    [ValidateRange(1, 65535)]
    [int]$ProductionPort = 25577,

    [ValidateRange(1, 65535)]
    [int]$StagingPort = 25579,

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedProductionConfigSha256 =
        'A300E7CBE190B42E434763CFCCAFB9D821F894B02E72A594ED72B340C3E22C70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedVelocitySha256 =
        'CCC49F71751ECE26568D3476392D6130C8B43F2E5F3A88313325B9278A52BABD',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedStagingVelocitySha256 =
        '4540289F48C83E305FC2F2C495A84D1F4D0B7F360830251E169DD5A208740E70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedHubCommandSha256 =
        'D785BB379CA8DCE8CA778D220183D9370C74A6B25EC75276A98228818C7968C2',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedViaVersionSha256 =
        '89DB76C8E3E674238F5EEE2BB7A9E9A2BEEBA0760BBD1B86494778E8A5A52F70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedViaBackwardsSha256 =
        '41085A59D784C9A0D14917FE7487EF5E201A9DA7825FD047F08D328FF33EECDC',

    [ValidateRange(5, 120)]
    [int]$StartupTimeoutSeconds = 45,

    [switch]$ConfirmRemoval
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-SafeRoots {
    $production = Get-NormalizedPath -Path $ProductionRoot
    $staging = Get-NormalizedPath -Path $StagingRoot
    $driveRoot = [IO.Path]::GetPathRoot($staging).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)

    if ([string]::Equals($production, $staging, [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($staging, $driveRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The staging root is unsafe.'
    }

    return [pscustomobject]@{
        Production = $production
        Staging = $staging
    }
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedSha256.ToUpperInvariant()) {
        throw "$Label SHA-256 mismatch. Expected $ExpectedSha256, got $actual."
    }

    return $actual
}

function Get-PortListeners {
    param([Parameter(Mandatory = $true)][int]$Port)

    return @(
        Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Select-Object LocalAddress, LocalPort, OwningProcess
    )
}

function Read-SharedText {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new(
            $stream,
            [Text.UTF8Encoding]::new($false, $true),
            $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Remove-VerifiedTemporaryRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedPrefix
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolved.StartsWith(
            $ExpectedPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The temporary staging path failed its removal boundary check.'
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Assert-ProductionBaseline {
    param([Parameter(Mandatory = $true)][pscustomobject]$Roots)

    if (-not (Test-Path -LiteralPath $Roots.Production -PathType Container)) {
        throw "The production Velocity root is missing: $($Roots.Production)"
    }

    Assert-FileHash `
        -Path (Join-Path $Roots.Production 'velocity.toml') `
        -ExpectedSha256 $ExpectedProductionConfigSha256 `
        -Label 'Production Velocity configuration' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Production 'velocity.jar') `
        -ExpectedSha256 $ExpectedVelocitySha256 `
        -Label 'Production Velocity JAR' | Out-Null

    $productionListeners = @(Get-PortListeners -Port $ProductionPort)
    if ($productionListeners.Count -ne 1) {
        throw "Expected exactly one production listener on port $ProductionPort."
    }

    $enabledViaJars = @(
        Get-ChildItem -LiteralPath (Join-Path $Roots.Production 'plugins') -File |
            Where-Object {
                $_.Extension -eq '.jar' -and
                $_.Name -match '^Via(?:Version|Backwards)'
            }
    )
    if ($enabledViaJars.Count -ne 0) {
        throw 'Production Via JARs are already enabled. Refusing isolated staging work.'
    }

    return [pscustomobject]@{
        ProcessId = $productionListeners[0].OwningProcess
        ConfigSha256 = $ExpectedProductionConfigSha256.ToUpperInvariant()
        EnabledViaJarCount = 0
    }
}

function Assert-StagingFiles {
    param([Parameter(Mandatory = $true)][pscustomobject]$Roots)

    Assert-FileHash `
        -Path (Join-Path $Roots.Staging 'velocity.jar') `
        -ExpectedSha256 $ExpectedStagingVelocitySha256 `
        -Label 'Staging Velocity JAR' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Staging 'plugins\HubCommand-1.0.0.jar') `
        -ExpectedSha256 $ExpectedHubCommandSha256 `
        -Label 'Staging HubCommand JAR' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Staging 'plugins\ViaVersion-5.11.0.jar') `
        -ExpectedSha256 $ExpectedViaVersionSha256 `
        -Label 'Staging ViaVersion JAR' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Staging 'plugins\ViaBackwards-5.11.0.jar') `
        -ExpectedSha256 $ExpectedViaBackwardsSha256 `
        -Label 'Staging ViaBackwards JAR' | Out-Null

    $configPath = Join-Path $Roots.Staging 'velocity.toml'
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw "The staging Velocity configuration is missing: $configPath"
    }

    $configText = [IO.File]::ReadAllText($configPath)
    $expectedBind = 'bind = "127.0.0.1:' + $StagingPort + '"'
    if ($configText -notmatch "(?m)^\s*$([regex]::Escape($expectedBind))\s*$") {
        throw "The staging Velocity bind must be 127.0.0.1:$StagingPort."
    }

    if ($configText -notmatch '(?m)^\s*online-mode\s*=\s*true\s*$' -or
        $configText -notmatch '(?m)^\s*player-info-forwarding-mode\s*=\s*"modern"\s*$' -or
        $configText -notmatch '(?m)^\s*enabled\s*=\s*false\s*$' -or
        $configText -notmatch "(?m)^\s*port\s*=\s*$StagingPort\s*$") {
        throw 'The staging Velocity safety settings are incomplete.'
    }
}

function Get-StagingStatus {
    param(
        [Parameter(Mandatory = $true)][pscustomobject]$Roots,
        [Parameter(Mandatory = $true)][pscustomobject]$Production
    )

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $listeners = @(Get-PortListeners -Port $StagingPort)
    $unexpectedListeners = @(
        $listeners | Where-Object { $_.LocalAddress -ne '127.0.0.1' }
    )
    $latestLog = Join-Path $Roots.Staging 'logs\latest.log'
    $fatalMatchCount = 0
    if (Test-Path -LiteralPath $latestLog -PathType Leaf) {
        $fatalMatchCount = [regex]::Matches(
            (Read-SharedText -Path $latestLog),
            'ERROR|FATAL|Exception',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
    }

    return [pscustomobject]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        StagingRoot = $Roots.Staging
        TaskName = $TaskName
        TaskState = if ($null -eq $task) { 'Missing' } else { [string]$task.State }
        ListenerCount = $listeners.Count
        ListenerAddresses = @($listeners | ForEach-Object { $_.LocalAddress })
        ProcessIds = @($listeners | ForEach-Object { $_.OwningProcess })
        LoopbackOnly = $unexpectedListeners.Count -eq 0
        FatalOrErrorLogMatches = $fatalMatchCount
        StagingVelocitySha256 = $ExpectedStagingVelocitySha256.ToUpperInvariant()
        ProductionProcessId = $Production.ProcessId
        ProductionConfigSha256 = $Production.ConfigSha256
        ProductionVelocitySha256 = $ExpectedVelocitySha256.ToUpperInvariant()
        ProductionEnabledViaJarCount = $Production.EnabledViaJarCount
    }
}

function New-StagingConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $text = [IO.File]::ReadAllText($Source)
    $bindPattern = '(?m)^bind\s*=\s*"[^"]+"\s*$'
    $motdPattern = '(?m)^motd\s*=\s*".*"\s*$'
    $queryPortPattern = "(?m)^port\s*=\s*$ProductionPort\s*$"
    if ([regex]::Matches($text, $bindPattern).Count -ne 1 -or
        [regex]::Matches($text, $motdPattern).Count -ne 1 -or
        [regex]::Matches($text, $queryPortPattern).Count -ne 1) {
        throw 'The production Velocity configuration does not have one bind, MOTD and query port.'
    }

    $text = [regex]::Replace(
        $text,
        $bindPattern,
        'bind = "127.0.0.1:' + $StagingPort + '"')
    $text = [regex]::Replace(
        $text,
        $motdPattern,
        'motd = "<red><bold>HECHAO PVP RETURN ISOLATION</bold></red>"')
    $text = [regex]::Replace(
        $text,
        $queryPortPattern,
        "port = $StagingPort")

    [IO.File]::WriteAllText(
        $Destination,
        $text,
        [Text.UTF8Encoding]::new($false))
}

function Prepare-Staging {
    param([Parameter(Mandatory = $true)][pscustomobject]$Roots)

    if (Test-Path -LiteralPath $Roots.Staging) {
        throw "The staging root already exists: $($Roots.Staging)"
    }
    if (@(Get-PortListeners -Port $StagingPort).Count -ne 0) {
        throw "Port $StagingPort is already listening."
    }
    if (-not (Test-Path -LiteralPath $JavaExecutable -PathType Leaf)) {
        throw "The Java executable is missing: $JavaExecutable"
    }
    Assert-FileHash `
        -Path $StagingVelocitySource `
        -ExpectedSha256 $ExpectedStagingVelocitySha256 `
        -Label 'Staging Velocity source JAR' | Out-Null
    if ($null -ne (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        throw "The staging task already exists: $TaskName"
    }

    $temporaryPrefix = "$($Roots.Staging).prepare-"
    $temporaryRoot = "$temporaryPrefix$([Guid]::NewGuid().ToString('N'))"
    $createdStagingRoot = $false
    try {
        [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $temporaryRoot 'plugins')) | Out-Null

        Copy-Item `
            -LiteralPath $StagingVelocitySource `
            -Destination (Join-Path $temporaryRoot 'velocity.jar')
        Copy-Item `
            -LiteralPath (Join-Path $Roots.Production 'forwarding.secret') `
            -Destination (Join-Path $temporaryRoot 'forwarding.secret')
        Copy-Item `
            -LiteralPath (Join-Path $Roots.Production 'server-icon.png') `
            -Destination (Join-Path $temporaryRoot 'server-icon.png')
        Copy-Item `
            -LiteralPath (Join-Path $Roots.Production 'plugins\HubCommand-1.0.0.jar') `
            -Destination (Join-Path $temporaryRoot 'plugins\HubCommand-1.0.0.jar')
        Copy-Item `
            -LiteralPath (Join-Path $Roots.Production 'plugins\ViaVersion-5.11.0.jar.disabled') `
            -Destination (Join-Path $temporaryRoot 'plugins\ViaVersion-5.11.0.jar')
        Copy-Item `
            -LiteralPath (Join-Path $Roots.Production 'plugins\ViaBackwards-5.11.0.jar.disabled') `
            -Destination (Join-Path $temporaryRoot 'plugins\ViaBackwards-5.11.0.jar')
        New-StagingConfiguration `
            -Source (Join-Path $Roots.Production 'velocity.toml') `
            -Destination (Join-Path $temporaryRoot 'velocity.toml')

        Set-Acl `
            -LiteralPath (Join-Path $temporaryRoot 'forwarding.secret') `
            -AclObject (Get-Acl -LiteralPath (Join-Path $Roots.Production 'forwarding.secret'))

        Move-Item -LiteralPath $temporaryRoot -Destination $Roots.Staging
        $temporaryRoot = $null
        $createdStagingRoot = $true

        Assert-StagingFiles -Roots $Roots

        $taskAction = New-ScheduledTaskAction `
            -Execute $JavaExecutable `
            -Argument '-Xms256M -Xmx512M -XX:+UseG1GC -XX:+ParallelRefProcEnabled -jar velocity.jar' `
            -WorkingDirectory $Roots.Staging
        $principal = New-ScheduledTaskPrincipal `
            -UserId 'SYSTEM' `
            -LogonType ServiceAccount `
            -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -MultipleInstances IgnoreNew
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $taskAction `
            -Principal $principal `
            -Settings $settings `
            -Description 'Loopback-only PVP return-route protocol staging. No automatic trigger.' |
            Out-Null
    }
    catch {
        if ($null -ne $temporaryRoot) {
            Remove-VerifiedTemporaryRoot `
                -Path $temporaryRoot `
                -ExpectedPrefix $temporaryPrefix
        }
        if ($createdStagingRoot -and (Test-Path -LiteralPath $Roots.Staging)) {
            $resolvedStaging = (Resolve-Path -LiteralPath $Roots.Staging).Path
            if ([string]::Equals(
                    $resolvedStaging,
                    $Roots.Staging,
                    [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
            }
        }

        throw
    }
}

function Start-Staging {
    param([Parameter(Mandatory = $true)][pscustomobject]$Roots)

    Assert-StagingFiles -Roots $Roots
    if (@(Get-PortListeners -Port $StagingPort).Count -ne 0) {
        throw "Port $StagingPort is already listening."
    }
    if ($null -eq (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        throw "The staging task is missing: $TaskName"
    }

    Start-ScheduledTask -TaskName $TaskName
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $listeners = @(Get-PortListeners -Port $StagingPort)
    } while ($listeners.Count -eq 0 -and [DateTimeOffset]::UtcNow -lt $deadline)

    if ($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1') {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        throw "The staging proxy did not bind only to 127.0.0.1:$StagingPort."
    }

        $latestLog = Join-Path $Roots.Staging 'logs\latest.log'
    do {
        Start-Sleep -Milliseconds 500
        $logText = if (Test-Path -LiteralPath $latestLog) {
            Read-SharedText -Path $latestLog
        }
        else {
            ''
        }
        $ready = $logText -match 'Done \(' -and
            $logText -match 'ViaVersion' -and
            $logText -match 'ViaBackwards'
    } while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline)

    if (-not $ready -or $logText -match '(?i)ERROR|FATAL|Exception') {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        throw 'The staging proxy did not pass its startup log checks.'
    }
}

function Stop-Staging {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task -and $task.State -ne 'Ready') {
        Stop-ScheduledTask -TaskName $TaskName
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $listeners = @(Get-PortListeners -Port $StagingPort)
    } while ($listeners.Count -ne 0 -and [DateTimeOffset]::UtcNow -lt $deadline)

    if ($listeners.Count -ne 0) {
        throw "The staging listener on port $StagingPort did not stop."
    }
}

$roots = Assert-SafeRoots
$production = Assert-ProductionBaseline -Roots $roots

switch ($Action) {
    'Prepare' {
        if ($PSCmdlet.ShouldProcess($roots.Staging, 'Prepare isolated Velocity staging')) {
            Prepare-Staging -Roots $roots
        }
    }
    'Start' {
        if ($PSCmdlet.ShouldProcess($TaskName, 'Start isolated Velocity staging')) {
            Start-Staging -Roots $roots
        }
    }
    'Stop' {
        if ($PSCmdlet.ShouldProcess($TaskName, 'Stop isolated Velocity staging')) {
            Stop-Staging
        }
    }
    'Remove' {
        if (-not $ConfirmRemoval) {
            throw 'Remove requires -ConfirmRemoval.'
        }
        if ($PSCmdlet.ShouldProcess($roots.Staging, 'Stop and remove isolated Velocity staging')) {
            Stop-Staging
            if ($null -ne (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
                Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            }
            if (Test-Path -LiteralPath $roots.Staging) {
                $resolvedStaging = (Resolve-Path -LiteralPath $roots.Staging).Path
                if (-not [string]::Equals(
                        $resolvedStaging,
                        $roots.Staging,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'The resolved staging root changed before removal.'
                }
                Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
            }
        }
    }
    'Status' {
        Assert-StagingFiles -Roots $roots
    }
}

Get-StagingStatus -Roots $roots -Production $production | ConvertTo-Json -Depth 4
