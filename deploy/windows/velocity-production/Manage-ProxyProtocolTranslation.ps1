[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Status', 'Migrate', 'Rollback')]
    [string]$Action = 'Status',

    [switch]$ConfirmMigration,

    [switch]$ConfirmRollback,

    [string]$BackupDirectory,

    [string]$VelocityRoot = 'E:\Velocity',

    [string]$LobbyRoot = 'E:\LobbyServer',

    [string]$BackupRoot = 'E:\manual-backups',

    [string]$VelocityTaskName = 'Codex-Velocity-Live',

    [string]$LobbyTaskName = 'Hechao-Server-Lobby',

    [string]$ConsoleBridge =
        'C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1',

    [string]$Velocity4Source =
        'E:\server-artifacts\velocity\velocity-4.0.0-6.jar',

    [string]$Java25Executable =
        'E:\server-artifacts\java\temurin-jre-25.0.4+7\bin\java.exe',

    [string]$Authorizer031Source =
        'E:\Velocity-PvpReturn-Staging\plugins\HechaoVelocityAuthorizer-0.3.1.jar',

    [ValidateRange(1, 65535)]
    [int]$VelocityPort = 25577,

    [ValidateRange(1, 65535)]
    [int]$LobbyPort = 25566,

    [ValidateRange(15, 180)]
    [int]$ShutdownTimeoutSeconds = 60,

    [ValidateRange(30, 300)]
    [int]$StartupTimeoutSeconds = 150,

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedVelocityTomlSha256 =
        'A300E7CBE190B42E434763CFCCAFB9D821F894B02E72A594ED72B340C3E22C70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedVelocityLegacySha256 =
        'CCC49F71751ECE26568D3476392D6130C8B43F2E5F3A88313325B9278A52BABD',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedVelocity4Sha256 =
        '4540289F48C83E305FC2F2C495A84D1F4D0B7F360830251E169DD5A208740E70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedJava25Sha256 =
        '1DFE0B08636BC74B56DB5E246F038CFE67C18F567053373FA601D310F29ED9DA',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedAuthorizer030Sha256 =
        '289B13472AEAC4073895EF9BE7E630B4B5AACEC48A4D0FD849BBAFE0064E681D',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedAuthorizer031Sha256 =
        '2FC06C2DBE6F01AFAC2C5AA016C902A10B4B1675C876C5850630B726BB041E75',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedViaVersionSha256 =
        '89DB76C8E3E674238F5EEE2BB7A9E9A2BEEBA0760BBD1B86494778E8A5A52F70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedViaBackwardsSha256 =
        '41085A59D784C9A0D14917FE7487EF5E201A9DA7825FD047F08D328FF33EECDC'
)

$ErrorActionPreference = 'Stop'
$script:MigrationPrefix = 'velocity-proxy-protocol-translation-'
$script:VelocityArguments = $null
$script:RollbackAttempted = $false

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must run from an elevated Administrator session.'
    }
}

