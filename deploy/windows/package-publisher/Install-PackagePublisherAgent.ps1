[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublisherExecutable,

    [Parameter(Mandatory)]
    [string]$Configuration,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [string]$InstallDirectory =
        "$env:ProgramData\Hechao\PackagePublisherAgent",

    [string]$TaskName = 'Hechao Launcher Package Publisher Agent',

    [string]$RunAsUser =
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,

    [switch]$StartAgent
)

$ErrorActionPreference = 'Stop'

$windowsPrincipal = [System.Security.Principal.WindowsPrincipal]::new(
    [System.Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $windowsPrincipal.IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Package publisher installation requires an elevated PowerShell 7 session.'
}

function Wait-PublisherTaskStopped {
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
    throw "The package publisher task did not stop within $TimeoutSeconds seconds."
}

$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
if (-not [string]::Equals(
        $RunAsUser,
        $currentIdentity,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw 'CurrentUser DPAPI requires installation under the same run-as user.'
}

$sourceExecutable = (Resolve-Path -LiteralPath $PublisherExecutable).Path
$sourceConfiguration = (Resolve-Path -LiteralPath $Configuration).Path
$expected = $ExpectedSha256.ToUpperInvariant()
$actual = (Get-FileHash -LiteralPath $sourceExecutable -Algorithm SHA256).Hash
if ($actual -ne $expected) {
    throw "Publisher SHA-256 mismatch. Expected $expected, got $actual."
}
$expectedConfigurationHash = (
    Get-FileHash -LiteralPath $sourceConfiguration -Algorithm SHA256
).Hash

$configurationObject = Get-Content -Raw -LiteralPath $sourceConfiguration |
    ConvertFrom-Json
if ([string]$configurationObject.agentId -notmatch
    '^[a-z0-9][a-z0-9._-]{1,63}$') {
    throw 'The package publisher configuration has an invalid agentId.'
}
foreach ($path in @(
        [string]$configurationObject.tokenPath,
        [string]$configurationObject.signingKeyPath,
        [string]$configurationObject.ossCredentialPath
    )) {
    if ([string]::IsNullOrWhiteSpace($path) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "A protected publisher input is missing: $path"
    }
}

$install = [System.IO.Path]::GetFullPath($InstallDirectory)
[System.IO.Directory]::CreateDirectory($install) | Out-Null
$destinationExecutable = Join-Path $install 'Hechao.Publisher.exe'
$destinationConfiguration = Join-Path $install 'package-publisher-agent.json'
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDirectory = Join-Path $install "backup-$timestamp"
[System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$taskWasRunning = $null -ne $existingTask -and $existingTask.State -eq 'Running'
$taskBackupPath = Join-Path $backupDirectory 'scheduled-task.xml'
if ($null -ne $existingTask) {
    Export-ScheduledTask -TaskName $TaskName |
        Set-Content -LiteralPath $taskBackupPath -Encoding utf8
}
$executableExisted = Test-Path -LiteralPath $destinationExecutable -PathType Leaf
$configurationExisted = Test-Path `
    -LiteralPath $destinationConfiguration `
    -PathType Leaf
$executableBackupPath = Join-Path $backupDirectory 'Hechao.Publisher.exe'
$configurationBackupPath = Join-Path (
    $backupDirectory
) 'package-publisher-agent.json'
if ($executableExisted) {
    Copy-Item -LiteralPath $destinationExecutable -Destination (
        $executableBackupPath
    )
}
if ($configurationExisted) {
    Copy-Item -LiteralPath $destinationConfiguration -Destination (
        $configurationBackupPath
    )
}

$stagedExecutable = Join-Path $install (
    '.publisher-' + [Guid]::NewGuid().ToString('N') + '.tmp'
)
$stagedConfiguration = Join-Path $install (
    '.config-' + [Guid]::NewGuid().ToString('N') + '.tmp'
)
try {
    if ($taskWasRunning) {
        Stop-ScheduledTask -TaskName $TaskName
        Wait-PublisherTaskStopped -Name $TaskName
    }

    Copy-Item -LiteralPath $sourceExecutable -Destination $stagedExecutable
    Copy-Item -LiteralPath $sourceConfiguration -Destination $stagedConfiguration
    if ((Get-FileHash -LiteralPath $stagedExecutable -Algorithm SHA256).Hash -ne
        $expected) {
        throw 'The staged publisher failed SHA-256 verification.'
    }
    Move-Item -LiteralPath $stagedExecutable -Destination (
        $destinationExecutable
    ) -Force
    Move-Item -LiteralPath $stagedConfiguration -Destination (
        $destinationConfiguration
    ) -Force
    if ((Get-FileHash -LiteralPath $destinationExecutable -Algorithm SHA256).Hash -ne
        $expected -or
        (Get-FileHash `
            -LiteralPath $destinationConfiguration `
            -Algorithm SHA256).Hash -ne $expectedConfigurationHash) {
        throw 'The installed package publisher files failed verification.'
    }

    $action = New-ScheduledTaskAction `
        -Execute $destinationExecutable `
        -Argument "run-package-agent --config `"$destinationConfiguration`"" `
        -WorkingDirectory $install
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $RunAsUser
    $principal = New-ScheduledTaskPrincipal `
        -UserId $RunAsUser `
        -LogonType Interactive `
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
        -Description 'Publishes reviewed Hechao client packages to private OSS.' `
        -Force |
        Out-Null

    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @($RunAsUser, 'SYSTEM', 'BUILTIN\Administrators')) {
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
    Set-Acl -LiteralPath $install -AclObject $acl

    if ($StartAgent) {
        Start-ScheduledTask -TaskName $TaskName
    }
}
catch {
    $failure = $_
    try {
        $currentTask = Get-ScheduledTask `
            -TaskName $TaskName `
            -ErrorAction SilentlyContinue
        if ($null -ne $currentTask) {
            if ($currentTask.State -eq 'Running') {
                Stop-ScheduledTask -TaskName $TaskName
                Wait-PublisherTaskStopped -Name $TaskName
            }
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        }
        if ($executableExisted) {
            Copy-Item `
                -LiteralPath $executableBackupPath `
                -Destination $destinationExecutable `
                -Force
        }
        else {
            Remove-Item `
                -LiteralPath $destinationExecutable `
                -Force `
                -ErrorAction SilentlyContinue
        }
        if ($configurationExisted) {
            Copy-Item `
                -LiteralPath $configurationBackupPath `
                -Destination $destinationConfiguration `
                -Force
        }
        else {
            Remove-Item `
                -LiteralPath $destinationConfiguration `
                -Force `
                -ErrorAction SilentlyContinue
        }
        if ($null -ne $existingTask) {
            Register-ScheduledTask `
                -TaskName $TaskName `
                -Xml (Get-Content -Raw -LiteralPath $taskBackupPath) |
                Out-Null
            if ($taskWasRunning) {
                Start-ScheduledTask -TaskName $TaskName
            }
        }
    }
    catch {
        throw (
            "Package publisher installation failed and rollback also failed. " +
            "Install error: $($failure.Exception.Message) Rollback error: " +
            $_.Exception.Message
        )
    }
    throw $failure
}
finally {
    Remove-Item -LiteralPath $stagedExecutable -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagedConfiguration -Force -ErrorAction SilentlyContinue
}

[ordered]@{
    task_name = $TaskName
    run_as_user = $RunAsUser
    executable = $destinationExecutable
    configuration = $destinationConfiguration
    backup_directory = $backupDirectory
    agent_started = [bool]$StartAgent
} | ConvertTo-Json -Compress
