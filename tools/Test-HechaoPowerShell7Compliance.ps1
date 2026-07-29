#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$SummaryOnly
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 is required."
}

Push-Location $RepositoryRoot
try {
    $searchRoots = @(
        "README.md"
        "docs"
        "tools"
        ".github"
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $references = @(
        & rg.exe -n --hidden `
            --ignore-case `
            --glob "!**/bin/**" `
            --glob "!**/obj/**" `
            --glob "!**/.git/**" `
            "powershell(\.exe)?|PowerShell 5\.1|pwsh(\.exe)?|New-ScheduledTaskAction|Register-ScheduledTask|Set-ScheduledTask" `
            $searchRoots 2>$null
    )

    if ($LASTEXITCODE -gt 1) {
        throw "rg failed with exit code $LASTEXITCODE."
    }

    $scriptFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "tools") `
            -Filter "*.ps1" `
            -File `
            -Recurse
    )
    $parseFailures = @()

    foreach ($scriptFile in $scriptFiles) {
        $tokens = $null
        $parseErrors = $null
        [Management.Automation.Language.Parser]::ParseFile(
            $scriptFile.FullName,
            [ref]$tokens,
            [ref]$parseErrors
        ) | Out-Null

        foreach ($parseError in $parseErrors) {
            $parseFailures += "$($scriptFile.FullName): $($parseError.Message)"
        }
    }

    if ($parseFailures.Count -gt 0) {
        throw "PowerShell parse checks failed:`n$($parseFailures -join "`n")"
    }

    $evidenceFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "docs\evidence") `
            -Filter "*.json" `
            -File `
            -ErrorAction SilentlyContinue
    )
    foreach ($evidenceFile in $evidenceFiles) {
        $document = [Text.Json.JsonDocument]::Parse(
            [IO.File]::ReadAllText($evidenceFile.FullName)
        )
        $document.Dispose()
    }

    $result = [ordered]@{
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        powershellHome = $PSHOME
        parsedScriptCount = $scriptFiles.Count
        parsedEvidenceJsonCount = $evidenceFiles.Count
    }
    if (-not $SummaryOnly) {
        $result.references = $references
    }

    [pscustomobject]$result | ConvertTo-Json -Depth 4
} finally {
    Pop-Location
}
