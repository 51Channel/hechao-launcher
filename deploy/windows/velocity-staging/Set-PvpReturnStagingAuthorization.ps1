[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Status', 'Remove')]
    [string]$Action,

    [string]$ProductionRoot = 'E:\Velocity',

    [string]$StagingRoot = 'E:\Velocity-PvpReturn-Staging',

    [string]$TaskName = 'Hechao-Velocity-PvpReturn-Staging',

    [ValidateRange(1, 65535)]
    [int]$ProductionPort = 25577,

    [ValidateRange(1, 65535)]
    [int]$StagingPort = 25579,

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedProductionConfigSha256 =
        'A300E7CBE190B42E434763CFCCAFB9D821F894B02E72A594ED72B340C3E22C70',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedAuthorizerSha256 =
        '289B13472AEAC4073895EF9BE7E630B4B5AACEC48A4D0FD849BBAFE0064E681D',

    [ValidatePattern('^http://127\.0\.0\.1:[0-9]{1,5}/v1/internal/velocity/authorize$')]
    [string]$ApiUrl =
        'http://127.0.0.1:18093/v1/internal/velocity/authorize',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$ProxyInstance = 'owl5-pvp-return-staging',

    [string]$BackupRoot = 'E:\manual-backups',

    [switch]$ProbeApi,

    [switch]$ConfirmRemoval
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentPath = Get-NormalizedPath -Path $Parent
    $childPath = Get-NormalizedPath -Path $Child
    $prefix = $parentPath + [IO.Path]::DirectorySeparatorChar
    if (-not $childPath.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected parent: $childPath"
    }

    return $childPath
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

function Get-ProductionBaseline {
    param(
        [Parameter(Mandatory = $true)][string]$Production,
        [Parameter(Mandatory = $true)][string]$AuthorizerJar
    )

    Assert-FileHash `
        -Path (Join-Path $Production 'velocity.toml') `
        -ExpectedSha256 $ExpectedProductionConfigSha256 `
        -Label 'Production Velocity configuration' | Out-Null
    Assert-FileHash `
        -Path $AuthorizerJar `
        -ExpectedSha256 $ExpectedAuthorizerSha256 `
        -Label 'Production Hechao authorizer JAR' | Out-Null

    $productionListeners = @(Get-PortListeners -Port $ProductionPort)
    if ($productionListeners.Count -ne 1) {
        throw "Expected exactly one production listener on port $ProductionPort."
    }

    $enabledViaJars = @(
        Get-ChildItem -LiteralPath (Join-Path $Production 'plugins') -File |
            Where-Object {
                $_.Extension -eq '.jar' -and
                $_.Name -match '^Via(?:Version|Backwards)'
            }
    )
    if ($enabledViaJars.Count -ne 0) {
        throw 'Production Via JARs are already enabled.'
    }

    return [pscustomobject]@{
        ProcessId = $productionListeners[0].OwningProcess
        ConfigSha256 = $ExpectedProductionConfigSha256.ToUpperInvariant()
        AuthorizerSha256 = $ExpectedAuthorizerSha256.ToUpperInvariant()
        EnabledViaJarCount = 0
    }
}

function Assert-StagingStopped {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task -and [string]$task.State -eq 'Running') {
        throw "Staging task $TaskName must be stopped first."
    }

    $listeners = @(Get-PortListeners -Port $StagingPort)
    if ($listeners.Count -ne 0) {
        throw "Staging port $StagingPort must not be listening."
    }
}

function Read-Configuration {
    param([Parameter(Mandatory = $true)][string]$Path)

    $result = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $parts = $trimmed.Split('=', 2)
        if ($parts.Count -ne 2) {
            throw "Invalid authorizer configuration line in $Path."
        }
        $result[$parts[0].Trim()] = $parts[1].Trim()
    }

    return $result
}

