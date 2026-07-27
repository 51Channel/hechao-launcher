[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentJar,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory)]
    [ValidateSet('Fabric', 'NeoForge')]
    [string]$Loader,

    [Parameter(Mandatory)]
    [string]$ServerDirectory,

    [Parameter(Mandatory)]
    [ValidateRange(1, 65535)]
    [int]$ServerPort,

    [Parameter(Mandatory)]
    [string]$BackupRoot,

    [switch]$RequireNoJavaProcess
)

$ErrorActionPreference = 'Stop'

function Set-RestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$LiteralPath)

    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().
        User.Value
    foreach ($sidValue in @(
            'S-1-5-18',
            'S-1-5-32-544',
            $currentSid
        ) | Select-Object -Unique) {
        $sid = [System.Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
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

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory)]
        [string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "The staged JAR does not contain $EntryName."
    }

    $reader = [System.IO.StreamReader]::new(
        $entry.Open(),
        [System.Text.UTF8Encoding]::new($false, $true),
        $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

$source = (Resolve-Path -LiteralPath $AgentJar).Path
$server = (Resolve-Path -LiteralPath $ServerDirectory).Path
$modsDirectory = Join-Path $server 'mods'
if (-not (Test-Path -LiteralPath $modsDirectory -PathType Container)) {
    throw "The server mods directory is missing: $modsDirectory"
}

$serverProperties = Join-Path $server 'server.properties'
if (-not (Test-Path -LiteralPath $serverProperties -PathType Leaf)) {
    throw "The server properties file is missing: $serverProperties"
}
$configuredPortLine = [System.IO.File]::ReadAllLines($serverProperties) |
    Where-Object { $_ -match '^server-port=' } |
    Select-Object -First 1
if ($null -eq $configuredPortLine -or
    [int]$configuredPortLine.Split('=', 2)[1] -ne $ServerPort) {
    throw "server.properties does not declare the reviewed port $ServerPort."
}

$listening = [bool](
    Get-NetTCPConnection -LocalPort $ServerPort -State Listen `
        -ErrorAction SilentlyContinue
)
if ($listening) {
    throw "The target server port $ServerPort is still listening."
}

$javaProcesses = @(
    Get-CimInstance Win32_Process -Filter "Name='java.exe'" `
        -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty ProcessId
)
if ($RequireNoJavaProcess -and $javaProcesses.Count -ne 0) {
    throw 'The deployment requires all Java processes to be stopped.'
}

$expectedUpper = $ExpectedSha256.ToUpperInvariant()
$actualSha256 = (
    Get-FileHash -LiteralPath $source -Algorithm SHA256
).Hash
if ($actualSha256 -ne $expectedUpper) {
    throw "Agent JAR SHA-256 mismatch. Expected $expectedUpper, got $actualSha256."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($source)
try {
    if ($Loader -eq 'Fabric') {
        $descriptor = (
            Read-ZipEntryText -Archive $archive -EntryName 'fabric.mod.json'
        ) | ConvertFrom-Json
        if ($descriptor.id -ne 'hechao_server_metrics' -or
            $descriptor.version -ne '0.1.0' -or
            $descriptor.environment -ne 'server' -or
            $descriptor.depends.minecraft -ne '~1.20.1') {
            throw 'The staged JAR is not the reviewed Fabric 1.20.1 metrics agent.'
        }
        $destinationName =
            'HechaoServerMetrics-Fabric-1.20.1-0.1.0.jar'
    }
    else {
        $descriptor = Read-ZipEntryText `
            -Archive $archive `
            -EntryName 'META-INF/neoforge.mods.toml'
        if ($descriptor -notmatch '(?m)^modId\s*=\s*"hechao_server_metrics"\s*$' -or
            $descriptor -notmatch '(?m)^version\s*=\s*"0\.1\.0"\s*$' -or
            $descriptor -notmatch '(?m)^versionRange\s*=\s*"\[21\.11\.42,21\.12\)"\s*$' -or
            $descriptor -notmatch '(?m)^versionRange\s*=\s*"\[1\.21\.11,1\.22\)"\s*$') {
            throw 'The staged JAR is not the reviewed NeoForge 1.21.11 metrics agent.'
        }
        $destinationName =
            'HechaoServerMetrics-NeoForge-1.21.11-0.1.0.jar'
    }
}
finally {
    $archive.Dispose()
}

$destination = Join-Path $modsDirectory $destinationName
$enabledAgents = @(
    Get-ChildItem -LiteralPath $modsDirectory -File |
        Where-Object {
            $_.Name -like 'HechaoServerMetrics-*.jar'
        }
)
if ($enabledAgents.Count -eq 1 -and
    $enabledAgents[0].FullName -eq $destination -and
    (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq
        $actualSha256) {
    [ordered]@{
        deployment = 'unchanged'
        destination = $destination
        sha256 = $actualSha256
        targetPortListening = $false
        serverRestart = 'not_performed'
    } | ConvertTo-Json -Compress
    return
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
[System.IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
$backupDirectory = Join-Path `
    ([System.IO.Path]::GetFullPath($BackupRoot)) `
    "mod-server-metrics-$timestamp"
[System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null

$staged = Join-Path $modsDirectory (
    '.hechao-server-metrics-' + [guid]::NewGuid().ToString('N') + '.tmp'
)
Copy-Item -LiteralPath $source -Destination $staged
if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne
    $actualSha256) {
    Remove-Item -LiteralPath $staged -Force
    throw 'The staged server-side JAR failed SHA-256 verification.'
}

$movedAgents = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($agent in $enabledAgents) {
        $backupPath = Join-Path $backupDirectory $agent.Name
        Move-Item -LiteralPath $agent.FullName -Destination $backupPath
        $movedAgents.Add([pscustomobject]@{
            Original = $agent.FullName
            Backup = $backupPath
        })
    }

    Move-Item -LiteralPath $staged -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne
        $actualSha256) {
        throw 'The deployed JAR failed SHA-256 verification.'
    }

    $deploymentRecord = [ordered]@{
        deployedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        loader = $Loader
        serverDirectory = $server
        serverPort = $ServerPort
        sourceSha256 = $actualSha256
        destination = $destination
        replacedAgents = @($movedAgents | ForEach-Object {
            [ordered]@{
                name = Split-Path -Leaf $_.Original
                backup = $_.Backup
            }
        })
        targetPortListeningBefore = $false
        javaProcessCountObserved = $javaProcesses.Count
        requireNoJavaProcess = [bool]$RequireNoJavaProcess
        serverRestart = 'not_performed'
    }
    $recordPath = Join-Path $backupDirectory 'deployment.json'
    [System.IO.File]::WriteAllText(
        $recordPath,
        ($deploymentRecord | ConvertTo-Json -Depth 5),
        [System.Text.UTF8Encoding]::new($false))
    Set-RestrictedDirectoryAcl -LiteralPath $backupDirectory
}
catch {
    if (Test-Path -LiteralPath $staged) {
        Remove-Item -LiteralPath $staged -Force
    }
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Force
    }
    foreach ($moved in $movedAgents) {
        if (Test-Path -LiteralPath $moved.Backup) {
            Move-Item -LiteralPath $moved.Backup -Destination $moved.Original
        }
    }
    throw
}

[ordered]@{
    deployment = 'completed'
    destination = $destination
    backup = $backupDirectory
    sha256 = $actualSha256
    targetPortListening = $false
    serverRestart = 'not_performed'
} | ConvertTo-Json -Compress
