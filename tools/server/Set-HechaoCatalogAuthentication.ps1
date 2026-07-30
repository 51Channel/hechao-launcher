#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ApiHostName,

    [string]$ApiUserName = "root",

    [ValidateRange(1, 65535)]
    [int]$ApiSshPort = 22,

    [Parameter(Mandatory)]
    [string]$ApiIdentityFile,

    [Parameter(Mandatory)]
    [string]$ApiKnownHostsFile,

    [Parameter(Mandatory)]
    [ValidateSet("Enabled", "Disabled")]
    [string]$DesiredState,

    [string]$EnforceEvidencePath,

    [uri]$ApiBaseUrl = "https://launcher-api.hechao.world/",

    [string]$EnvironmentFile = "/etc/hechao-launcher-api/environment",

    [string]$ServiceName = "hechao-launcher-api.service",

    [string]$BackupRoot = "/var/backups/hechao-launcher-api",

    [string]$OutputPath,

    [switch]$Apply,

    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

foreach ($path in @($ApiIdentityFile, $ApiKnownHostsFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SSH file does not exist: $path"
    }
}
if ($ApiHostName -notmatch "^[A-Za-z0-9.-]+$" -or
    $ApiUserName -notmatch "^[A-Za-z0-9._-]+$") {
    throw "SSH host or user name contains unsupported characters."
}
foreach ($remotePath in @($EnvironmentFile, $BackupRoot)) {
    if ($remotePath -notmatch "^/[A-Za-z0-9._/-]+$") {
        throw "Remote path contains unsupported characters: $remotePath"
    }
}
if ($ServiceName -notmatch "^[A-Za-z0-9_.@-]+$") {
    throw "ServiceName contains unsupported characters."
}
if ($ApiBaseUrl.Scheme -ne "https" -or
    $ApiBaseUrl.Host -notmatch "^[A-Za-z0-9.-]+$" -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.UserInfo) -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.Query) -or
    -not [string]::IsNullOrEmpty($ApiBaseUrl.Fragment) -or
    $ApiBaseUrl.AbsolutePath -ne "/") {
    throw "ApiBaseUrl must be a plain HTTPS origin."
}
if ($Apply -and [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw "OutputPath is required when Apply is specified."
}

$gateResult = $null
if ($DesiredState -eq "Enabled") {
    if ([string]::IsNullOrWhiteSpace($EnforceEvidencePath) -or
        -not (Test-Path -LiteralPath $EnforceEvidencePath -PathType Leaf)) {
        throw "Passing enforce-mode evidence is required before enabling."
    }

    $gateScript = Join-Path (
        Split-Path -Parent (Split-Path -Parent $PSCommandPath)
    ) "acceptance\Test-HechaoAuthorizerEnforceGate.ps1"
    $gateOutput = & (Join-Path $PSHOME "pwsh.exe") `
        -NoLogo `
        -NoProfile `
        -File $gateScript `
        -EvidencePath $EnforceEvidencePath `
        -ExpectedEvidenceAuthorizerMode enforce `
        -AsJson
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Enforce-mode evidence failed the catalog-authentication gate. " +
            "The production setting remains unchanged."
        )
    }
    $gateResult = ($gateOutput -join [Environment]::NewLine) |
        ConvertFrom-Json
}

$desiredValue = if ($DesiredState -eq "Enabled") {
    "true"
} else {
    "false"
}
$expectedCatalogStatus = if ($DesiredState -eq "Enabled") {
    401
} else {
    200
}

function Invoke-ApiSsh {
    param(
        [Parameter(Mandatory)]
        [string]$RemoteCommand
    )

    $arguments = @(
        "-i", (Resolve-Path -LiteralPath $ApiIdentityFile).Path,
        "-p", $ApiSshPort.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        ),
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=yes",
        "-o", "UserKnownHostsFile=$(
            (Resolve-Path -LiteralPath $ApiKnownHostsFile).Path
        )",
        "$ApiUserName@$ApiHostName",
        $RemoteCommand
    )
    $output = & ssh.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Remote API operation failed with exit code $LASTEXITCODE."
    }
    return @($output)
}

$statusScript = @"
set -euo pipefail
environment_file='$EnvironmentFile'
service_name='$ServiceName'
test -f "`$environment_file"
count=`$(grep -c '^Authentication__EnforceCatalogAuthentication=' \
  "`$environment_file")
test "`$count" -eq 1
value=`$(grep '^Authentication__EnforceCatalogAuthentication=' \
  "`$environment_file" | cut -d= -f2-)
