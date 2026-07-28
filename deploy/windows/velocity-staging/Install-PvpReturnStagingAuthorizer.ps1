[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Status')]
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
    [string]$ExpectedProductionAuthorizerSha256 =
        '289B13472AEAC4073895EF9BE7E630B4B5AACEC48A4D0FD849BBAFE0064E681D',

    [ValidatePattern('^HechaoVelocityAuthorizer-[0-9]+\.[0-9]+\.[0-9]+\.jar$')]
    [string]$CandidateFileName =
        'HechaoVelocityAuthorizer-0.3.1.jar',

    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedCandidateSha256 =
        '2FC06C2DBE6F01AFAC2C5AA016C902A10B4B1675C876C5850630B726BB041E75',

    [string]$CandidateSource =
        'E:\Velocity-PvpReturn-Staging\incoming\HechaoVelocityAuthorizer-0.3.1.jar.upload',

    [string]$BackupRoot = 'E:\manual-backups'
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
    if (-not $childPath.StartsWith(
            $parentPath + [IO.Path]::DirectorySeparatorChar,
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
    param([Parameter(Mandatory = $true)][string]$Production)

    Assert-FileHash `
        -Path (Join-Path $Production 'velocity.toml') `
        -ExpectedSha256 $ExpectedProductionConfigSha256 `
        -Label 'Production Velocity configuration' | Out-Null
    Assert-FileHash `
        -Path (Join-Path $Production 'plugins\HechaoVelocityAuthorizer-0.3.0.jar') `
        -ExpectedSha256 $ExpectedProductionAuthorizerSha256 `
        -Label 'Production Hechao authorizer JAR' | Out-Null

    $listeners = @(Get-PortListeners -Port $ProductionPort)
    if ($listeners.Count -ne 1) {
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
        ProcessId = $listeners[0].OwningProcess
        ConfigSha256 = $ExpectedProductionConfigSha256.ToUpperInvariant()
        AuthorizerSha256 = $ExpectedProductionAuthorizerSha256.ToUpperInvariant()
        EnabledViaJarCount = 0
    }
}

function Assert-StagingStopped {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task -and [string]$task.State -eq 'Running') {
        throw "Staging task $TaskName must be stopped first."
    }
    if (@(Get-PortListeners -Port $StagingPort).Count -ne 0) {
        throw "Staging port $StagingPort must not be listening."
    }
}

function Get-EnabledAuthorizers {
    param([Parameter(Mandatory = $true)][string]$PluginsDirectory)

    return @(
        Get-ChildItem -LiteralPath $PluginsDirectory -File |
            Where-Object {
                $_.Name -match
                    '^HechaoVelocityAuthorizer-[0-9]+\.[0-9]+\.[0-9]+\.jar$'
            }
    )
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

$pluginsDirectory = Assert-ChildPath `
    -Parent $staging `
    -Child (Join-Path $staging 'plugins')
$incomingDirectory = Assert-ChildPath `
    -Parent $staging `
    -Child (Join-Path $staging 'incoming')
$candidateSourcePath = Assert-ChildPath `
    -Parent $incomingDirectory `
    -Child $CandidateSource
$candidateTarget = Assert-ChildPath `
    -Parent $pluginsDirectory `
    -Child (Join-Path $pluginsDirectory $CandidateFileName)
$temporaryTarget = Assert-ChildPath `
    -Parent $pluginsDirectory `
    -Child "$candidateTarget.new"
$productionBefore = Get-ProductionBaseline -Production $production

switch ($Action) {
    'Status' {
        $enabledAuthorizers = @(Get-EnabledAuthorizers -PluginsDirectory $pluginsDirectory)
        $candidateInstalled =
            Test-Path -LiteralPath $candidateTarget -PathType Leaf
        $candidateHashValid =
            $candidateInstalled -and
            (Get-FileHash -LiteralPath $candidateTarget -Algorithm SHA256).Hash -eq
                $ExpectedCandidateSha256.ToUpperInvariant()
        [pscustomobject]@{
            CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            CandidateFileName = $CandidateFileName
            CandidateInstalled = $candidateInstalled
            CandidateHashValid = $candidateHashValid
            EnabledAuthorizerJarCount = $enabledAuthorizers.Count
            EnabledAuthorizerJarNames = @($enabledAuthorizers.Name)
            StagingListenerCount = @(Get-PortListeners -Port $StagingPort).Count
            ProductionProcessId = $productionBefore.ProcessId
            ProductionConfigSha256 = $productionBefore.ConfigSha256
            ProductionAuthorizerSha256 = $productionBefore.AuthorizerSha256
            ProductionEnabledViaJarCount = 0
        } | ConvertTo-Json
    }
    'Install' {
        Assert-StagingStopped
        Assert-FileHash `
            -Path $candidateSourcePath `
            -ExpectedSha256 $ExpectedCandidateSha256 `
            -Label 'Staging authorizer candidate upload' | Out-Null

        if (-not (Test-Path -LiteralPath $BackupRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
        }
        $backupDirectory = Join-Path $BackupRoot (
            'PvpReturnAuthorizerStaging-' +
            [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        $previousAuthorizers = @(
            Get-EnabledAuthorizers -PluginsDirectory $pluginsDirectory)
        foreach ($jar in $previousAuthorizers) {
            Copy-Item -LiteralPath $jar.FullName -Destination $backupDirectory
        }
        [IO.File]::WriteAllText(
            (Join-Path $backupDirectory 'scope.txt'),
            "Isolated PVP return authorizer candidate only.`n",
            [Text.UTF8Encoding]::new($false))

        if (-not $PSCmdlet.ShouldProcess(
                $staging,
                "Install isolated authorizer candidate $CandidateFileName")) {
            return
        }

        try {
            Copy-Item `
                -LiteralPath $candidateSourcePath `
                -Destination $temporaryTarget `
                -Force
            Assert-FileHash `
                -Path $temporaryTarget `
                -ExpectedSha256 $ExpectedCandidateSha256 `
                -Label 'Temporary staging authorizer candidate' | Out-Null

            foreach ($jar in $previousAuthorizers) {
                $safeJar = Assert-ChildPath `
                    -Parent $pluginsDirectory `
                    -Child $jar.FullName
                Remove-Item -LiteralPath $safeJar -Force
            }
            Move-Item `
                -LiteralPath $temporaryTarget `
                -Destination $candidateTarget `
                -Force

            $installedAuthorizers = @(
                Get-EnabledAuthorizers -PluginsDirectory $pluginsDirectory)
            if ($installedAuthorizers.Count -ne 1 -or
                $installedAuthorizers[0].FullName -ne $candidateTarget) {
                throw 'The staging proxy does not have exactly one candidate authorizer.'
            }
            Assert-FileHash `
                -Path $candidateTarget `
                -ExpectedSha256 $ExpectedCandidateSha256 `
                -Label 'Installed staging authorizer candidate' | Out-Null
        }
        catch {
            if (Test-Path -LiteralPath $temporaryTarget -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryTarget -Force
            }
            if (Test-Path -LiteralPath $candidateTarget -PathType Leaf) {
                Remove-Item -LiteralPath $candidateTarget -Force
            }
            foreach ($jar in $previousAuthorizers) {
                $backupJar = Join-Path $backupDirectory $jar.Name
                if (Test-Path -LiteralPath $backupJar -PathType Leaf) {
                    Copy-Item `
                        -LiteralPath $backupJar `
                        -Destination $jar.FullName `
                        -Force
                }
            }
            throw
        }

        Assert-StagingStopped
        $productionAfter = Get-ProductionBaseline -Production $production
        if ($productionAfter.ProcessId -ne $productionBefore.ProcessId) {
            throw 'Production Velocity process changed during candidate install.'
        }
        [pscustomobject]@{
            Installed = $true
            CandidateFileName = $CandidateFileName
            CandidateSha256 = $ExpectedCandidateSha256.ToUpperInvariant()
            EnabledAuthorizerJarCount = 1
            BackupDirectory = $backupDirectory
            StagingStarted = $false
            ProductionUnchanged = $true
        } | ConvertTo-Json
    }
}
