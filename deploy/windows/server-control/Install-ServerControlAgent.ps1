[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentExecutable,

    [Parameter(Mandatory)]
    [string]$Configuration,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [string]$InstallDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent",

    [string]$TaskName = 'Hechao Launcher Server Control Agent',

    [string]$BackupRoot = "$env:ProgramData\Hechao\backups",

    [switch]$StartAgent
)

$ErrorActionPreference = 'Stop'

function Set-RestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$LiteralPath)

    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @('SYSTEM', 'BUILTIN\Administrators')) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            (
                [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            ),
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

function Wait-AgentTaskStopped {
    param(
        [Parameter(Mandatory)][string]$Name,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
        if ($null -eq $task -or $task.State -ne 'Running') {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "The existing agent task did not stop within $TimeoutSeconds seconds."
}

function Get-OptionalConfigurationValue {
    param(
        [Parameter(Mandatory)]
        [psobject]$InputObject,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [AllowNull()]
        $DefaultValue
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $DefaultValue
    }

    return $property.Value
}

$sourceExecutable = (Resolve-Path -LiteralPath $AgentExecutable).Path
$sourceConfiguration = (Resolve-Path -LiteralPath $Configuration).Path
$expected = $ExpectedSha256.ToUpperInvariant()
$actual = (Get-FileHash -LiteralPath $sourceExecutable -Algorithm SHA256).Hash
if ($actual -ne $expected) {
    throw "Agent SHA-256 mismatch. Expected $expected, got $actual."
}

$configurationObject = Get-Content -Raw -LiteralPath $sourceConfiguration |
    ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$configurationObject.agentId) -or
    $configurationObject.agentId -notmatch
        '^[a-z0-9][a-z0-9._-]{1,63}$') {
    throw 'The staged agent configuration has an invalid agentId.'
}
if ($null -eq $configurationObject.targets -or
    @($configurationObject.targets).Count -lt 1) {
    throw 'The staged agent configuration has no managed targets.'
}
$configuredStateDirectory = [string]$configurationObject.stateDirectory
if ([string]::IsNullOrWhiteSpace($configuredStateDirectory)) {
    $configuredStateDirectory =
        "$env:ProgramData\Hechao\ServerControlAgent"
}
$stateDirectory = [System.IO.Path]::GetFullPath(
    $configuredStateDirectory
)
$runtimeMarkerDirectory = Join-Path $stateDirectory 'runtime'
if (-not (Test-Path -LiteralPath $configurationObject.tokenPath -PathType Leaf)) {
    throw "The protected agent token is missing: $($configurationObject.tokenPath)"
}
if (-not (
    Test-Path -LiteralPath $configurationObject.consoleSubmitScript -PathType Leaf
)) {
    throw (
        'The fixed Minecraft console submitter is missing: ' +
        $configurationObject.consoleSubmitScript
    )
}

$slotProvisioning = Get-OptionalConfigurationValue `
    -InputObject $configurationObject `
    -Name 'deploymentSlotProvisioning' `
    -DefaultValue $null
if ($null -ne $slotProvisioning -and [bool]$slotProvisioning.enabled) {
    $slotRoot = [string]$slotProvisioning.rootDirectory
    $slotTaskInstaller = [string]$slotProvisioning.taskInstallerScript
    $slotMaximum = [int]$slotProvisioning.maximumSlots
    if (-not [string]::Equals(
            [string]$configurationObject.agentId,
            'owl5',
            [System.StringComparison]::Ordinal
        ) -or
        -not [string]::Equals(
            [string]$slotProvisioning.templateServerId,
            'activity',
            [System.StringComparison]::Ordinal
        ) -or
        -not [System.IO.Path]::IsPathFullyQualified($slotRoot) -or
        -not [System.IO.Path]::IsPathFullyQualified($slotTaskInstaller) -or
        $slotMaximum -lt 1 -or
        $slotMaximum -gt 16) {
        throw 'Dynamic deployment slot provisioning configuration is invalid.'
    }
    if (-not (Test-Path -LiteralPath $slotTaskInstaller -PathType Leaf)) {
        throw "Dynamic slot task installer is missing: $slotTaskInstaller"
    }
}

foreach ($target in @($configurationObject.targets)) {
    $serverDeletionEnabled = [bool](Get-OptionalConfigurationValue `
        -InputObject $target `
        -Name 'serverDeletionEnabled' `
        -DefaultValue $false)
    $serverDirectoryExists = Test-Path -LiteralPath $target.serverDirectory
    $serverDirectoryPresent = Test-Path `
        -LiteralPath $target.serverDirectory `
        -PathType Container
    if ($serverDirectoryExists -and -not $serverDirectoryPresent) {
        throw "Server directory path is not a directory: $($target.serverDirectory)"
    }
    if (-not $serverDirectoryPresent -and -not $serverDeletionEnabled) {
        throw "Server directory is missing: $($target.serverDirectory)"
    }
    $memorySettingsPath = Join-Path (
        [System.IO.Path]::GetFullPath([string]$target.serverDirectory)
    ) ([string]$target.memorySettingsRelativePath)
    if ($serverDirectoryPresent) {
        if (-not (
            Test-Path -LiteralPath (
                Join-Path $target.serverDirectory $target.propertiesRelativePath
            ) -PathType Leaf
        )) {
            throw "server.properties is missing for target $($target.serverId)."
        }
        if (-not (Test-Path -LiteralPath $memorySettingsPath -PathType Leaf)) {
            throw "JVM memory settings file is missing for target $($target.serverId)."
        }
        $memorySettingsText = [System.Text.Encoding]::Latin1.GetString(
            [System.IO.File]::ReadAllBytes($memorySettingsPath))
        $initialMemoryMatches = [regex]::Matches(
            $memorySettingsText,
            '(?i)(?<!\S)-Xms[1-9][0-9]*[KMG](?=\s|$)')
        $maximumMemoryMatches = [regex]::Matches(
            $memorySettingsText,
            '(?i)(?<!\S)-Xmx[1-9][0-9]*[KMG](?=\s|$)')
        if ($initialMemoryMatches.Count -ne 1 -or
            $maximumMemoryMatches.Count -ne 1) {
            throw (
                "JVM memory settings file for target $($target.serverId) must " +
                'contain exactly one -Xms and one -Xmx argument.'
            )
        }
    }
    $packageDeploymentEnabled = [bool](Get-OptionalConfigurationValue `
        -InputObject $target `
        -Name 'packageDeploymentEnabled' `
        -DefaultValue $false)
    $startScriptRelativePath = [string](Get-OptionalConfigurationValue `
        -InputObject $target `
        -Name 'startScriptRelativePath' `
        -DefaultValue 'start.bat')
    if ([string]::IsNullOrWhiteSpace($startScriptRelativePath)) {
        $startScriptRelativePath = 'start.bat'
    }
    $serverDirectory = [System.IO.Path]::GetFullPath(
        [string]$target.serverDirectory
    ).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $startScriptPath = [System.IO.Path]::GetFullPath(
        (Join-Path $target.serverDirectory $startScriptRelativePath)
    )
    $startScriptText = $null
    if ($packageDeploymentEnabled) {
        if (-not [string]::Equals(
                [string]$configurationObject.agentId,
                'owl5',
                [System.StringComparison]::Ordinal
            ) -or
            -not [string]::Equals(
                [string]$target.conflictGroup,
                'owl5-activity-slot',
                [System.StringComparison]::Ordinal
            ) -or
            [int]$target.port -ne 25568) {
            throw 'Package deployment is restricted to approved owl5 activity slots.'
        }
        if (-not $startScriptPath.StartsWith(
                $serverDirectory,
                [System.StringComparison]::OrdinalIgnoreCase
            )) {
            throw "Managed start script escapes target $($target.serverId)."
        }
        if ($serverDirectoryPresent -and -not (
            Test-Path -LiteralPath $startScriptPath -PathType Leaf
        )) {
            throw "Managed start script is missing for target $($target.serverId)."
        }
        if ($serverDirectoryPresent) {
            $startScriptText = [System.IO.File]::ReadAllText($startScriptPath)
        }
        if ($serverDirectoryPresent -and
            $startScriptText -notmatch '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*(?:\r)?$') {
            throw (
                "Managed start script for target $($target.serverId) is not " +
                'managed-start aware.'
            )
        }
    }
    $task = Get-ScheduledTask `
        -TaskName $target.startTaskName `
        -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        throw "Managed start task is missing: $($target.startTaskName)"
    }
    $taskArguments = [string]$task.Actions[0].Arguments
    $expectedServerId = "-ServerId `"$($target.serverId)`""
    $expectedMarkerDirectory =
        "-RuntimeMarkerDirectory `"$runtimeMarkerDirectory`""
    $expectedStartScript = "-StartScript `"$startScriptPath`""
    $startScriptMatches = -not $packageDeploymentEnabled
    if ($packageDeploymentEnabled) {
        $startScriptMatches = $taskArguments.IndexOf(
            $expectedStartScript,
            [System.StringComparison]::OrdinalIgnoreCase
        ) -ge 0
    }
    if ($taskArguments.IndexOf(
            $expectedServerId,
            [System.StringComparison]::OrdinalIgnoreCase
        ) -lt 0 -or
        $taskArguments.IndexOf(
            $expectedMarkerDirectory,
            [System.StringComparison]::OrdinalIgnoreCase
        ) -lt 0 -or
        -not $startScriptMatches) {
        throw (
            "Managed start task $($target.startTaskName) is missing its " +
            'server identity runtime marker. Reinstall that launch task.'
        )
    }
}

$install = [System.IO.Path]::GetFullPath($InstallDirectory)
[System.IO.Directory]::CreateDirectory($install) | Out-Null
[System.IO.Directory]::CreateDirectory($runtimeMarkerDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path (
    [System.IO.Path]::GetFullPath($BackupRoot)
) "server-control-agent-$timestamp"
[System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null

$destinationExecutable = Join-Path $install 'Hechao.ServerControlAgent.exe'
$destinationConfiguration = Join-Path $install 'server-control-agent.json'
$stagedExecutable = Join-Path $install (
    '.agent-' + [Guid]::NewGuid().ToString('N') + '.tmp'
)
$stagedConfiguration = Join-Path $install (
    '.config-' + [Guid]::NewGuid().ToString('N') + '.tmp'
)
Copy-Item -LiteralPath $sourceExecutable -Destination $stagedExecutable
Copy-Item -LiteralPath $sourceConfiguration -Destination $stagedConfiguration
if ((Get-FileHash -LiteralPath $stagedExecutable -Algorithm SHA256).Hash -ne
    $expected) {
    throw 'The staged server control agent failed SHA-256 verification.'
}

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$taskWasRunning = $null -ne $existingTask -and $existingTask.State -eq 'Running'
$taskBackup = Join-Path $backupDirectory 'scheduled-task.xml'
if ($null -ne $existingTask) {
    Export-ScheduledTask -TaskName $TaskName |
        Set-Content -LiteralPath $taskBackup -Encoding utf8
    if ($taskWasRunning) {
        Stop-ScheduledTask -TaskName $TaskName
        Wait-AgentTaskStopped -Name $TaskName
    }
}

$executableBackup = Join-Path $backupDirectory 'Hechao.ServerControlAgent.exe'
$configurationBackup = Join-Path $backupDirectory 'server-control-agent.json'
$filesReplaced = $false
try {
    if (Test-Path -LiteralPath $destinationExecutable -PathType Leaf) {
        Move-Item `
            -LiteralPath $destinationExecutable `
            -Destination $executableBackup
    }
    if (Test-Path -LiteralPath $destinationConfiguration -PathType Leaf) {
        Move-Item `
            -LiteralPath $destinationConfiguration `
            -Destination $configurationBackup
    }
    Move-Item `
        -LiteralPath $stagedExecutable `
        -Destination $destinationExecutable
    Move-Item `
        -LiteralPath $stagedConfiguration `
        -Destination $destinationConfiguration
    $filesReplaced = $true

    $action = New-ScheduledTaskAction `
        -Execute $destinationExecutable `
        -Argument "--config `"$destinationConfiguration`"" `
        -WorkingDirectory $install
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 5 `
        -RestartInterval ([TimeSpan]::FromMinutes(1)) `
        -MultipleInstances IgnoreNew
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description (
            'Structured, allowlisted Hechao Minecraft server control agent.'
        ) `
        -Force |
        Out-Null
    Set-RestrictedDirectoryAcl -LiteralPath $install
    if (-not [string]::Equals(
            $install,
            $stateDirectory,
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
        Set-RestrictedDirectoryAcl -LiteralPath $stateDirectory
    }

    if ($StartAgent) {
        Start-ScheduledTask -TaskName $TaskName
        Start-Sleep -Seconds 2
        $startedTask = Get-ScheduledTask -TaskName $TaskName
        if ($startedTask.State -ne 'Running') {
            throw 'The installed server control agent did not remain running.'
        }
    }
}
catch {
    if (Test-Path -LiteralPath $stagedExecutable) {
        Remove-Item -LiteralPath $stagedExecutable -Force
    }
    if (Test-Path -LiteralPath $stagedConfiguration) {
        Remove-Item -LiteralPath $stagedConfiguration -Force
    }
    Unregister-ScheduledTask `
        -TaskName $TaskName `
        -Confirm:$false `
        -ErrorAction SilentlyContinue
    if ($filesReplaced) {
        Remove-Item `
            -LiteralPath $destinationExecutable `
            -Force `
            -ErrorAction SilentlyContinue
        Remove-Item `
            -LiteralPath $destinationConfiguration `
            -Force `
            -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $executableBackup -PathType Leaf) {
        Move-Item `
            -LiteralPath $executableBackup `
            -Destination $destinationExecutable
    }
    if (Test-Path -LiteralPath $configurationBackup -PathType Leaf) {
        Move-Item `
            -LiteralPath $configurationBackup `
            -Destination $destinationConfiguration
    }
    if (Test-Path -LiteralPath $taskBackup -PathType Leaf) {
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Xml (Get-Content -Raw -LiteralPath $taskBackup) `
            -Force |
            Out-Null
        if ($taskWasRunning) {
            Start-ScheduledTask -TaskName $TaskName
        }
    }
    throw
}

[ordered]@{
    agent_id = $configurationObject.agentId
    executable = $destinationExecutable
    configuration = $destinationConfiguration
    sha256 = $expected
    task_name = $TaskName
    task_state = [string](
        Get-ScheduledTask -TaskName $TaskName
    ).State
    backup = $backupDirectory
    server_action = 'none'
} | ConvertTo-Json -Compress