function Assert-SafeRoots {
    $velocity = Get-NormalizedPath -Path $VelocityRoot
    $lobby = Get-NormalizedPath -Path $LobbyRoot
    $backup = Get-NormalizedPath -Path $BackupRoot

    foreach ($path in @($velocity, $lobby, $backup)) {
        $driveRoot = [IO.Path]::GetPathRoot($path).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        if ([string]::Equals(
                $path,
                $driveRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "A server or backup root cannot be a drive root: $path"
        }
    }

    if ([string]::Equals(
            $velocity,
            $lobby,
            [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $velocity,
            $backup,
            [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $lobby,
            $backup,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Velocity, Lobby and backup roots must be distinct.'
    }

    return [pscustomobject]@{
        Velocity = $velocity
        Lobby = $lobby
        Backup = $backup
    }
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedSha256,
        [Parameter(Mandatory)][string]$Label
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

function Read-SharedText {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new(
            $stream,
            [Text.UTF8Encoding]::new($false, $false),
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

function Get-NewLogText {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$PreviousLength
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ''
    }

    $text = Read-SharedText -Path $Path
    if ($PreviousLength -gt 0 -and $text.Length -ge $PreviousLength) {
        return $text.Substring([int]$PreviousLength)
    }

    return $text
}

function Get-Listener {
    param([Parameter(Mandatory)][int]$Port)

    return @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Listen `
            -ErrorAction SilentlyContinue |
            Select-Object LocalAddress, LocalPort, OwningProcess
    )
}

function Get-EstablishedClientCount {
    return @(
        Get-NetTCPConnection `
            -LocalPort $VelocityPort `
            -State Established `
            -ErrorAction SilentlyContinue
    ).Count
}

function Wait-ListenerState {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][bool]$Present,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$Label
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $listeners = @(Get-Listener -Port $Port)
        $matches = if ($Present) {
            $listeners.Count -eq 1
        }
        else {
            $listeners.Count -eq 0
        }

        if ($matches) {
            return $listeners
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $expected = if ($Present) { 'appear' } else { 'close' }
    throw "$Label listener on port $Port did not $expected within $TimeoutSeconds seconds."
}

function Wait-ProcessAndTaskStopped {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$Label
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $processExists = $null -ne (
            Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        )
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $taskStopped = $task.State -ne 'Running'
        if (-not $processExists -and $taskStopped) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "$Label process or scheduled task did not fully stop within $TimeoutSeconds seconds."
}

function Get-TaskAction {
    param([Parameter(Mandatory)][string]$TaskName)

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    $actions = @($task.Actions)
    if ($actions.Count -ne 1) {
        throw "Task $TaskName must contain exactly one action."
    }

    return [pscustomobject]@{
        Task = $task
        Execute = [string]$actions[0].Execute
        Arguments = [string]$actions[0].Arguments
        WorkingDirectory = [string]$actions[0].WorkingDirectory
    }
}

function Assert-AuthorizerConfiguration {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    $configurationPath = Join-Path `
        $Roots.Velocity `
        'plugins\hechao-velocity-authorizer\config.properties'
    if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
        throw "The production Authorizer configuration is missing: $configurationPath"
    }

    $text = [IO.File]::ReadAllText($configurationPath)
    foreach ($pattern in @(
            '(?m)^mode=monitor\s*$',
            '(?m)^api-url=https://launcher-api\.hechao\.world/',
            '(?m)^proxy-instance=owl5-main\s*$',
            '(?m)^token=[A-Za-z0-9_-]{24,256}\s*$'
        )) {
        if ($text -notmatch $pattern) {
            throw 'The production Authorizer configuration failed its safety checks.'
        }
    }

    return $configurationPath
}

function Get-ViaPaths {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    return [pscustomobject]@{
        VelocityViaVersionEnabled = Join-Path `
            $Roots.Velocity `
            'plugins\ViaVersion-5.11.0.jar'
        VelocityViaVersionDisabled = Join-Path `
            $Roots.Velocity `
            'plugins\ViaVersion-5.11.0.jar.disabled'
        VelocityViaBackwardsEnabled = Join-Path `
            $Roots.Velocity `
            'plugins\ViaBackwards-5.11.0.jar'
        VelocityViaBackwardsDisabled = Join-Path `
            $Roots.Velocity `
            'plugins\ViaBackwards-5.11.0.jar.disabled'
        LobbyViaVersionEnabled = Join-Path `
            $Roots.Lobby `
            'plugins\ViaVersion-5.11.0.jar'
        LobbyViaVersionDisabled = Join-Path `
            $Roots.Lobby `
            'plugins\ViaVersion-5.11.0.jar.disabled'
        LobbyViaBackwardsEnabled = Join-Path `
            $Roots.Lobby `
            'plugins\ViaBackwards-5.11.0.jar'
        LobbyViaBackwardsDisabled = Join-Path `
            $Roots.Lobby `
            'plugins\ViaBackwards-5.11.0.jar.disabled'
    }
}

function Assert-NoUnexpectedViaJars {
    param(
        [Parameter(Mandatory)][string]$PluginsDirectory,
        [Parameter(Mandatory)][string[]]$ExpectedNames,
        [Parameter(Mandatory)][string]$Label
    )

    $actual = @(
        Get-ChildItem -LiteralPath $PluginsDirectory -File |
            Where-Object {
                $_.Name -match '^Via(?:Version|Backwards)-.*\.jar(?:\.disabled)?$'
            } |
            Sort-Object Name |
            Select-Object -ExpandProperty Name
    )
    $expected = @($ExpectedNames | Sort-Object)
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        throw "$Label Via JAR inventory is not the reviewed two-file set."
    }
}

function Assert-LegacyBaseline {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [switch]$RequireRunning
    )

    Assert-FileHash `
        -Path (Join-Path $Roots.Velocity 'velocity.toml') `
        -ExpectedSha256 $ExpectedVelocityTomlSha256 `
        -Label 'Velocity configuration' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Velocity 'velocity.jar') `
        -ExpectedSha256 $ExpectedVelocityLegacySha256 `
        -Label 'Legacy Velocity JAR' | Out-Null
    Assert-FileHash `
        -Path (
            Join-Path `
                $Roots.Velocity `
                'plugins\HechaoVelocityAuthorizer-0.3.0.jar'
        ) `
        -ExpectedSha256 $ExpectedAuthorizer030Sha256 `
        -Label 'Authorizer 0.3.0 JAR' | Out-Null
    Assert-AuthorizerConfiguration -Roots $Roots | Out-Null

    $via = Get-ViaPaths -Roots $Roots
    Assert-FileHash `
        -Path $via.VelocityViaVersionDisabled `
        -ExpectedSha256 $ExpectedViaVersionSha256 `
        -Label 'Disabled proxy ViaVersion JAR' | Out-Null
    Assert-FileHash `
        -Path $via.VelocityViaBackwardsDisabled `
        -ExpectedSha256 $ExpectedViaBackwardsSha256 `
        -Label 'Disabled proxy ViaBackwards JAR' | Out-Null
    Assert-FileHash `
        -Path $via.LobbyViaVersionEnabled `
        -ExpectedSha256 $ExpectedViaVersionSha256 `
        -Label 'Enabled Lobby ViaVersion JAR' | Out-Null
    Assert-FileHash `
        -Path $via.LobbyViaBackwardsEnabled `
        -ExpectedSha256 $ExpectedViaBackwardsSha256 `
        -Label 'Enabled Lobby ViaBackwards JAR' | Out-Null

    Assert-NoUnexpectedViaJars `
        -PluginsDirectory (Join-Path $Roots.Velocity 'plugins') `
        -ExpectedNames @(
            'ViaBackwards-5.11.0.jar.disabled',
            'ViaVersion-5.11.0.jar.disabled'
        ) `
        -Label 'Velocity'
    Assert-NoUnexpectedViaJars `
        -PluginsDirectory (Join-Path $Roots.Lobby 'plugins') `
        -ExpectedNames @(
            'ViaBackwards-5.11.0.jar',
            'ViaVersion-5.11.0.jar'
        ) `
        -Label 'Lobby'

    $authorizerJars = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $Roots.Velocity 'plugins') `
            -File |
            Where-Object {
                $_.Name -match '^HechaoVelocityAuthorizer-.*\.jar$'
            }
    )
    if ($authorizerJars.Count -ne 1) {
        throw 'Velocity must contain exactly one enabled Authorizer JAR.'
    }

    $velocityTask = Get-TaskAction -TaskName $VelocityTaskName
    if ($velocityTask.Execute -ine 'E:\jdk\bin\java.exe' -or
        $velocityTask.WorkingDirectory -ine $Roots.Velocity -or
        $velocityTask.Arguments -notmatch '(?i)(?:^|\s)-jar\s+velocity\.jar(?:\s|$)') {
        throw 'The legacy Velocity task action is not the reviewed production action.'
    }
    $script:VelocityArguments = $velocityTask.Arguments

    if ($RequireRunning) {
        if (@(Get-Listener -Port $VelocityPort).Count -ne 1) {
            throw "Expected one production Velocity listener on port $VelocityPort."
        }
        if (@(Get-Listener -Port $LobbyPort).Count -ne 1) {
            throw "Expected one production Lobby listener on port $LobbyPort."
        }
    }

    return [pscustomobject]@{
        State = 'legacy-backend-translation'
        VelocityJava = $velocityTask.Execute
        VelocityArguments = $velocityTask.Arguments
        VelocityTaskState = [string]$velocityTask.Task.State
        VelocityClientConnections = Get-EstablishedClientCount
    }
}

function Assert-ProxyBaseline {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [switch]$RequireRunning
    )

    Assert-FileHash `
        -Path (Join-Path $Roots.Velocity 'velocity.toml') `
        -ExpectedSha256 $ExpectedVelocityTomlSha256 `
        -Label 'Velocity configuration' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Roots.Velocity 'velocity.jar') `
        -ExpectedSha256 $ExpectedVelocity4Sha256 `
        -Label 'Velocity 4 JAR' | Out-Null
    Assert-FileHash `
        -Path (
            Join-Path `
                $Roots.Velocity `
                'plugins\HechaoVelocityAuthorizer-0.3.1.jar'
        ) `
        -ExpectedSha256 $ExpectedAuthorizer031Sha256 `
        -Label 'Authorizer 0.3.1 JAR' | Out-Null
    Assert-AuthorizerConfiguration -Roots $Roots | Out-Null

    $via = Get-ViaPaths -Roots $Roots
    Assert-FileHash `
        -Path $via.VelocityViaVersionEnabled `
        -ExpectedSha256 $ExpectedViaVersionSha256 `
        -Label 'Enabled proxy ViaVersion JAR' | Out-Null
    Assert-FileHash `
        -Path $via.VelocityViaBackwardsEnabled `
        -ExpectedSha256 $ExpectedViaBackwardsSha256 `
        -Label 'Enabled proxy ViaBackwards JAR' | Out-Null
    Assert-FileHash `
        -Path $via.LobbyViaVersionDisabled `
        -ExpectedSha256 $ExpectedViaVersionSha256 `
        -Label 'Disabled Lobby ViaVersion JAR' | Out-Null
    Assert-FileHash `
        -Path $via.LobbyViaBackwardsDisabled `
        -ExpectedSha256 $ExpectedViaBackwardsSha256 `
        -Label 'Disabled Lobby ViaBackwards JAR' | Out-Null

    Assert-NoUnexpectedViaJars `
        -PluginsDirectory (Join-Path $Roots.Velocity 'plugins') `
        -ExpectedNames @(
            'ViaBackwards-5.11.0.jar',
            'ViaVersion-5.11.0.jar'
        ) `
        -Label 'Velocity'
    Assert-NoUnexpectedViaJars `
        -PluginsDirectory (Join-Path $Roots.Lobby 'plugins') `
        -ExpectedNames @(
            'ViaBackwards-5.11.0.jar.disabled',
            'ViaVersion-5.11.0.jar.disabled'
        ) `
        -Label 'Lobby'

    $authorizerJars = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $Roots.Velocity 'plugins') `
            -File |
            Where-Object {
                $_.Name -match '^HechaoVelocityAuthorizer-.*\.jar$'
            }
    )
    if ($authorizerJars.Count -ne 1) {
        throw 'Velocity must contain exactly one enabled Authorizer JAR.'
    }

    Assert-FileHash `
        -Path $Java25Executable `
        -ExpectedSha256 $ExpectedJava25Sha256 `
        -Label 'Java 25 executable' | Out-Null
    $velocityTask = Get-TaskAction -TaskName $VelocityTaskName
    if ($velocityTask.Execute -ine (Get-NormalizedPath -Path $Java25Executable) -or
        $velocityTask.WorkingDirectory -ine $Roots.Velocity -or
        $velocityTask.Arguments -notmatch '(?i)(?:^|\s)-jar\s+velocity\.jar(?:\s|$)') {
        throw 'The Velocity task is not configured for the pinned Java 25 runtime.'
    }
    $script:VelocityArguments = $velocityTask.Arguments

    if ($RequireRunning) {
        if (@(Get-Listener -Port $VelocityPort).Count -ne 1) {
            throw "Expected one production Velocity listener on port $VelocityPort."
        }
        if (@(Get-Listener -Port $LobbyPort).Count -ne 1) {
            throw "Expected one production Lobby listener on port $LobbyPort."
        }
    }

    return [pscustomobject]@{
        State = 'proxy-only-translation'
        VelocityJava = $velocityTask.Execute
        VelocityArguments = $velocityTask.Arguments
        VelocityTaskState = [string]$velocityTask.Task.State
        VelocityClientConnections = Get-EstablishedClientCount
    }
}

function Get-DeploymentStatus {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    $detectedState = 'unknown'
    $baseline = $null
    $legacyError = $null
    $proxyError = $null
    try {
        $baseline = Assert-LegacyBaseline -Roots $Roots
        $detectedState = $baseline.State
    }
    catch {
        $legacyError = $_.Exception.Message
    }

    if ($detectedState -eq 'unknown') {
        try {
            $baseline = Assert-ProxyBaseline -Roots $Roots
            $detectedState = $baseline.State
        }
        catch {
            $proxyError = $_.Exception.Message
        }
    }

    $velocityTask = Get-ScheduledTask `
        -TaskName $VelocityTaskName `
        -ErrorAction SilentlyContinue
    $lobbyTask = Get-ScheduledTask `
        -TaskName $LobbyTaskName `
        -ErrorAction SilentlyContinue

    return [ordered]@{
        schemaVersion = 1
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        action = 'status'
        state = $detectedState
        safeForMigration = (
            $detectedState -eq 'legacy-backend-translation' -and
            (Get-EstablishedClientCount) -eq 0 -and
            @(Get-Listener -Port $VelocityPort).Count -eq 1 -and
            @(Get-Listener -Port $LobbyPort).Count -eq 1
        )
        velocity = [ordered]@{
            task = $VelocityTaskName
            taskState = if ($null -eq $velocityTask) {
                'Missing'
            }
            else {
                [string]$velocityTask.State
            }
            listenerCount = @(Get-Listener -Port $VelocityPort).Count
            establishedClientConnections = Get-EstablishedClientCount
        }
        lobby = [ordered]@{
            task = $LobbyTaskName
            taskState = if ($null -eq $lobbyTask) {
                'Missing'
            }
            else {
                [string]$lobbyTask.State
            }
            listenerCount = @(Get-Listener -Port $LobbyPort).Count
        }
        checks = [ordered]@{
            legacyBaselineError = $legacyError
            proxyBaselineError = $proxyError
        }
    }
}

function Set-RestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)

    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidValue in @(
            'S-1-5-18',
            'S-1-5-32-544'
        )) {
        $sid = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            (
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [Security.AccessControl.InheritanceFlags]::ObjectInherit
            ),
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Copy-BackupFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "A required backup source is missing: $Source"
    }

    $parent = Split-Path -Parent $Destination
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination

    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $destinationHash = (
        Get-FileHash -LiteralPath $Destination -Algorithm SHA256
    ).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "Backup verification failed for $Source."
    }
}

function Copy-SharedSnapshotFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "A required snapshot source is missing: $Source"
    }

    $parent = Split-Path -Parent $Destination
    [IO.Directory]::CreateDirectory($parent) | Out-Null

    $sourceStream = [IO.FileStream]::new(
        $Source,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $snapshotLength = $sourceStream.Length
        $destinationStream = [IO.FileStream]::new(
            $Destination,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try {
            $buffer = [byte[]]::new(1MB)
            $remaining = $snapshotLength
            while ($remaining -gt 0) {
                $requested = [int][Math]::Min($buffer.Length, $remaining)
                $read = $sourceStream.Read($buffer, 0, $requested)
                if ($read -le 0) {
                    throw "The live snapshot source was truncated: $Source"
                }
                $destinationStream.Write($buffer, 0, $read)
                $remaining -= $read
            }
            $destinationStream.Flush($true)
        }
        finally {
            $destinationStream.Dispose()
        }
    }
    finally {
        $sourceStream.Dispose()
    }

    $destinationLength = (Get-Item -LiteralPath $Destination).Length
    if ($destinationLength -ne $snapshotLength) {
        throw "Live snapshot length verification failed for $Source."
    }

    return [pscustomobject]@{
        Length = $destinationLength
        Sha256 = (
            Get-FileHash -LiteralPath $Destination -Algorithm SHA256
        ).Hash
    }
}

function New-MigrationBackup {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [Parameter(Mandatory)][pscustomobject]$Baseline
    )

    [IO.Directory]::CreateDirectory($Roots.Backup) | Out-Null
    $timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $path = Join-Path $Roots.Backup "$($script:MigrationPrefix)$timestamp"
    if (Test-Path -LiteralPath $path) {
        throw "The migration backup already exists: $path"
    }

    [IO.Directory]::CreateDirectory($path) | Out-Null
    Set-RestrictedDirectoryAcl -Path $path

    try {
        $files = [ordered]@{
        VelocityJar = @(
            (Join-Path $Roots.Velocity 'velocity.jar'),
            (Join-Path $path 'velocity\velocity.jar')
        )
        VelocityToml = @(
            (Join-Path $Roots.Velocity 'velocity.toml'),
            (Join-Path $path 'velocity\velocity.toml')
        )
        AuthorizerJar = @(
            (
                Join-Path `
                    $Roots.Velocity `
                    'plugins\HechaoVelocityAuthorizer-0.3.0.jar'
            ),
            (
                Join-Path `
                    $path `
                    'velocity\plugins\HechaoVelocityAuthorizer-0.3.0.jar'
            )
        )
        VelocityViaVersion = @(
            (
                Join-Path `
                    $Roots.Velocity `
                    'plugins\ViaVersion-5.11.0.jar.disabled'
            ),
            (
                Join-Path `
                    $path `
                    'velocity\plugins\ViaVersion-5.11.0.jar.disabled'
            )
        )
        VelocityViaBackwards = @(
            (
                Join-Path `
                    $Roots.Velocity `
                    'plugins\ViaBackwards-5.11.0.jar.disabled'
            ),
            (
                Join-Path `
                    $path `
                    'velocity\plugins\ViaBackwards-5.11.0.jar.disabled'
            )
        )
        LobbyViaVersion = @(
            (
                Join-Path `
                    $Roots.Lobby `
                    'plugins\ViaVersion-5.11.0.jar'
            ),
            (
                Join-Path `
                    $path `
                    'lobby\plugins\ViaVersion-5.11.0.jar'
            )
        )
        LobbyViaBackwards = @(
            (
                Join-Path `
                    $Roots.Lobby `
                    'plugins\ViaBackwards-5.11.0.jar'
            ),
            (
                Join-Path `
                    $path `
                    'lobby\plugins\ViaBackwards-5.11.0.jar'
            )
        )
    }
        foreach ($entry in $files.GetEnumerator()) {
            Copy-BackupFile `
                -Source $entry.Value[0] `
                -Destination $entry.Value[1]
        }

    $authorizerConfig = Join-Path `
        $Roots.Velocity `
        'plugins\hechao-velocity-authorizer'
    if (Test-Path -LiteralPath $authorizerConfig -PathType Container) {
        $configDestination = Join-Path `
            $path `
            'velocity\plugins\hechao-velocity-authorizer'
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $configDestination)
        ) | Out-Null
        Copy-Item `
            -LiteralPath $authorizerConfig `
            -Destination $configDestination `
            -Recurse
    }

        foreach ($log in @(
            @(
                (Join-Path $Roots.Velocity 'logs\latest.log'),
                (Join-Path $path 'logs\velocity-before.log')
            ),
            @(
                (Join-Path $Roots.Lobby 'logs\latest.log'),
                (Join-Path $path 'logs\lobby-before.log')
            )
        )) {
            if (Test-Path -LiteralPath $log[0] -PathType Leaf) {
                Copy-SharedSnapshotFile `
                    -Source $log[0] `
                    -Destination $log[1] | Out-Null
            }
        }

        $taskXmlPath = Join-Path $path 'tasks\velocity.xml'
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $taskXmlPath)
        ) | Out-Null
        [IO.File]::WriteAllText(
            $taskXmlPath,
            (Export-ScheduledTask -TaskName $VelocityTaskName),
            [Text.UTF8Encoding]::new($false))

    $prechange = [ordered]@{
        schemaVersion = 1
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        state = $Baseline.State
        velocityTask = $VelocityTaskName
        lobbyTask = $LobbyTaskName
        velocityTaskXmlSha256 = (
            Get-FileHash -LiteralPath $taskXmlPath -Algorithm SHA256
        ).Hash
        velocityClientConnections = $Baseline.VelocityClientConnections
        files = [ordered]@{
            velocityJarSha256 = $ExpectedVelocityLegacySha256.ToUpperInvariant()
            velocityTomlSha256 = $ExpectedVelocityTomlSha256.ToUpperInvariant()
            authorizerSha256 = $ExpectedAuthorizer030Sha256.ToUpperInvariant()
            viaVersionSha256 = $ExpectedViaVersionSha256.ToUpperInvariant()
            viaBackwardsSha256 = $ExpectedViaBackwardsSha256.ToUpperInvariant()
        }
    }
        [IO.File]::WriteAllText(
            (Join-Path $path 'prechange.json'),
            ($prechange | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))

        return $path
    }
    catch {
        $expectedPrefix = $Roots.Backup +
            [IO.Path]::DirectorySeparatorChar +
            $script:MigrationPrefix
        if ((Test-Path -LiteralPath $path -PathType Container) -and
            $path.StartsWith(
                $expectedPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
        throw
    }
}

function Invoke-LobbyConsoleCommand {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [Parameter(Mandatory)][string]$Command
    )

    if (-not (Test-Path -LiteralPath $ConsoleBridge -PathType Leaf)) {
        throw "The console bridge is missing: $ConsoleBridge"
    }

    & $ConsoleBridge `
        -ProcessId $ProcessId `
        -Command $Command `
        -TimeoutSeconds 30 | Out-Null
}

function Stop-LobbyGracefully {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    $listeners = @(Get-Listener -Port $LobbyPort)
    if ($listeners.Count -eq 0) {
        return
    }
    if ($listeners.Count -ne 1) {
        throw "Expected one Lobby listener on port $LobbyPort."
    }

    $latestLog = Join-Path $Roots.Lobby 'logs\latest.log'
    $previousLength = if (Test-Path -LiteralPath $latestLog -PathType Leaf) {
        (Read-SharedText -Path $latestLog).Length
    }
    else {
        0
    }

    Invoke-LobbyConsoleCommand `
        -ProcessId $listeners[0].OwningProcess `
        -Command 'save-all flush'

    $saveDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    $saved = $false
    do {
        Start-Sleep -Milliseconds 500
        $newLog = Get-NewLogText `
            -Path $latestLog `
            -PreviousLength $previousLength
        $saved = $newLog -match '(?i)Saved the game|Saved all worlds'
    } while (-not $saved -and [DateTimeOffset]::UtcNow -lt $saveDeadline)

    if (-not $saved) {
        throw 'Lobby did not confirm save-all flush in its new log output.'
    }

    Invoke-LobbyConsoleCommand `
        -ProcessId $listeners[0].OwningProcess `
        -Command 'stop'
    $stoppingProcessId = [int]$listeners[0].OwningProcess
    Wait-ListenerState `
        -Port $LobbyPort `
        -Present $false `
        -TimeoutSeconds $ShutdownTimeoutSeconds `
        -Label 'Lobby' | Out-Null
    Wait-ProcessAndTaskStopped `
        -ProcessId $stoppingProcessId `
        -TaskName $LobbyTaskName `
        -TimeoutSeconds $ShutdownTimeoutSeconds `
        -Label 'Lobby'
}

function Stop-VelocityTask {
    $listeners = @(Get-Listener -Port $VelocityPort)
    if ($listeners.Count -eq 0) {
        return
    }
    if ($listeners.Count -ne 1) {
        throw "Expected one Velocity listener on port $VelocityPort."
    }

    $stoppingProcessId = [int]$listeners[0].OwningProcess
    Stop-ScheduledTask -TaskName $VelocityTaskName
    Wait-ListenerState `
        -Port $VelocityPort `
        -Present $false `
        -TimeoutSeconds $ShutdownTimeoutSeconds `
        -Label 'Velocity' | Out-Null
    Wait-ProcessAndTaskStopped `
        -ProcessId $stoppingProcessId `
        -TaskName $VelocityTaskName `
        -TimeoutSeconds $ShutdownTimeoutSeconds `
        -Label 'Velocity'
}

function Start-LobbyAndValidate {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [Parameter(Mandatory)][bool]$ExpectVia
    )

    $latestLog = Join-Path $Roots.Lobby 'logs\latest.log'
    $previousLength = if (Test-Path -LiteralPath $latestLog -PathType Leaf) {
        (Read-SharedText -Path $latestLog).Length
    }
    else {
        0
    }

    $taskBeforeStart = Get-ScheduledTask `
        -TaskName $LobbyTaskName `
        -ErrorAction Stop
    if ($taskBeforeStart.State -eq 'Running') {
        throw 'Lobby scheduled task is still running before startup.'
    }
    Start-ScheduledTask -TaskName $LobbyTaskName
    Wait-ListenerState `
        -Port $LobbyPort `
        -Present $true `
        -TimeoutSeconds $StartupTimeoutSeconds `
        -Label 'Lobby' | Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $ready = $false
    $newLog = ''
    do {
        Start-Sleep -Milliseconds 500
        $newLog = Get-NewLogText `
            -Path $latestLog `
            -PreviousLength $previousLength
        $ready = $newLog -match 'Done \('
    } while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline)

    if (-not $ready) {
        throw 'Lobby did not reach the Done state.'
    }
    $fatalStartupPattern = (
        '(?im)Failed to start the minecraft server|' +
        'Encountered an unexpected exception|' +
        'FatalStartupException|' +
        'UnsupportedClassVersionError|' +
        'Unable to access jarfile|' +
        'Error loading plugin|' +
        'Invalid plugin'
    )
    if ($newLog -match $fatalStartupPattern) {
        throw 'Lobby emitted a fatal startup signature.'
    }

    $viaVersionLoaded = (
        $newLog -match '(?i)\[ViaVersion\] Enabling ViaVersion'
    )
    $viaBackwardsLoaded = (
        $newLog -match '(?i)\[ViaBackwards\] Enabling ViaBackwards'
    )
    if ($viaVersionLoaded -ne $ExpectVia -or
        $viaBackwardsLoaded -ne $ExpectVia) {
        throw 'Lobby Via plugin startup ownership did not match the requested state.'
    }
}

function Start-VelocityAndValidate {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [Parameter(Mandatory)][bool]$ExpectProxyVia,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$ExpectedAuthorizerVersion
    )

    $latestLog = Join-Path $Roots.Velocity 'logs\latest.log'
    $previousLength = if (Test-Path -LiteralPath $latestLog -PathType Leaf) {
        (Read-SharedText -Path $latestLog).Length
    }
    else {
        0
    }

    $taskBeforeStart = Get-ScheduledTask `
        -TaskName $VelocityTaskName `
        -ErrorAction Stop
    if ($taskBeforeStart.State -eq 'Running') {
        throw 'Velocity scheduled task is still running before startup.'
    }
    Start-ScheduledTask -TaskName $VelocityTaskName
    Wait-ListenerState `
        -Port $VelocityPort `
        -Present $true `
        -TimeoutSeconds $StartupTimeoutSeconds `
        -Label 'Velocity' | Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $ready = $false
    $newLog = ''
    do {
        Start-Sleep -Milliseconds 500
        $newLog = Get-NewLogText `
            -Path $latestLog `
            -PreviousLength $previousLength
        $ready = (
            $newLog -match "Booting up Velocity $([regex]::Escape($ExpectedVersion))" -and
            $newLog -match (
                'Loaded plugin hechao-velocity-authorizer ' +
                [regex]::Escape($ExpectedAuthorizerVersion)
            ) -and
            $newLog -match 'Done \('
        )
    } while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline)

    if (-not $ready) {
        throw 'Velocity did not reach the reviewed startup state.'
    }
    if ($newLog -match '(?im)/ERROR\]|FATAL|Exception') {
        throw 'Velocity emitted an error, fatal entry, or exception during startup.'
    }

    $viaVersionLoaded = $newLog -match 'Loaded plugin viaversion 5\.11\.0'
    $viaBackwardsLoaded = $newLog -match 'Loaded plugin viabackwards 5\.11\.0'
    if ($viaVersionLoaded -ne $ExpectProxyVia -or
        $viaBackwardsLoaded -ne $ExpectProxyVia) {
        throw 'Velocity Via plugin startup ownership did not match the requested state.'
    }
}

function Set-VelocityTaskRuntime {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    $newAction = New-ScheduledTaskAction `
        -Execute $Executable `
        -Argument $Arguments `
        -WorkingDirectory $WorkingDirectory
    Set-ScheduledTask `
        -TaskName $VelocityTaskName `
        -Action $newAction | Out-Null
}

