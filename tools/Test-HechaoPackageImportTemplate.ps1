[CmdletBinding()]
param(
    [string] $HandoffArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or later is required. Run this script with pwsh."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$handoffRoot = Join-Path $repositoryRoot "handoff\package-import-template"
$validator = Join-Path $PSScriptRoot "Test-HechaoPackageImportSource.ps1"
$builder = Join-Path $PSScriptRoot "New-HechaoPackageImportArchive.ps1"
$handoffBuilder = Join-Path $PSScriptRoot "New-HechaoPackageImportTemplateHandoff.ps1"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "hechao-package-template-test-" + [Guid]::NewGuid().ToString("N"))

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool] $Condition,
        [Parameter(Mandatory = $true)][string] $Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Value
    )
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($Path)) -Force |
        Out-Null
    [System.IO.File]::WriteAllText(
        $Path,
        $Value,
        [System.Text.UTF8Encoding]::new($false))
}

function Write-FixtureBytes {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Value
    )
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($Path)) -Force |
        Out-Null
    [System.IO.File]::WriteAllBytes(
        $Path,
        [System.Text.Encoding]::UTF8.GetBytes($Value))
}

function Test-HandoffIntegrity {
    param([Parameter(Mandatory = $true)][string] $ArchivePath)

    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $extractRoot = Join-Path $temporaryRoot "handoff-extracted"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($resolvedArchive, $extractRoot)
    $requiredPaths = @(
        "README.md",
        "AGENTS.md",
        "00-从这里开始.md",
        "01-给Codex的首条消息.md",
        "02-标准上传包格式.md",
        "03-客户端制作规范.md",
        "04-服务端制作规范.md",
        "05-导入与企划流程.md",
        "06-最终交付清单.md",
        "tools/Test-HechaoPackageImportSource.ps1",
        "tools/New-HechaoPackageImportArchive.ps1",
        "reference/platform-docs/PACKAGE_IMPORT_OPERATIONS.md",
        "reference/platform-docs/ACTIVITY_PLAN_OPERATIONS.md",
        "reference/source-contract/Hechao.Modpack/ModpackArchiveAnalyzer.cs",
        "SOURCE-SNAPSHOT.json",
        "MANIFEST.json",
        "SHA256SUMS"
    )
    foreach ($relativePath in $requiredPaths) {
        Assert-True `
            (Test-Path -LiteralPath (Join-Path $extractRoot $relativePath) -PathType Leaf) `
            "Handoff archive is missing $relativePath"
    }

    $sumPath = Join-Path $extractRoot "SHA256SUMS"
    $sumEntries = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($sumPath)) {
        if ($line -notmatch '^(?<sha>[0-9a-f]{64}) \*(?<path>.+)$') {
            throw "Invalid SHA256SUMS line: $line"
        }
        if ($sumEntries.ContainsKey($Matches.path)) {
            throw "Duplicate SHA256SUMS path: $($Matches.path)"
        }
        $sumEntries[$Matches.path] = $Matches.sha
        $filePath = Join-Path $extractRoot $Matches.path
        Assert-True (Test-Path -LiteralPath $filePath -PathType Leaf) "SHA256SUMS path is missing: $($Matches.path)"
        $digest = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($digest -ceq $Matches.sha) "SHA256SUMS mismatch: $($Matches.path)"
    }

    $filesExceptSums = @(Get-ChildItem -LiteralPath $extractRoot -File -Recurse -Force |
        Where-Object Name -ne "SHA256SUMS")
    Assert-True ($sumEntries.Count -eq $filesExceptSums.Count) "SHA256SUMS does not cover the full handoff."

    $manifest = Get-Content -LiteralPath (Join-Path $extractRoot "MANIFEST.json") -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-True ($manifest.schemaVersion -eq 1) "Handoff MANIFEST.json schema is invalid."
    Assert-True ($manifest.packageKind -eq "hechao-package-import-template-handoff") "Handoff MANIFEST.json kind is invalid."
    $manifestPaths = @($manifest.entries | ForEach-Object { [string] $_.path })
    Assert-True ($manifestPaths.Count -eq $manifest.totalFiles) "Handoff MANIFEST.json count is invalid."
    foreach ($entry in $manifest.entries) {
        $filePath = Join-Path $extractRoot ([string] $entry.path)
        Assert-True (Test-Path -LiteralPath $filePath -PathType Leaf) "Manifest path is missing: $($entry.path)"
        $file = Get-Item -LiteralPath $filePath
        Assert-True ($file.Length -eq [long] $entry.bytes) "Manifest length mismatch: $($entry.path)"
        $digest = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($digest -ceq [string] $entry.sha256) "Manifest digest mismatch: $($entry.path)"
    }

    $expectedManifestPaths = @(Get-ChildItem -LiteralPath $extractRoot -File -Recurse -Force |
        Where-Object { $_.Name -notin @("MANIFEST.json", "SHA256SUMS") } |
        ForEach-Object {
            [System.IO.Path]::GetRelativePath($extractRoot, $_.FullName).Replace("\", "/")
        })
    Assert-True ($manifestPaths.Count -eq $expectedManifestPaths.Count) "Manifest does not cover the expected file set."
    foreach ($path in $expectedManifestPaths) {
        Assert-True ($path -cin $manifestPaths) "Manifest omits $path"
    }
}

