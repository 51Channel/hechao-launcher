#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MirrorRoot,

    [Parameter(Mandatory)]
    [string]$HostName,

    [string]$UserName = "root",

    [ValidateRange(1, 65535)]
    [int]$Port = 22,

    [Parameter(Mandatory)]
    [string]$IdentityFile,

    [Parameter(Mandatory)]
    [string]$KnownHostsFile,

    [string]$RemoteRoot = "/var/backups/hechao-launcher/distribution-objects",

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function ConvertTo-BashSingleQuoted {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "'""'""'") + "'"
}

function Get-SshBaseArguments {
    return @(
        "-i", (Resolve-Path -LiteralPath $IdentityFile).Path,
        "-p", $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes",
        "-o", "UserKnownHostsFile=$(
            (Resolve-Path -LiteralPath $KnownHostsFile).Path
        )",
        "$UserName@$HostName"
    )
}

function Invoke-RemoteScript {
    param(
        [Parameter(Mandatory)]
        [string]$Script
    )

    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($Script)
    )
    $arguments = @(
        Get-SshBaseArguments
    ) + @(
        "echo $encoded | base64 -d | bash"
    )
    $output = & ssh.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Remote recovery command failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Copy-MirrorArchive {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRoot,

        [Parameter(Mandatory)]
        [string]$RemoteStagingRoot
    )

    $sourceParent = Split-Path -Parent $SourceRoot
    $sourceLeaf = Split-Path -Leaf $SourceRoot
    $remoteCommand = (
        "tar -xf - -C {0} --strip-components=1" -f
            (ConvertTo-BashSingleQuoted -Value $RemoteStagingRoot)
    )

    $tarStart = [Diagnostics.ProcessStartInfo]::new()
    $tarStart.FileName = "tar.exe"
    $tarStart.UseShellExecute = $false
    $tarStart.RedirectStandardOutput = $true
    $tarStart.RedirectStandardError = $true
    foreach ($argument in @("-cf", "-", "-C", $sourceParent, $sourceLeaf)) {
        $tarStart.ArgumentList.Add($argument)
    }

    $sshStart = [Diagnostics.ProcessStartInfo]::new()
    $sshStart.FileName = "ssh.exe"
    $sshStart.UseShellExecute = $false
    $sshStart.RedirectStandardInput = $true
    $sshStart.RedirectStandardOutput = $true
    $sshStart.RedirectStandardError = $true
    foreach ($argument in @(
        (Get-SshBaseArguments) + @($remoteCommand)
    )) {
        $sshStart.ArgumentList.Add($argument)
    }

    $tar = [Diagnostics.Process]::new()
    $tar.StartInfo = $tarStart
    $ssh = [Diagnostics.Process]::new()
    $ssh.StartInfo = $sshStart
    try {
        if (-not $ssh.Start()) {
            throw "Unable to start ssh.exe."
        }
        if (-not $tar.Start()) {
            throw "Unable to start tar.exe."
        }

        $tarErrorTask = $tar.StandardError.ReadToEndAsync()
        $sshOutputTask = $ssh.StandardOutput.ReadToEndAsync()
        $sshErrorTask = $ssh.StandardError.ReadToEndAsync()
        $copyTask = $tar.StandardOutput.BaseStream.CopyToAsync(
            $ssh.StandardInput.BaseStream
        )
        $copyTask.GetAwaiter().GetResult() | Out-Null
        $ssh.StandardInput.Close()

        $tar.WaitForExit()
        $ssh.WaitForExit()
        $tarError = $tarErrorTask.GetAwaiter().GetResult()
        $sshOutput = $sshOutputTask.GetAwaiter().GetResult()
        $sshError = $sshErrorTask.GetAwaiter().GetResult()

        if ($tar.ExitCode -ne 0) {
            throw "tar.exe failed with exit code $($tar.ExitCode): $tarError"
        }
        if ($ssh.ExitCode -ne 0) {
            throw "ssh.exe failed with exit code $($ssh.ExitCode): $sshError"
        }
        if (-not [string]::IsNullOrWhiteSpace($sshError)) {
            Write-Verbose $sshError.Trim()
        }
        if (-not [string]::IsNullOrWhiteSpace($sshOutput)) {
            Write-Verbose $sshOutput.Trim()
        }
    } finally {
        $tar.Dispose()
        $ssh.Dispose()
    }
}

foreach ($path in @($IdentityFile, $KnownHostsFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SSH file does not exist: $path"
    }
}
if ($RemoteRoot -notmatch "^/[A-Za-z0-9._/-]+$" -or
    $RemoteRoot -eq "/" -or
    $RemoteRoot.Contains("..")) {
    throw "RemoteRoot must be a safe absolute Linux path."
}