function Copy-VerifiedIncomingFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )

    $incoming = "$Destination.incoming-$([Guid]::NewGuid().ToString('N'))"
    try {
        Copy-Item -LiteralPath $Source -Destination $incoming
        Assert-FileHash `
            -Path $incoming `
            -ExpectedSha256 $ExpectedSha256 `
            -Label 'Incoming production artifact' | Out-Null
        Move-Item -LiteralPath $incoming -Destination $Destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $incoming -PathType Leaf) {
            Remove-Item -LiteralPath $incoming -Force
        }
    }
}

function Install-ProxyTranslationFiles {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    Assert-FileHash `
        -Path $Velocity4Source `
        -ExpectedSha256 $ExpectedVelocity4Sha256 `
        -Label 'Velocity 4 source JAR' | Out-Null
    Assert-FileHash `
        -Path $Java25Executable `
        -ExpectedSha256 $ExpectedJava25Sha256 `
        -Label 'Java 25 executable' | Out-Null
    Assert-FileHash `
        -Path $Authorizer031Source `
        -ExpectedSha256 $ExpectedAuthorizer031Sha256 `
        -Label 'Authorizer 0.3.1 source JAR' | Out-Null

    Copy-VerifiedIncomingFile `
        -Source $Velocity4Source `
        -Destination (Join-Path $Roots.Velocity 'velocity.jar') `
        -ExpectedSha256 $ExpectedVelocity4Sha256

    $velocityPlugins = Join-Path $Roots.Velocity 'plugins'
    $legacyAuthorizer = Join-Path `
        $velocityPlugins `
        'HechaoVelocityAuthorizer-0.3.0.jar'
    if (Test-Path -LiteralPath $legacyAuthorizer -PathType Leaf) {
        Remove-Item -LiteralPath $legacyAuthorizer -Force
    }
    Copy-VerifiedIncomingFile `
        -Source $Authorizer031Source `
        -Destination (
            Join-Path `
                $velocityPlugins `
                'HechaoVelocityAuthorizer-0.3.1.jar'
        ) `
        -ExpectedSha256 $ExpectedAuthorizer031Sha256

    $via = Get-ViaPaths -Roots $Roots
    Move-Item `
        -LiteralPath $via.VelocityViaVersionDisabled `
        -Destination $via.VelocityViaVersionEnabled
    Move-Item `
        -LiteralPath $via.VelocityViaBackwardsDisabled `
        -Destination $via.VelocityViaBackwardsEnabled
    Move-Item `
        -LiteralPath $via.LobbyViaVersionEnabled `
        -Destination $via.LobbyViaVersionDisabled
    Move-Item `
        -LiteralPath $via.LobbyViaBackwardsEnabled `
        -Destination $via.LobbyViaBackwardsDisabled

    Set-VelocityTaskRuntime `
        -Executable (Get-NormalizedPath -Path $Java25Executable) `
        -Arguments $script:VelocityArguments `
        -WorkingDirectory $Roots.Velocity
}

function Assert-BackupPath {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [Parameter(Mandatory)][string]$Path
    )

    $resolved = Get-NormalizedPath -Path $Path
    $expectedPrefix = $Roots.Backup +
        [IO.Path]::DirectorySeparatorChar +
        $script:MigrationPrefix
    if (-not $resolved.StartsWith(
            $expectedPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The rollback backup is outside the reviewed migration backup boundary.'
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "The rollback backup directory is missing: $resolved"
    }
    if (-not (Test-Path `
            -LiteralPath (Join-Path $resolved 'prechange.json') `
            -PathType Leaf)) {
        throw 'The rollback backup does not contain prechange.json.'
    }

    return $resolved
}

function Remove-ReviewedPluginFiles {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    $velocityPlugins = Join-Path $Roots.Velocity 'plugins'
    foreach ($item in @(
            'HechaoVelocityAuthorizer-0.3.0.jar',
            'HechaoVelocityAuthorizer-0.3.1.jar',
            'ViaVersion-5.11.0.jar',
            'ViaVersion-5.11.0.jar.disabled',
            'ViaBackwards-5.11.0.jar',
            'ViaBackwards-5.11.0.jar.disabled'
        )) {
        $path = Join-Path $velocityPlugins $item
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $lobbyPlugins = Join-Path $Roots.Lobby 'plugins'
    foreach ($item in @(
            'ViaVersion-5.11.0.jar',
            'ViaVersion-5.11.0.jar.disabled',
            'ViaBackwards-5.11.0.jar',
            'ViaBackwards-5.11.0.jar.disabled'
        )) {
        $path = Join-Path $lobbyPlugins $item
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Restore-LegacyEnvironment {
    param(
        [Parameter(Mandatory)][pscustomobject]$Roots,
        [Parameter(Mandatory)][string]$Path
    )

    $script:RollbackAttempted = $true
    $resolvedBackup = Assert-BackupPath -Roots $Roots -Path $Path

    if ((Get-EstablishedClientCount) -ne 0) {
        throw 'Rollback refuses to disconnect active proxy clients.'
    }

    if (@(Get-Listener -Port $VelocityPort).Count -gt 0) {
        Stop-VelocityTask
    }
    if (@(Get-Listener -Port $LobbyPort).Count -gt 0) {
        Stop-LobbyGracefully -Roots $Roots
    }

    Copy-VerifiedIncomingFile `
        -Source (Join-Path $resolvedBackup 'velocity\velocity.jar') `
        -Destination (Join-Path $Roots.Velocity 'velocity.jar') `
        -ExpectedSha256 $ExpectedVelocityLegacySha256
    Copy-VerifiedIncomingFile `
        -Source (Join-Path $resolvedBackup 'velocity\velocity.toml') `
        -Destination (Join-Path $Roots.Velocity 'velocity.toml') `
        -ExpectedSha256 $ExpectedVelocityTomlSha256

    Remove-ReviewedPluginFiles -Roots $Roots
    foreach ($restore in @(
            @(
                'velocity\plugins\HechaoVelocityAuthorizer-0.3.0.jar',
                (
                    Join-Path `
                        $Roots.Velocity `
                        'plugins\HechaoVelocityAuthorizer-0.3.0.jar'
                ),
                $ExpectedAuthorizer030Sha256
            ),
            @(
                'velocity\plugins\ViaVersion-5.11.0.jar.disabled',
                (
                    Join-Path `
                        $Roots.Velocity `
                        'plugins\ViaVersion-5.11.0.jar.disabled'
                ),
                $ExpectedViaVersionSha256
            ),
            @(
                'velocity\plugins\ViaBackwards-5.11.0.jar.disabled',
                (
                    Join-Path `
                        $Roots.Velocity `
                        'plugins\ViaBackwards-5.11.0.jar.disabled'
                ),
                $ExpectedViaBackwardsSha256
            ),
            @(
                'lobby\plugins\ViaVersion-5.11.0.jar',
                (
                    Join-Path `
                        $Roots.Lobby `
                        'plugins\ViaVersion-5.11.0.jar'
                ),
                $ExpectedViaVersionSha256
            ),
            @(
                'lobby\plugins\ViaBackwards-5.11.0.jar',
                (
                    Join-Path `
                        $Roots.Lobby `
                        'plugins\ViaBackwards-5.11.0.jar'
                ),
                $ExpectedViaBackwardsSha256
            )
        )) {
        Copy-VerifiedIncomingFile `
            -Source (Join-Path $resolvedBackup $restore[0]) `
            -Destination $restore[1] `
            -ExpectedSha256 $restore[2]
    }

    $authorizerConfigBackup = Join-Path `
        $resolvedBackup `
        'velocity\plugins\hechao-velocity-authorizer'
    $authorizerConfigDestination = Join-Path `
        $Roots.Velocity `
        'plugins\hechao-velocity-authorizer'
    if (-not (Test-Path `
            -LiteralPath $authorizerConfigBackup `
            -PathType Container)) {
        throw 'The rollback backup is missing the Authorizer configuration.'
    }
    if (Test-Path -LiteralPath $authorizerConfigDestination) {
        Remove-Item `
            -LiteralPath $authorizerConfigDestination `
            -Recurse `
            -Force
    }
    Copy-Item `
        -LiteralPath $authorizerConfigBackup `
        -Destination $authorizerConfigDestination `
        -Recurse

    $taskXmlPath = Join-Path $resolvedBackup 'tasks\velocity.xml'
    if (-not (Test-Path -LiteralPath $taskXmlPath -PathType Leaf)) {
        throw 'The rollback backup is missing the Velocity task XML.'
    }
    Register-ScheduledTask `
        -TaskName $VelocityTaskName `
        -Xml ([IO.File]::ReadAllText($taskXmlPath)) `
        -Force | Out-Null

    Start-LobbyAndValidate -Roots $Roots -ExpectVia $true
    Start-VelocityAndValidate `
        -Roots $Roots `
        -ExpectProxyVia $false `
        -ExpectedVersion '3.4.0-SNAPSHOT' `
        -ExpectedAuthorizerVersion '0.3.0'
    $legacy = Assert-LegacyBaseline -Roots $Roots -RequireRunning

    $result = [ordered]@{
        schemaVersion = 1
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        action = 'rollback'
        result = 'legacy-backend-translation-restored'
        backupDirectory = $resolvedBackup
        state = $legacy.State
        velocityClientConnections = $legacy.VelocityClientConnections
        rollbackCompleted = $true
    }
    [IO.File]::WriteAllText(
        (Join-Path $resolvedBackup 'rollback.json'),
        ($result | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    return $result
}

function Invoke-Migration {
    param([Parameter(Mandatory)][pscustomobject]$Roots)

    if (-not $ConfirmMigration) {
        throw 'Migrate requires -ConfirmMigration.'
    }

    $baseline = Assert-LegacyBaseline -Roots $Roots -RequireRunning
    if ($baseline.VelocityClientConnections -ne 0) {
        throw 'Migration refuses to disconnect active proxy clients.'
    }

    Assert-FileHash `
        -Path $Velocity4Source `
        -ExpectedSha256 $ExpectedVelocity4Sha256 `
        -Label 'Velocity 4 source JAR' | Out-Null
    Assert-FileHash `
        -Path $Java25Executable `
        -ExpectedSha256 $ExpectedJava25Sha256 `
        -Label 'Java 25 executable' | Out-Null
    Assert-FileHash `
        -Path $Authorizer031Source `
        -ExpectedSha256 $ExpectedAuthorizer031Sha256 `
        -Label 'Authorizer 0.3.1 source JAR' | Out-Null

    $backup = New-MigrationBackup -Roots $Roots -Baseline $baseline
    try {
        if ((Get-EstablishedClientCount) -ne 0) {
            throw 'A client connected after backup creation; migration was not started.'
        }
        Stop-VelocityTask
        Stop-LobbyGracefully -Roots $Roots
        Install-ProxyTranslationFiles -Roots $Roots

        Start-LobbyAndValidate -Roots $Roots -ExpectVia $false
        Start-VelocityAndValidate `
            -Roots $Roots `
            -ExpectProxyVia $true `
            -ExpectedVersion '4.0.0' `
            -ExpectedAuthorizerVersion '0.3.1'

        $deployed = Assert-ProxyBaseline -Roots $Roots -RequireRunning
        if ($deployed.VelocityClientConnections -ne 0) {
            throw 'Unexpected clients connected during the maintenance validation window.'
        }

        $result = [ordered]@{
            schemaVersion = 1
            capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            action = 'migrate'
            result = 'proxy-only-translation-ready'
            backupDirectory = $backup
            state = $deployed.State
            velocity = [ordered]@{
                version = '4.0.0'
                javaMajor = 25
                authorizerVersion = '0.3.1'
                authorizerMode = 'monitor'
                enabledViaJarCount = 2
                listenerCount = @(Get-Listener -Port $VelocityPort).Count
                establishedClientConnections = (
                    $deployed.VelocityClientConnections
                )
            }
            lobby = [ordered]@{
                enabledViaJarCount = 0
                listenerCount = @(Get-Listener -Port $LobbyPort).Count
            }
            productionProtocolTranslationFlagChanged = false
            gameWorldFilesChanged = false
            rollbackRequired = false
        }
        [IO.File]::WriteAllText(
            (Join-Path $backup 'migration.json'),
            ($result | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))

        return $result
    }
    catch {
        $migrationError = $_.Exception.Message
        $rollbackError = $null
        try {
            Restore-LegacyEnvironment -Roots $Roots -Path $backup | Out-Null
        }
        catch {
            $rollbackError = $_.Exception.Message
        }

        if ($null -ne $rollbackError) {
            throw (
                "Migration failed: $migrationError " +
                "Automatic rollback also failed: $rollbackError"
            )
        }
        throw "Migration failed and was rolled back: $migrationError"
    }
}

Assert-Administrator
$roots = Assert-SafeRoots

switch ($Action) {
    'Status' {
        Get-DeploymentStatus -Roots $roots |
            ConvertTo-Json -Depth 8 -Compress
    }
    'Migrate' {
        Invoke-Migration -Roots $roots |
            ConvertTo-Json -Depth 8 -Compress
    }
    'Rollback' {
        if (-not $ConfirmRollback) {
            throw 'Rollback requires -ConfirmRollback.'
        }
        if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
            throw 'Rollback requires -BackupDirectory.'
        }
        Restore-LegacyEnvironment `
            -Roots $roots `
            -Path $BackupDirectory |
            ConvertTo-Json -Depth 8 -Compress
    }
}