try {
    Add-Type -AssemblyName System.IO.Compression
    Assert-True (Test-Path -LiteralPath $handoffRoot -PathType Container) "Handoff source directory is missing."
    foreach ($scriptPath in @($validator, $builder, $handoffBuilder, $PSCommandPath)) {
        Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) "Required script is missing: $scriptPath"
        [void] [scriptblock]::Create([System.IO.File]::ReadAllText($scriptPath))
    }
    foreach ($jsonPath in @(Get-ChildItem -LiteralPath $handoffRoot -Filter "*.json*" -File -Recurse -Force)) {
        $text = [System.IO.File]::ReadAllText($jsonPath.FullName)
        $options = [System.Text.Json.JsonDocumentOptions]::new()
        $options.AllowTrailingCommas = $false
        $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
        $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
        $document.Dispose()
    }
    foreach ($startExample in @(Get-ChildItem -LiteralPath (Join-Path $handoffRoot "templates\start-scripts") -File)) {
        $text = [System.IO.File]::ReadAllText($startExample.FullName)
        Assert-True `
            ($text -match '(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*\r?$') `
            "Start script example lacks the managed guard: $($startExample.Name)"
        Assert-True ($text -match '(?i)user_jvm_args\.txt') "Start script example lacks user_jvm_args.txt: $($startExample.Name)"
    }

    $fixture = Join-Path $temporaryRoot "valid-source"
    $versionId = "1.20.1-Fabric_0.15.11"
    Write-Utf8Text (Join-Path $fixture "hechao-pack.json") @'
{
  "schemaVersion": 1,
  "id": "activity-contract-fixture-fabric-1.20.1",
  "displayName": "Contract Fixture",
  "version": "1.0.0",
  "minecraftVersion": "1.20.1",
  "javaMajorVersion": 17,
  "loader": "Fabric",
  "loaderVersion": "0.15.11",
  "clientRoot": "client",
  "serverRoot": "server",
  "sharedRoot": "shared"
}
'@
    Write-Utf8Text (Join-Path $fixture "client\hechao-profile.json") @'
{
  "schemaVersion": 1,
  "versionId": "1.20.1-Fabric_0.15.11",
  "javaMajorVersion": 17
}
'@
    Write-Utf8Text (Join-Path $fixture "client\versions\$versionId\$versionId.json") @'
{
  "id": "1.20.1-Fabric_0.15.11",
  "javaVersion": { "majorVersion": 17 },
  "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient"
}
'@
    Write-FixtureBytes (Join-Path $fixture "client\versions\$versionId\$versionId.jar") "fixture-version-jar"
    Write-Utf8Text (Join-Path $fixture "client\assets\indexes\1.20.json") '{ "objects": {} }'
    Write-FixtureBytes (Join-Path $fixture "client\assets\objects\00\0000000000000000000000000000000000000000") "fixture-asset"
    Write-FixtureBytes (Join-Path $fixture "client\libraries\fixture\library.jar") "fixture-library"
    Write-FixtureBytes (Join-Path $fixture "client\mods\client-only.jar") "fixture-client-mod"
    Write-Utf8Text (Join-Path $fixture "server\server.properties") @'
server-ip=127.0.0.1
server-port=25568
online-mode=false
max-players=20
enable-rcon=false
'@
    Write-Utf8Text (Join-Path $fixture "server\eula.txt") "eula=true`n"
    Write-Utf8Text (Join-Path $fixture "server\user_jvm_args.txt") "-Xms1024M`n-Xmx2048M`n"
    Write-Utf8Text (Join-Path $fixture "server\start.bat") @'
