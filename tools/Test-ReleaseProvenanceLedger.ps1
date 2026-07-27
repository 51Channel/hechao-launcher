[CmdletBinding()]
param(
    [string]$LedgerPath = (
        Join-Path $PSScriptRoot `
            '..\docs\evidence\ACTIVE_RELEASE_PROVENANCE_2026-07-28.json'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryPrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
) + [IO.Path]::DirectorySeparatorChar
$resolvedLedger = [IO.Path]::GetFullPath($LedgerPath)
$errors = [Collections.Generic.List[string]]::new()
$sha256Pattern = '^[0-9A-Fa-f]{64}$'
$commitPattern = '^[0-9a-f]{40}$'

function Add-ValidationError {
    param([string]$Message)

    $errors.Add($Message)
}

function Invoke-GitScalar {
    param([string[]]$Arguments)

    $output = & git -C $repositoryRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return (($output | Out-String).Trim())
}

if (-not (Test-Path -LiteralPath $resolvedLedger -PathType Leaf)) {
    throw "Release provenance ledger not found: $resolvedLedger"
}

try {
    $ledger = Get-Content -LiteralPath $resolvedLedger -Raw -Encoding utf8 |
        ConvertFrom-Json
}
catch {
    throw "Release provenance ledger is not valid JSON: $($_.Exception.Message)"
}

if ($ledger.schemaVersion -ne 1) {
    Add-ValidationError "schemaVersion must be 1."
}

if ([string]::IsNullOrWhiteSpace([string]$ledger.scope)) {
    Add-ValidationError "scope is required."
}

$generatedAt = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse(
        [string]$ledger.generatedAtUtc,
        [ref]$generatedAt
    )) {
    Add-ValidationError "generatedAtUtc must be an ISO-8601 timestamp."
}

$records = @($ledger.records)
if ($records.Count -eq 0) {
    Add-ValidationError "At least one release record is required."
}

$recordKeys = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)

foreach ($record in $records) {
    $component = [string]$record.component
    $version = [string]$record.version
    $recordLabel = "$component@$version"

    if ([string]::IsNullOrWhiteSpace($component)) {
        Add-ValidationError "A release record has no component."
        continue
    }

    if ([string]::IsNullOrWhiteSpace($version)) {
        Add-ValidationError "$component has no version."
    }

    if (-not $recordKeys.Add($recordLabel)) {
        Add-ValidationError "Duplicate release record: $recordLabel"
    }

    foreach ($field in @(
            'releaseTag',
            'releaseCommit',
            'taggedAt',
            'publisher',
            'deploymentState'
        )) {
        if ([string]::IsNullOrWhiteSpace([string]$record.$field)) {
            Add-ValidationError "$recordLabel is missing $field."
        }
    }

    $taggedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string]$record.taggedAt,
            [ref]$taggedAt
        )) {
        Add-ValidationError "$recordLabel has an invalid taggedAt timestamp."
    }

    if ([string]$record.releaseCommit -notmatch $commitPattern) {
        Add-ValidationError "$recordLabel has an invalid releaseCommit."
    }

    $tagReference = "refs/tags/$($record.releaseTag)"
    $tagType = Invoke-GitScalar @('cat-file', '-t', $tagReference)
    if ($tagType -ne 'tag') {
        Add-ValidationError (
            "$recordLabel must reference an annotated tag: " +
            "$($record.releaseTag)"
        )
    }
    else {
        $tagCommit = Invoke-GitScalar @(
            'rev-list',
            '-n',
            '1',
            [string]$record.releaseTag
        )
        if ($tagCommit -ne [string]$record.releaseCommit) {
            Add-ValidationError (
                "$recordLabel tag resolves to $tagCommit instead of " +
                "$($record.releaseCommit)."
            )
        }

        $tagger = Invoke-GitScalar @(
            'for-each-ref',
            '--format=%(taggername)',
            $tagReference
        )
        if ($tagger -ne [string]$record.publisher) {
            Add-ValidationError (
                "$recordLabel publisher '$($record.publisher)' does not " +
                "match annotated tagger '$tagger'."
            )
        }
    }

    $buildSource = $record.buildSource
    $sourceKind = [string]$buildSource.kind
    $sourceValue = [string]$buildSource.value
    switch ($sourceKind) {
        'git-commit' {
            if ($sourceValue -notmatch $commitPattern) {
                Add-ValidationError "$recordLabel has an invalid source commit."
            }
            else {
                $sourceType = Invoke-GitScalar @(
                    'cat-file',
                    '-t',
                    "$sourceValue`^{commit}"
                )
                if ($sourceType -ne 'commit') {
                    Add-ValidationError (
                        "$recordLabel source commit is not present in Git."
                    )
                }
            }
        }
        'signed-manifest' {
            if ($sourceValue -notmatch $sha256Pattern) {
                Add-ValidationError (
                    "$recordLabel signed-manifest source is not SHA-256."
                )
            }
        }
        default {
            Add-ValidationError (
                "$recordLabel has unsupported build source kind '$sourceKind'."
            )
        }
    }

    $artifacts = @($record.artifacts)
    if ($artifacts.Count -eq 0) {
        Add-ValidationError "$recordLabel has no artifacts."
    }

    $primaryCount = 0
    foreach ($artifact in $artifacts) {
        if ([string]::IsNullOrWhiteSpace([string]$artifact.name)) {
            Add-ValidationError "$recordLabel has an artifact without a name."
        }

        if ([string]::IsNullOrWhiteSpace([string]$artifact.location)) {
            Add-ValidationError (
                "$recordLabel artifact '$($artifact.name)' has no location."
            )
        }

        if ([string]$artifact.sha256 -notmatch $sha256Pattern) {
            Add-ValidationError (
                "$recordLabel artifact '$($artifact.name)' has invalid SHA-256."
            )
        }

        if ($artifact.primary -eq $true) {
            $primaryCount++
        }
    }

    if ($primaryCount -ne 1) {
        Add-ValidationError (
            "$recordLabel must identify exactly one primary artifact; " +
            "found $primaryCount."
        )
    }

    if ([string]::IsNullOrWhiteSpace([string]$record.rollback.kind) -or
        [string]::IsNullOrWhiteSpace([string]$record.rollback.target)) {
        Add-ValidationError "$recordLabel has no explicit rollback target."
    }

    $evidencePaths = @($record.evidence)
    if ($evidencePaths.Count -eq 0) {
        Add-ValidationError "$recordLabel has no evidence files."
    }

    foreach ($evidencePath in $evidencePaths) {
        if ([IO.Path]::IsPathRooted([string]$evidencePath)) {
            Add-ValidationError (
                "$recordLabel evidence path must be repository-relative: " +
                "$evidencePath"
            )
            continue
        }

        $fullEvidencePath = [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot ([string]$evidencePath))
        )
        if (-not $fullEvidencePath.StartsWith(
                $repositoryPrefix,
                [StringComparison]::OrdinalIgnoreCase
            )) {
            Add-ValidationError (
                "$recordLabel evidence path escapes the repository: " +
                "$evidencePath"
            )
            continue
        }

        if (-not (Test-Path -LiteralPath $fullEvidencePath -PathType Leaf)) {
            Add-ValidationError (
                "$recordLabel evidence file does not exist: $evidencePath"
            )
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

$successMessage = (
    "PASS: {0} active releases have annotated tags, build sources, " +
    "SHA-256 artifacts, publishers, rollback targets and evidence."
) -f $records.Count
Write-Output $successMessage