$resolvedMirror = (Resolve-Path -LiteralPath $MirrorRoot).Path.TrimEnd("\", "/")
$verifyTool = Join-Path (
    Split-Path -Parent $PSCommandPath
) "Test-HechaoDistributionObjectMirror.ps1"
$localValidation = & $verifyTool -MirrorRoot $resolvedMirror -AsJson |
    ConvertFrom-Json

$inventoryPath = Join-Path $resolvedMirror "inventory.json"
$sumsPath = Join-Path $resolvedMirror "SHA256SUMS"
$inventorySha256 = (
    Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$sumsSha256 = (
    Get-FileHash -LiteralPath $sumsPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$remoteStaging = "$RemoteRoot/$stamp.partial"
$remoteFinal = "$RemoteRoot/$stamp"

$quotedRemoteRoot = ConvertTo-BashSingleQuoted -Value $RemoteRoot
$quotedRemoteStaging = ConvertTo-BashSingleQuoted -Value $remoteStaging
$prepareScript = @"
set -euo pipefail
umask 077
install -d -m 700 $quotedRemoteRoot
if [ -e $quotedRemoteStaging ]; then
    echo "remote staging path already exists" >&2
    exit 20
fi
install -d -m 700 $quotedRemoteStaging
"@
Invoke-RemoteScript -Script $prepareScript | Out-Null

Copy-MirrorArchive `
    -SourceRoot $resolvedMirror `
    -RemoteStagingRoot $remoteStaging

$quotedRemoteFinal = ConvertTo-BashSingleQuoted -Value $remoteFinal
$quotedRestore = ConvertTo-BashSingleQuoted -Value (
    "/var/tmp/hechao-distribution-restore-$stamp"
)
$expectedObjects = [int64]$localValidation.uniqueObjectCount
$expectedProfiles = [int64]$localValidation.profileCount
$expectedObjectBytes = [int64]$localValidation.uniqueObjectBytes
$expectedValidatedFiles = [int64]$localValidation.validatedFileCount
$quotedObjectSet = ConvertTo-BashSingleQuoted -Value (
    [string]$localValidation.objectSetSha256
)
$quotedInventorySha = ConvertTo-BashSingleQuoted -Value $inventorySha256
$quotedSumsSha = ConvertTo-BashSingleQuoted -Value $sumsSha256

$finalizeScript = @"
set -euo pipefail
umask 077
staging=$quotedRemoteStaging
final=$quotedRemoteFinal
root=$quotedRemoteRoot
restore=$quotedRestore

test -d "`$staging"
test ! -e "`$final"
cd "`$staging"

test "`$(sha256sum inventory.json | awk '{print `$1}')" = $quotedInventorySha
test "`$(sha256sum SHA256SUMS | awk '{print `$1}')" = $quotedSumsSha
sha256sum -c SHA256SUMS > verification.log

object_count="`$(find objects -type f | wc -l)"
manifest_count="`$(find manifests -type f | wc -l)"
object_bytes="`$(find objects -type f -printf '%s\n' | awk '{sum += `$1} END {print sum + 0}')"
validated_count="`$(wc -l < SHA256SUMS)"
test "`$object_count" -eq $expectedObjects
test "`$manifest_count" -eq $expectedProfiles
test "`$object_bytes" -eq $expectedObjectBytes
test "`$validated_count" -eq $expectedValidatedFiles

inventory_object_set="`$(python3 - <<'PY'
import json
with open("inventory.json", "r", encoding="utf-8") as handle:
    print(json.load(handle)["objectSetSha256"])
PY
)"
test "`$inventory_object_set" = $quotedObjectSet

case "`$restore" in
    /var/tmp/hechao-distribution-restore-*) ;;
    *) echo "unsafe restore path" >&2; exit 30 ;;
esac
test ! -e "`$restore"
install -d -m 700 "`$restore"
tar -cf - -C "`$staging" . | tar -xf - -C "`$restore"
cd "`$restore"
test "`$(sha256sum inventory.json | awk '{print `$1}')" = $quotedInventorySha
test "`$(sha256sum SHA256SUMS | awk '{print `$1}')" = $quotedSumsSha
sha256sum -c SHA256SUMS > /dev/null
test "`$(find objects -type f | wc -l)" -eq $expectedObjects
test "`$(find manifests -type f | wc -l)" -eq $expectedProfiles
cd /
rm -rf -- "`$restore"

cd "`$staging"
python3 - <<PY
import json
from datetime import datetime, timezone

evidence = {
    "schemaVersion": 1,
    "acceptedAtUtc": datetime.now(timezone.utc).isoformat(),
    "profileCount": $expectedProfiles,
    "uniqueObjectCount": $expectedObjects,
    "uniqueObjectBytes": $expectedObjectBytes,
    "validatedFileCount": $expectedValidatedFiles,
    "objectSetSha256": "$($localValidation.objectSetSha256)",
    "inventorySha256": "$inventorySha256",
    "sha256SumsSha256": "$sumsSha256",
    "fullHashValidationPassed": True,
    "isolatedRestoreValidationPassed": True,
    "source": "administrator-release-workstation",
    "destinationClass": "independent-api-host-system-disk",
}
with open("acceptance.json", "w", encoding="utf-8") as handle:
    json.dump(evidence, handle, ensure_ascii=True, indent=2)
    handle.write("\n")
PY

chmod -R go-rwx "`$staging"
mv -- "`$staging" "`$final"
ln -sfn -- "`$final" "`$root/current.new"
mv -Tf -- "`$root/current.new" "`$root/current"
cat "`$final/acceptance.json"
"@

$remoteAcceptance = (
    Invoke-RemoteScript -Script $finalizeScript
) -join [Environment]::NewLine
$acceptance = $remoteAcceptance | ConvertFrom-Json

$result = [ordered]@{
    status = "installed"
    host = $HostName
    remotePath = $remoteFinal
    currentPath = "$RemoteRoot/current"
    profileCount = [int64]$acceptance.profileCount
    uniqueObjectCount = [int64]$acceptance.uniqueObjectCount
    uniqueObjectBytes = [int64]$acceptance.uniqueObjectBytes
    validatedFileCount = [int64]$acceptance.validatedFileCount
    objectSetSha256 = [string]$acceptance.objectSetSha256
    inventorySha256 = [string]$acceptance.inventorySha256
    sha256SumsSha256 = [string]$acceptance.sha256SumsSha256
    fullHashValidationPassed = [bool]$acceptance.fullHashValidationPassed
    isolatedRestoreValidationPassed = [bool]$acceptance.isolatedRestoreValidationPassed
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4 -Compress
} else {
    [pscustomobject]$result
}