@echo off
if not defined HECHAO_MANAGED_START pause
java @user_jvm_args.txt -jar fabric-server-launch.jar nogui
'@
    Write-FixtureBytes (Join-Path $fixture "server\fabric-server-launch.jar") "fixture-fabric-server"
    Write-FixtureBytes (Join-Path $fixture "server\mods\server-only.jar") "fixture-server-mod"
    Write-FixtureBytes (Join-Path $fixture "shared\mods\hechao-contract.jar") "fixture-common-mod"

    $validation = & $validator -SourceDirectory $fixture -PassThru
    Assert-True ($validation.package.id -eq "activity-contract-fixture-fabric-1.20.1") "Validator returned the wrong package ID."
    Assert-True ($validation.totals.clientFileCount -gt 0) "Validator did not classify client files."
    Assert-True ($validation.totals.serverFileCount -gt 0) "Validator did not classify server files."
    Assert-True ($validation.totals.sharedFileCount -eq 1) "Validator did not classify the shared fixture."

    $sharedFixturePath = Join-Path $fixture "shared\mods\hechao-contract.jar"
    Remove-Item -LiteralPath $sharedFixturePath -Force
    $validationWithoutSharedFiles = & $validator -SourceDirectory $fixture -PassThru
    Assert-True `
        ($validationWithoutSharedFiles.totals.sharedFileCount -eq 0) `
        "Validator did not accept an empty shared file set."
    Assert-True `
        ($validationWithoutSharedFiles.totals.sharedBytes -eq 0) `
        "Validator did not report zero bytes for an empty shared file set."
    Write-FixtureBytes $sharedFixturePath "fixture-common-mod"

    $businessArchive = Join-Path $temporaryRoot "Hechao-contract-fixture-1.0.0.zip"
    $buildResult = & $builder -SourceDirectory $fixture -OutputArchive $businessArchive
    Assert-True (Test-Path -LiteralPath $businessArchive -PathType Leaf) "Business archive was not generated."
    Assert-True (Test-Path -LiteralPath ($businessArchive + ".sha256") -PathType Leaf) "Business archive SHA-256 sidecar was not generated."
    Assert-True (Test-Path -LiteralPath ($businessArchive + ".report.json") -PathType Leaf) "Business archive report was not generated."
    $actualArchiveHash = (Get-FileHash -LiteralPath $businessArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($actualArchiveHash -ceq [string] $buildResult.archiveSha256) "Business archive SHA-256 result is incorrect."

    $zip = [System.IO.Compression.ZipFile]::OpenRead($businessArchive)
    try {
        $entryNames = @($zip.Entries | ForEach-Object FullName)
        Assert-True ("hechao-pack.json" -cin $entryNames) "Business archive lacks hechao-pack.json."
        Assert-True ("client/hechao-profile.json" -cin $entryNames) "Business archive lacks client metadata."
        Assert-True ("server/start.bat" -cin $entryNames) "Business archive lacks server/start.bat."
        Assert-True ("shared/mods/hechao-contract.jar" -cin $entryNames) "Business archive lacks the shared JAR."
        Assert-True (-not (@($entryNames | Where-Object { $_ -match '(^|/)\.example$' }).Count -gt 0)) "Business archive contains an example file."
        foreach ($name in $entryNames) {
            Assert-True (-not $name.StartsWith("valid-source/")) "Business archive contains an outer wrapper directory."
        }
    }
    finally {
        $zip.Dispose()
    }

    $forbiddenPath = Join-Path $fixture "server\forwarding.secret"
    Write-Utf8Text $forbiddenPath "not-a-real-secret"
    $secretRejected = $false
    try {
        & $validator -SourceDirectory $fixture -PassThru | Out-Null
    }
    catch {
        $secretRejected = $_.Exception.Message -match 'forbidden|validation failed'
    }
    Assert-True $secretRejected "Validator did not reject forwarding.secret."
    Remove-Item -LiteralPath $forbiddenPath -Force

    Write-FixtureBytes (Join-Path $fixture "client\mods\mismatch.jar") "client-bytes"
    Write-FixtureBytes (Join-Path $fixture "server\mods\mismatch.jar") "server-bytes"
    $mismatchRejected = $false
    try {
        & $validator -SourceDirectory $fixture -PassThru | Out-Null
    }
    catch {
        $mismatchRejected = $_.Exception.Message -match 'different SHA-256|validation failed'
    }
    Assert-True $mismatchRejected "Validator did not reject a mismatched common JAR."

    if (-not [string]::IsNullOrWhiteSpace($HandoffArchive)) {
        Test-HandoffIntegrity $HandoffArchive
    }

    Write-Output ([pscustomobject] [ordered]@{
        sourceContract = "passed"
        businessArchive = "passed"
        secretRejection = "passed"
        commonJarMismatchRejection = "passed"
        handoffArchive = if ([string]::IsNullOrWhiteSpace($HandoffArchive)) { "not-requested" } else { "passed" }
        fixtureFiles = [int] $validation.totals.fileCount
    })
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