printf 'value=%s\n' "`$value"
printf 'service=%s\n' "`$(systemctl is-active "`$service_name")"
"@
$statusEncoded = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($statusScript)
)
$statusOutput = Invoke-ApiSsh -RemoteCommand (
    "echo $statusEncoded | base64 -d | bash"
)
$statusValues = @{}
foreach ($line in $statusOutput) {
    if ($line -match "^(?<name>[a-z]+)=(?<value>.*)$") {
        $statusValues[$Matches.name] = $Matches.value
    }
}
if ($statusValues.service -ne "active") {
    throw "Launcher API service is not active."
}
if ($statusValues.value -notin @("true", "false")) {
    throw "Current catalog-authentication value is invalid."
}
$previousValue = [string]$statusValues.value

$changed = $false
$remoteValues = @{}
$apiRoot = $ApiBaseUrl.AbsoluteUri.TrimEnd("/")
$transactionScript = @"
set -euo pipefail
environment_file='$EnvironmentFile'
service_name='$ServiceName'
backup_root='$BackupRoot'
desired_value='$desiredValue'
expected_catalog_status='$expectedCatalogStatus'
api_root='$apiRoot'

wait_ready() {
  local deadline=`$((SECONDS + 60))
  while (( SECONDS < deadline )); do
    if curl --fail --silent --show-error --max-time 5 \
      http://127.0.0.1:8090/readyz >/dev/null; then
      return 0
    fi
    sleep 1
  done
  return 1
}

verify_public() {
  local expected_catalog_status="`$1"
  health_status=`$(curl --silent --show-error --max-time 10 \
    --output /dev/null --write-out '%{http_code}' \
    "`$api_root/healthz")
  ready_status=`$(curl --silent --show-error --max-time 10 \
    --output /dev/null --write-out '%{http_code}' \
    "`$api_root/readyz")
  catalog_status=`$(curl --silent --show-error --max-time 10 \
    --output /dev/null --write-out '%{http_code}' \
    "`$api_root/v1/catalog")
  test "`$health_status" = "200"
  test "`$ready_status" = "200"
  test "`$catalog_status" = "`$expected_catalog_status"
}

current_value=`$(grep '^Authentication__EnforceCatalogAuthentication=' \
  "`$environment_file" | cut -d= -f2-)
timestamp=`$(date -u +%Y%m%dT%H%M%SZ)
backup_directory="`$backup_root/catalog-auth-`$current_value-to-`$desired_value-`$timestamp"
install -d -m 700 "`$backup_directory"
backup_file="`$backup_directory/environment"
cp --preserve=mode,ownership,timestamps \
  "`$environment_file" "`$backup_file"
backup_sha256=`$(sha256sum "`$backup_file" | awk '{print toupper(`$1)}')
printf '%s  environment\n' "`$(printf '%s' "`$backup_sha256" | \
  tr '[:upper:]' '[:lower:]')" \
  > "`$backup_directory/manifest.sha256"
chmod 600 "`$backup_directory/manifest.sha256"

python3 - "`$environment_file" "`$desired_value" <<'PY'
import os
import shutil
import sys
import tempfile

path, desired = sys.argv[1:]
with open(path, "r", encoding="utf-8") as source:
    lines = source.read().splitlines()
prefix = "Authentication__EnforceCatalogAuthentication="
matches = sum(line.startswith(prefix) for line in lines)
if matches != 1:
    raise SystemExit("catalog authentication setting is not unique")
updated = [
    prefix + desired if line.startswith(prefix) else line
    for line in lines
]
directory = os.path.dirname(path)
descriptor, temporary = tempfile.mkstemp(
    prefix=".hechao-catalog-auth-",
    dir=directory,
    text=True,
)
try:
    with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as target:
        target.write("\n".join(updated) + "\n")
    source_stat = os.stat(path)
    shutil.copystat(path, temporary)
    os.chown(temporary, source_stat.st_uid, source_stat.st_gid)
    os.replace(temporary, path)
finally:
    if os.path.exists(temporary):
        os.unlink(temporary)
PY

rollback() {
  cp --preserve=mode,ownership,timestamps \
    "`$backup_file" "`$environment_file"
  systemctl restart "`$service_name"
  wait_ready
  if test "`$current_value" = "true"; then
    verify_public 401
  else
    verify_public 200
  fi
}

transaction_complete=false
on_error() {
  local exit_code="`$?"
  trap - ERR
  set +e
  if test "`$transaction_complete" != "true"; then
    rollback
  fi
  exit "`$exit_code"
}
trap on_error ERR

systemctl restart "`$service_name"
wait_ready
verify_public "`$expected_catalog_status"

actual_value=`$(grep '^Authentication__EnforceCatalogAuthentication=' \
  "`$environment_file" | cut -d= -f2-)
test "`$actual_value" = "`$desired_value"
transaction_complete=true
trap - ERR
printf 'changed=true\n'
printf 'previous=%s\n' "`$current_value"
printf 'current=%s\n' "`$actual_value"
printf 'backup=%s\n' "`$backup_directory"
printf 'backup_sha256=%s\n' "`$backup_sha256"
printf 'service=%s\n' "`$(systemctl is-active "`$service_name")"
printf 'health_status=%s\n' "`$health_status"
printf 'ready_status=%s\n' "`$ready_status"
printf 'catalog_status=%s\n' "`$catalog_status"
"@
$syntaxEncoded = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($transactionScript)
)
Invoke-ApiSsh -RemoteCommand (
    "echo $syntaxEncoded | base64 -d | bash -n"
) | Out-Null