function Get-AuthorizationStatus {
    param(
        [Parameter(Mandatory = $true)][string]$JarPath,
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][pscustomobject]$Production,
        [switch]$ProbeApi
    )

    $jarInstalled = Test-Path -LiteralPath $JarPath -PathType Leaf
    $configInstalled = Test-Path -LiteralPath $ConfigPath -PathType Leaf
    $jarHashValid = $false
    $configurationValid = $false
    $tokenConfigured = $false
    $apiProbeReason = $null

    if ($jarInstalled) {
        $jarHashValid =
            (Get-FileHash -LiteralPath $JarPath -Algorithm SHA256).Hash -eq
            $ExpectedAuthorizerSha256.ToUpperInvariant()
    }
    if ($configInstalled) {
        $configuration = Read-Configuration -Path $ConfigPath
        $token = [string]$configuration['token']
        $tokenConfigured =
            $token -cmatch '^[A-Za-z0-9._~-]{24,256}$'
        $configurationValid =
            [string]$configuration['mode'] -eq 'monitor' -and
            [string]$configuration['api-url'] -eq $ApiUrl -and
            [string]$configuration['proxy-instance'] -eq $ProxyInstance -and
            [string]$configuration['request-timeout-millis'] -eq '5000' -and
            $tokenConfigured

        if ($ProbeApi) {
            if (-not $configurationValid) {
                throw 'Cannot probe the staging API with an invalid configuration.'
            }
            $probeSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
            $probeBody = @{
                minecraftUuid = [Guid]::NewGuid()
                minecraftName = "Prb$probeSuffix"
                velocityTarget = 'lobby'
                initialConnection = $false
                remoteAddress = '127.0.0.1'
                proxyInstance = $ProxyInstance
                sessionServerId = 'pvp'
            } | ConvertTo-Json -Compress
            $probeResponse = Invoke-RestMethod `
                -Uri $ApiUrl `
                -Method Post `
                -ContentType 'application/json' `
                -Headers @{ 'X-Hechao-Velocity-Token' = $token } `
                -Body $probeBody `
                -TimeoutSec 8
            $apiProbeReason = [string]$probeResponse.reason
            if ($apiProbeReason -ne 'PlayerNotLinked') {
                throw "Unexpected staging API probe result: $apiProbeReason"
            }
        }
    }

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $listeners = @(Get-PortListeners -Port $StagingPort)
    return [pscustomobject]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        JarInstalled = $jarInstalled
        JarHashValid = $jarHashValid
        ConfigInstalled = $configInstalled
        ConfigurationValid = $configurationValid
        TokenConfigured = $tokenConfigured
        TokenDisclosed = $false
        ApiUrl = $ApiUrl
        Mode = 'monitor'
        ApiProbePerformed = [bool]$ProbeApi
        ApiProbeReason = $apiProbeReason
        TaskState = if ($null -eq $task) { 'Missing' } else { [string]$task.State }
        ListenerCount = $listeners.Count
        ProductionProcessId = $Production.ProcessId
        ProductionConfigSha256 = $Production.ConfigSha256
        ProductionEnabledViaJarCount = $Production.EnabledViaJarCount
    }
}

