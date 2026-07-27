[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$StagedJar,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedJarSha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedForwardingSecretSha256,

    [string]$ServerRoot = 'C:\mc\server',
    [string]$BackupRoot = 'C:\manual-backups',
    [string]$StartupTaskName = 'HorrorPrank',
    [switch]$ReadBase64SecretFromStandardInput,
    [switch]$ReuseMatchingExistingConfig
)

$ErrorActionPreference = 'Stop'

function Get-BytesSha256 {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($sha256.ComputeHash($Bytes) |
            ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
}

if (-not $ReadBase64SecretFromStandardInput) {
    throw 'The forwarding secret must be supplied as Base64 through standard input.'
}

$sourceJar = (Resolve-Path -LiteralPath $StagedJar).Path
$resolvedServerRoot = (Resolve-Path -LiteralPath $ServerRoot).Path
$modsDirectory = Join-Path $resolvedServerRoot 'mods'
$configDirectory = Join-Path $resolvedServerRoot 'config'
$serverProperties = Join-Path $resolvedServerRoot 'server.properties'
$startScript = Join-Path $resolvedServerRoot 'start-headless.bat'

foreach ($requiredPath in @(
        $modsDirectory,
        $configDirectory,
        $serverProperties,
        $startScript
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required server path is missing: $requiredPath"
    }
}

$actualJarSha256 = (Get-FileHash -LiteralPath $sourceJar -Algorithm SHA256).Hash
if ($actualJarSha256 -ne $ExpectedJarSha256.ToUpperInvariant()) {
    throw "FabricProxy-Lite JAR SHA-256 mismatch. Expected $ExpectedJarSha256, got $actualJarSha256."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($sourceJar)
try {
    $descriptorEntry = $archive.GetEntry('fabric.mod.json')
    if ($null -eq $descriptorEntry) {
        throw 'The staged JAR does not contain fabric.mod.json.'
    }

    $descriptorReader = [System.IO.StreamReader]::new($descriptorEntry.Open())
    try {
        $descriptor = $descriptorReader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $descriptorReader.Dispose()
    }

    if ($descriptor.id -ne 'fabricproxy-lite' -or
        $descriptor.version -ne '2.6.0' -or
        $descriptor.environment -ne 'server') {
        throw 'The staged JAR is not the reviewed FabricProxy-Lite 2.6.0 server mod.'
    }
}
finally {
    $archive.Dispose()
}

$reader = [System.IO.StreamReader]::new(
    [Console]::OpenStandardInput(),
    [System.Text.UTF8Encoding]::new($false, $true),
    $true)
try {
    $encodedSecret = $reader.ReadToEnd().Trim()
}
finally {
    $reader.Dispose()
}

if ([string]::IsNullOrWhiteSpace($encodedSecret)) {
    throw 'The forwarding secret was not supplied through standard input.'
}

$secretBytes = [Convert]::FromBase64String($encodedSecret)
$encodedSecret = $null
$createdTargetJar = $false
$createdTargetConfig = $false
$configAclChanged = $false
try {
    $secret = [System.Text.UTF8Encoding]::new($false, $true).
        GetString($secretBytes)
    if ($secret -cnotmatch '^[A-Za-z0-9_-]{32,128}$') {
        throw 'The forwarding secret must be 32-128 URL-safe characters.'
    }

    $secretSha256 = Get-BytesSha256 -Bytes $secretBytes
    if ($secretSha256 -ne $ExpectedForwardingSecretSha256.ToUpperInvariant()) {
        throw 'The forwarding secret does not match the reviewed Velocity secret.'
    }

    $serverPropertyLines = [System.IO.File]::ReadAllLines($serverProperties)
    $serverPortLine = $serverPropertyLines |
        Where-Object { $_ -match '^server-port=' } |
        Select-Object -First 1
    $onlineModeLine = $serverPropertyLines |
        Where-Object { $_ -match '^online-mode=' } |
        Select-Object -First 1
    if ($null -eq $serverPortLine -or $null -eq $onlineModeLine) {
        throw 'server.properties is missing server-port or online-mode.'
    }

    $serverPort = [int]$serverPortLine.Split('=', 2)[1]
    $onlineMode = $onlineModeLine.Split('=', 2)[1]
    if ($onlineMode -ne 'true') {
        throw 'This deployment expects online-mode=true with FabricProxy-Lite hackOnlineMode.'
    }

    $javaBefore = @(
        Get-CimInstance Win32_Process -Filter "Name='java.exe'" `
            -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty ProcessId
    )
    $portBefore = [bool](
        Get-NetTCPConnection -LocalPort $serverPort -State Listen `
            -ErrorAction SilentlyContinue
    )
    if ($javaBefore.Count -ne 0 -or $portBefore) {
        throw 'The PVP server must be fully stopped before deployment.'
    }

    $targetJar = Join-Path $modsDirectory 'FabricProxy-Lite-2.6.0.jar'
    $targetConfig = Join-Path $configDirectory 'FabricProxy-Lite.toml'
    if (Test-Path -LiteralPath $targetJar) {
        throw "The target JAR already exists: $targetJar"
    }
    $configExisted = Test-Path -LiteralPath $targetConfig -PathType Leaf
    if ($configExisted -and -not $ReuseMatchingExistingConfig) {
        throw "The target configuration already exists: $targetConfig"
    }

    $existingConfigAcl = $null
    $existingConfigSha256 = $null
    $existingConfigAclSddl = $null
    $existingHackEarlySend = $null
    if ($configExisted) {
        $existingConfigText = [System.IO.File]::ReadAllText($targetConfig)
        $existingSecretMatch = [regex]::Match(
            $existingConfigText,
            '(?m)^secret\s*=\s*"([A-Za-z0-9_-]+)"\s*$')
        $existingHackOnlineMode = [regex]::Match(
            $existingConfigText,
            '(?m)^hackOnlineMode\s*=\s*(true|false)\s*$')
        $existingHackEarlySendMatch = [regex]::Match(
            $existingConfigText,
            '(?m)^hackEarlySend\s*=\s*(true|false)\s*$')
        $existingHackMessageChain = [regex]::Match(
            $existingConfigText,
            '(?m)^hackMessageChain\s*=\s*(true|false)\s*$')
        if (-not $existingSecretMatch.Success -or
            -not $existingHackOnlineMode.Success -or
            -not $existingHackEarlySendMatch.Success -or
            -not $existingHackMessageChain.Success) {
            throw 'The existing FabricProxy-Lite configuration is incomplete.'
        }
        if ($existingHackOnlineMode.Groups[1].Value -ne 'true' -or
            $existingHackMessageChain.Groups[1].Value -ne 'true') {
            throw 'The existing FabricProxy-Lite safety settings are not compatible.'
        }

        $existingSecretBytes = [System.Text.Encoding]::UTF8.GetBytes(
            $existingSecretMatch.Groups[1].Value)
        try {
            $existingSecretSha256 = Get-BytesSha256 `
                -Bytes $existingSecretBytes
        }
        finally {
            [Array]::Clear(
                $existingSecretBytes,
                0,
                $existingSecretBytes.Length)
        }
        if ($existingSecretSha256 -ne $secretSha256) {
            throw 'The existing FabricProxy-Lite secret does not match Velocity.'
        }

        $existingHackEarlySend = (
            $existingHackEarlySendMatch.Groups[1].Value -eq 'true'
        )
        $existingConfigSha256 = (
            Get-FileHash -LiteralPath $targetConfig -Algorithm SHA256
        ).Hash
        $existingConfigAcl = Get-Acl -LiteralPath $targetConfig
        $existingConfigAclSddl = $existingConfigAcl.Sddl
        $existingConfigText = $null
    }

    $timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    [System.IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
    $backupDirectory = Join-Path $BackupRoot "pvp-velocity-modern-$timestamp"
    [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null

    $backupAcl = [System.Security.AccessControl.DirectorySecurity]::new()
    $backupAcl.SetAccessRuleProtection($true, $false)
    foreach ($sidValue in @('S-1-5-18', 'S-1-5-32-544')) {
        $sid = [System.Security.Principal.SecurityIdentifier]::new(
            $sidValue)
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            (
                [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            ),
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $backupAcl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $backupDirectory -AclObject $backupAcl

    Copy-Item -LiteralPath $serverProperties `
        -Destination (Join-Path $backupDirectory 'server.properties')
    Copy-Item -LiteralPath $startScript `
        -Destination (Join-Path $backupDirectory 'start-headless.bat')
    if ($configExisted) {
        Copy-Item -LiteralPath $targetConfig `
            -Destination (Join-Path $backupDirectory 'FabricProxy-Lite.toml')
    }

    $taskXml = Export-ScheduledTask -TaskName $StartupTaskName
    $taskXmlPath = Join-Path $backupDirectory "$StartupTaskName.xml"
    [System.IO.File]::WriteAllText(
        $taskXmlPath,
        $taskXml,
        [System.Text.UTF8Encoding]::new($false))

    $prechange = [ordered]@{
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        serverPropertiesSha256 = (
            Get-FileHash -LiteralPath $serverProperties -Algorithm SHA256
        ).Hash
        startScriptSha256 = (
            Get-FileHash -LiteralPath $startScript -Algorithm SHA256
        ).Hash
        startupTaskXmlSha256 = (
            Get-FileHash -LiteralPath $taskXmlPath -Algorithm SHA256
        ).Hash
        mods = @(
            Get-ChildItem -LiteralPath $modsDirectory -Filter '*.jar' -File |
                Sort-Object Name |
                ForEach-Object {
                    [ordered]@{
                        name = $_.Name
                        length = $_.Length
                        sha256 = (
                            Get-FileHash -LiteralPath $_.FullName `
                                -Algorithm SHA256
                        ).Hash
                    }
                }
        )
        forwardingSecretSha256 = $secretSha256
        existingConfig = [ordered]@{
            existed = $configExisted
            sha256 = $existingConfigSha256
            aclSddl = $existingConfigAclSddl
            hackEarlySend = $existingHackEarlySend
        }
        javaPids = $javaBefore
        portListening = $portBefore
    }
    $prechangePath = Join-Path $backupDirectory 'prechange.json'
    [System.IO.File]::WriteAllText(
        $prechangePath,
        ($prechange | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    $incomingJar = Join-Path $modsDirectory `
        ".FabricProxy-Lite-2.6.0.jar.incoming-$timestamp"
    $incomingConfig = Join-Path $configDirectory `
        ".FabricProxy-Lite.toml.incoming-$timestamp"

    try {
        Copy-Item -LiteralPath $sourceJar -Destination $incomingJar
        $incomingJarSha256 = (
            Get-FileHash -LiteralPath $incomingJar -Algorithm SHA256
        ).Hash
        if ($incomingJarSha256 -ne $actualJarSha256) {
            throw 'The copied JAR failed SHA-256 verification.'
        }

        $configAcl = [System.Security.AccessControl.FileSecurity]::new()
        $configAcl.SetAccessRuleProtection($true, $false)
        foreach ($sidValue in @('S-1-5-18', 'S-1-5-32-544')) {
            $sid = [System.Security.Principal.SecurityIdentifier]::new(
                $sidValue)
            $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                $sid,
                [System.Security.AccessControl.FileSystemRights]::FullControl,
                [System.Security.AccessControl.AccessControlType]::Allow)
            $configAcl.AddAccessRule($rule)
        }

        if ($configExisted) {
            Set-Acl -LiteralPath $targetConfig -AclObject $configAcl
            $configAclChanged = $true
        }
        else {
            $configuration = @(
                'hackOnlineMode = true'
                'hackEarlySend = false'
                'hackMessageChain = true'
                "secret = `"$secret`""
                ''
            ) -join "`r`n"
            [System.IO.File]::WriteAllText(
                $incomingConfig,
                $configuration,
                [System.Text.UTF8Encoding]::new($false))
            Set-Acl -LiteralPath $incomingConfig -AclObject $configAcl
        }

        Move-Item -LiteralPath $incomingJar -Destination $targetJar
        $createdTargetJar = $true
        if (-not $configExisted) {
            Move-Item -LiteralPath $incomingConfig -Destination $targetConfig
            $createdTargetConfig = $true
        }
    }
    catch {
        foreach ($path in @($incomingJar, $incomingConfig)) {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Remove-Item -LiteralPath $path -Force
            }
        }
        if ($createdTargetConfig -and
            (Test-Path -LiteralPath $targetConfig -PathType Leaf)) {
            Remove-Item -LiteralPath $targetConfig -Force
            $createdTargetConfig = $false
        }
        if ($createdTargetJar -and
            (Test-Path -LiteralPath $targetJar -PathType Leaf)) {
            Remove-Item -LiteralPath $targetJar -Force
            $createdTargetJar = $false
        }
        if ($configAclChanged -and $null -ne $existingConfigAcl) {
            Set-Acl -LiteralPath $targetConfig -AclObject $existingConfigAcl
            $configAclChanged = $false
        }
        throw
    }

    $configText = [System.IO.File]::ReadAllText($targetConfig)
    $secretMatch = [regex]::Match(
        $configText,
        '(?m)^secret\s*=\s*"([A-Za-z0-9_-]+)"\s*$')
    if (-not $secretMatch.Success) {
        throw 'The deployed FabricProxy-Lite configuration cannot be parsed.'
    }

    $deployedSecretBytes = [System.Text.Encoding]::UTF8.GetBytes(
        $secretMatch.Groups[1].Value)
    try {
        $deployedSecretSha256 = Get-BytesSha256 -Bytes $deployedSecretBytes
    }
    finally {
        [Array]::Clear(
            $deployedSecretBytes,
            0,
            $deployedSecretBytes.Length)
    }

    $taskXmlAfter = Export-ScheduledTask -TaskName $StartupTaskName
    $taskXmlAfterPath = Join-Path $backupDirectory `
        "$StartupTaskName.after.xml"
    [System.IO.File]::WriteAllText(
        $taskXmlAfterPath,
        $taskXmlAfter,
        [System.Text.UTF8Encoding]::new($false))

    $javaAfter = @(
        Get-CimInstance Win32_Process -Filter "Name='java.exe'" `
            -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty ProcessId
    )
    $portAfter = [bool](
        Get-NetTCPConnection -LocalPort $serverPort -State Listen `
            -ErrorAction SilentlyContinue
    )
    $deployedAcl = Get-Acl -LiteralPath $targetConfig

    $result = [ordered]@{
        backup = $backupDirectory
        backupPrechangeSha256 = (
            Get-FileHash -LiteralPath $prechangePath -Algorithm SHA256
        ).Hash
        modPath = $targetJar
        modLength = (Get-Item -LiteralPath $targetJar).Length
        modSha256 = (
            Get-FileHash -LiteralPath $targetJar -Algorithm SHA256
        ).Hash
        configPath = $targetConfig
        configSha256 = (
            Get-FileHash -LiteralPath $targetConfig -Algorithm SHA256
        ).Hash
        configSecretMatchesVelocity = (
            $deployedSecretSha256 -eq $secretSha256
        )
        configReused = $configExisted
        configContentUnchanged = (
            -not $configExisted -or
            (
                Get-FileHash -LiteralPath $targetConfig -Algorithm SHA256
            ).Hash -eq $existingConfigSha256
        )
        configHackEarlySend = (
            [regex]::Match(
                $configText,
                '(?m)^hackEarlySend\s*=\s*(true|false)\s*$'
            ).Groups[1].Value -eq 'true'
        )
        configAclProtected = $deployedAcl.AreAccessRulesProtected
        configAclIdentities = @(
            $deployedAcl.Access |
                ForEach-Object { $_.IdentityReference.Value } |
                Sort-Object -Unique
        )
        onlineMode = $onlineMode
        serverPropertiesUnchanged = (
            (
                Get-FileHash -LiteralPath $serverProperties `
                    -Algorithm SHA256
            ).Hash -eq $prechange.serverPropertiesSha256
        )
        startScriptUnchanged = (
            (
                Get-FileHash -LiteralPath $startScript -Algorithm SHA256
            ).Hash -eq $prechange.startScriptSha256
        )
        startupTaskDefinitionUnchanged = (
            (
                Get-FileHash -LiteralPath $taskXmlAfterPath `
                    -Algorithm SHA256
            ).Hash -eq $prechange.startupTaskXmlSha256
        )
        javaBefore = $javaBefore
        javaAfter = $javaAfter
        portListeningBefore = $portBefore
        portListeningAfter = $portAfter
        serverRestartPerformed = $false
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $backupDirectory 'deployment.json'),
        ($result | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    $result | ConvertTo-Json -Depth 6 -Compress
}
catch {
    if ($createdTargetConfig -and
        (Test-Path -LiteralPath $targetConfig -PathType Leaf)) {
        Remove-Item -LiteralPath $targetConfig -Force
    }
    if ($createdTargetJar -and
        (Test-Path -LiteralPath $targetJar -PathType Leaf)) {
        Remove-Item -LiteralPath $targetJar -Force
    }
    if ($configAclChanged -and $null -ne $existingConfigAcl -and
        (Test-Path -LiteralPath $targetConfig -PathType Leaf)) {
        Set-Acl -LiteralPath $targetConfig -AclObject $existingConfigAcl
    }
    throw
}
finally {
    if ($null -ne $secretBytes) {
        [Array]::Clear($secretBytes, 0, $secretBytes.Length)
    }
    $secret = $null
}