if ($Apply -and $previousValue -ne $desiredValue) {
    $transactionEncoded = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($transactionScript)
    )
    $transactionOutput = Invoke-ApiSsh -RemoteCommand (
        "echo $transactionEncoded | base64 -d | bash"
    )
    foreach ($line in $transactionOutput) {
        if ($line -match "^(?<name>[a-z_]+)=(?<value>.*)$") {
            $remoteValues[$Matches.name] = $Matches.value
        }
    }
    if ($remoteValues.current -ne $desiredValue -or
        $remoteValues.service -ne "active" -or
        $remoteValues.health_status -ne "200" -or
        $remoteValues.ready_status -ne "200" -or
        $remoteValues.catalog_status -ne
            $expectedCatalogStatus.ToString(
                [Globalization.CultureInfo]::InvariantCulture
            )) {
        throw "Remote catalog-authentication verification failed."
    }
    $changed = $true
}

$healthStatus = $null
$readyStatus = $null
$catalogStatus = $null
$publicVerificationSource = "operator-host"
try {
    $handler = [Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $healthResponse = $client.GetAsync(
            [uri]::new($ApiBaseUrl, "healthz")
        ).GetAwaiter().GetResult()
        try {
            $healthStatus = [int]$healthResponse.StatusCode
        } finally {
            $healthResponse.Dispose()
        }
        $readyResponse = $client.GetAsync(
            [uri]::new($ApiBaseUrl, "readyz")
        ).GetAwaiter().GetResult()
        try {
            $readyStatus = [int]$readyResponse.StatusCode
        } finally {
            $readyResponse.Dispose()
        }
        $catalogResponse = $client.GetAsync(
            [uri]::new($ApiBaseUrl, "v1/catalog")
        ).GetAwaiter().GetResult()
        try {
            $catalogStatus = [int]$catalogResponse.StatusCode
        } finally {
            $catalogResponse.Dispose()
        }
    } finally {
        $client.Dispose()
    }
} catch {
    if ($Apply -and $changed -and
        $remoteValues.ContainsKey("health_status") -and
        $remoteValues.ContainsKey("ready_status") -and
        $remoteValues.ContainsKey("catalog_status")) {
        $healthStatus = [int]$remoteValues.health_status
        $readyStatus = [int]$remoteValues.ready_status
        $catalogStatus = [int]$remoteValues.catalog_status
        $publicVerificationSource = "remote-origin"
    } else {
        throw "Public API verification failed."
    }
}

$effectiveValue = if ($Apply) {
    $desiredValue
} else {
    $previousValue
}
$expectedCurrentCatalogStatus = if ($effectiveValue -eq "true") {
    401
} else {
    200
}
if ($healthStatus -ne 200 -or
    $readyStatus -ne 200 -or
    $catalogStatus -ne $expectedCurrentCatalogStatus) {
    if ($Apply -and $changed -and
        [int]$remoteValues.health_status -eq 200 -and
        [int]$remoteValues.ready_status -eq 200 -and
        [int]$remoteValues.catalog_status -eq
            $expectedCurrentCatalogStatus) {
        $healthStatus = [int]$remoteValues.health_status
        $readyStatus = [int]$remoteValues.ready_status
        $catalogStatus = [int]$remoteValues.catalog_status
        $publicVerificationSource = "remote-origin"
    } else {
        throw "Current public API behavior does not match its catalog setting."
    }
}

$result = [ordered]@{
    schemaVersion = 1
    status = if (-not $Apply) {
        "eligible-dry-run"
    } elseif ($changed) {
        "changed"
    } else {
        "already-in-desired-state"
    }
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    host = $ApiHostName
    desiredState = $DesiredState
    previousValue = $previousValue
    currentValue = $effectiveValue
    applied = [bool]$Apply
    changed = $changed
    enforceGatePassed = if ($DesiredState -eq "Enabled") {
        [bool]$gateResult.passed
    } else {
        $null
    }
    publicChecks = [ordered]@{
        source = $publicVerificationSource
        healthStatus = $healthStatus
        readyStatus = $readyStatus
        anonymousCatalogStatus = $catalogStatus
    }
    backupDirectory = if ($remoteValues.ContainsKey("backup")) {
        $remoteValues.backup
    } else {
        $null
    }
    backupSha256 = if ($remoteValues.ContainsKey("backup_sha256")) {
        $remoteValues.backup_sha256
    } else {
        $null
    }
    rollback = (
        "Restore the protected environment backup and restart only " +
        "$ServiceName; failed remote health checks do this automatically."
    )
}

if ($Apply) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory(
        (Split-Path -Parent $resolvedOutput)
    ) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedOutput,
        ($result | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 6 -Compress
} else {
    [pscustomobject]$result
}