function New-Backup {
    param(
        [Parameter(Mandatory = $true)][string]$JarPath,
        [Parameter(Mandatory = $true)][string]$ConfigDirectory
    )

    if (-not (Test-Path -LiteralPath $BackupRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    }
    $backupDirectory = Join-Path $BackupRoot (
        'PvpReturnAuthorizationStaging-' +
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

    if (Test-Path -LiteralPath $JarPath -PathType Leaf) {
        Copy-Item -LiteralPath $JarPath -Destination $backupDirectory
    }
    if (Test-Path -LiteralPath $ConfigDirectory -PathType Container) {
        Copy-Item `
            -LiteralPath $ConfigDirectory `
            -Destination (Join-Path $backupDirectory 'hechao-velocity-authorizer') `
            -Recurse
    }
    [IO.File]::WriteAllText(
        (Join-Path $backupDirectory 'scope.txt'),
        "Isolated PVP return authorizer staging only.`n",
        [Text.UTF8Encoding]::new($false))

    $currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $currentUserGrant = "*${currentUserSid}:(F)"
    & icacls.exe $backupDirectory `
        '/inheritance:r' `
        '/grant:r' `
        '*S-1-5-18:(F)' `
        '*S-1-5-32-544:(F)' `
        $currentUserGrant `
        '/t' `
        '/c' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to secure staging authorization backup: $backupDirectory"
    }
    return $backupDirectory
}

$production = Get-NormalizedPath -Path $ProductionRoot
$staging = Get-NormalizedPath -Path $StagingRoot
if ([string]::Equals(
        $production,
        $staging,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Production and staging roots must be different.'
}
if (-not (Test-Path -LiteralPath $production -PathType Container) -or
    -not (Test-Path -LiteralPath $staging -PathType Container)) {
    throw 'Production or staging Velocity root is missing.'
}

$productionAuthorizerJar = Join-Path `
    $production `
    'plugins\HechaoVelocityAuthorizer-0.3.0.jar'
$stagingAuthorizerJar = Assert-ChildPath `
    -Parent $staging `
    -Child (Join-Path $staging 'plugins\HechaoVelocityAuthorizer-0.3.0.jar')
$stagingConfigDirectory = Assert-ChildPath `
    -Parent $staging `
    -Child (Join-Path $staging 'plugins\hechao-velocity-authorizer')
$stagingConfigPath = Assert-ChildPath `
    -Parent $stagingConfigDirectory `
    -Child (Join-Path $stagingConfigDirectory 'config.properties')
$productionBefore = Get-ProductionBaseline `
    -Production $production `
    -AuthorizerJar $productionAuthorizerJar

switch ($Action) {
    'Install' {
        Assert-StagingStopped
        $token = [Console]::In.ReadToEnd().Trim()
        if ($token -cnotmatch '^[A-Za-z0-9._~-]{24,256}$') {
            throw (
                'A 24 to 256 character ASCII staging token must be supplied ' +
                'through standard input.')
        }

        $backupDirectory = New-Backup `
            -JarPath $stagingAuthorizerJar `
            -ConfigDirectory $stagingConfigDirectory
        if (-not $PSCmdlet.ShouldProcess(
                $staging,
                'Install isolated Hechao Velocity authorization')) {
            return
        }

        Copy-Item `
            -LiteralPath $productionAuthorizerJar `
            -Destination $stagingAuthorizerJar `
            -Force
        Assert-FileHash `
            -Path $stagingAuthorizerJar `
            -ExpectedSha256 $ExpectedAuthorizerSha256 `
            -Label 'Staging Hechao authorizer JAR' | Out-Null

        New-Item `
            -ItemType Directory `
            -Path $stagingConfigDirectory `
            -Force | Out-Null
        $configuration = @"
mode=monitor
api-url=$ApiUrl
token=$token
proxy-instance=$ProxyInstance
request-timeout-millis=5000
"@
        $temporaryConfig = "$stagingConfigPath.new"
        [IO.File]::WriteAllText(
            $temporaryConfig,
            $configuration,
            [Text.UTF8Encoding]::new($false))
        Move-Item `
            -LiteralPath $temporaryConfig `
            -Destination $stagingConfigPath `
            -Force

        $currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $currentUserGrant = "*${currentUserSid}:(F)"
        foreach ($path in @($stagingConfigDirectory, $stagingConfigPath)) {
            & icacls.exe $path `
                '/inheritance:r' `
                '/grant:r' `
                '*S-1-5-18:(F)' `
                '*S-1-5-32-544:(F)' `
                $currentUserGrant | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to secure staging authorization path: $path"
            }
        }

        $status = Get-AuthorizationStatus `
            -JarPath $stagingAuthorizerJar `
            -ConfigPath $stagingConfigPath `
            -Production $productionBefore
        if (-not $status.JarHashValid -or -not $status.ConfigurationValid) {
            throw 'Staging authorization verification failed.'
        }
        $productionAfter = Get-ProductionBaseline `
            -Production $production `
            -AuthorizerJar $productionAuthorizerJar
        if ($productionAfter.ProcessId -ne $productionBefore.ProcessId) {
            throw 'Production Velocity process changed during staging authorization install.'
        }

        [pscustomobject]@{
            Installed = $true
            BackupDirectory = $backupDirectory
            TokenConfigured = $true
            TokenDisclosed = $false
            ApiUrl = $ApiUrl
            Mode = 'monitor'
            ProductionUnchanged = $true
        } | ConvertTo-Json
    }
    'Status' {
        Get-AuthorizationStatus `
            -JarPath $stagingAuthorizerJar `
            -ConfigPath $stagingConfigPath `
            -Production $productionBefore `
            -ProbeApi:$ProbeApi |
            ConvertTo-Json
    }
    'Remove' {
        if (-not $ConfirmRemoval) {
            throw 'Remove requires -ConfirmRemoval.'
        }
        Assert-StagingStopped
        $backupDirectory = New-Backup `
            -JarPath $stagingAuthorizerJar `
            -ConfigDirectory $stagingConfigDirectory
        if (-not $PSCmdlet.ShouldProcess(
                $staging,
                'Remove isolated Hechao Velocity authorization')) {
            return
        }

        if (Test-Path -LiteralPath $stagingAuthorizerJar -PathType Leaf) {
            Remove-Item -LiteralPath $stagingAuthorizerJar -Force
        }
        if (Test-Path -LiteralPath $stagingConfigDirectory -PathType Container) {
            $resolvedConfig = (Resolve-Path -LiteralPath $stagingConfigDirectory).Path
            Assert-ChildPath -Parent $staging -Child $resolvedConfig | Out-Null
            Remove-Item -LiteralPath $resolvedConfig -Recurse -Force
        }

        $productionAfter = Get-ProductionBaseline `
            -Production $production `
            -AuthorizerJar $productionAuthorizerJar
        if ($productionAfter.ProcessId -ne $productionBefore.ProcessId) {
            throw 'Production Velocity process changed during staging authorization removal.'
        }
        [pscustomobject]@{
            Removed = $true
            BackupDirectory = $backupDirectory
            ProductionUnchanged = $true
        } | ConvertTo-Json
    }
}
