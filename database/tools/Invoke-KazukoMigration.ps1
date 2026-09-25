<#
.SYNOPSIS
    Applies a single Firebird schema migration script with full artifact SHA-256 checksum tracking and atomic execution.

.DESCRIPTION
    Wraps an input migration SQL file in an atomic transaction:
    - Calculates full file SHA-256 checksum of the migration artifact.
    - Validates that the migration version does not exist in SCHEMA_MIGRATIONS.
    - If the same version exists with a different checksum, aborts immediately.
    - Generates a temporary wrapper executing:
        SET AUTODDL OFF;
        INPUT '<MigrationFile>';
        INSERT INTO SCHEMA_MIGRATIONS (VERSION_NO, MIGRATION_NAME, CHECKSUM_VALUE, APPLIED_AT) ...
        COMMIT;
    - Runs isql.exe with -b (bail on error).
    - Cleans up temporary wrapper.
    - Preserves security: reads credentials from environment, never prints password.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$MigrationPath,

    [Parameter(Position = 1)]
    [string]$Database = "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb",

    [Parameter()]
    [string]$IsqlPath
)

$ErrorActionPreference = "Stop"

# 1. Resolve migration artifact path
if (-not [System.IO.Path]::IsPathRooted($MigrationPath)) {
    $MigrationPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $MigrationPath))
}

if (-not (Test-Path -Path $MigrationPath -PathType Leaf)) {
    Write-Error "Migration file not found: $MigrationPath"
    exit 1
}

# 2. Compute full artifact SHA-256
$fullArtifactHash = (Get-FileHash -Path $MigrationPath -Algorithm SHA256).Hash.ToUpperInvariant()

# 3. Parse version number and migration name from filename (format: 0030__supplier_core.sql)
$fileName = [System.IO.Path]::GetFileNameWithoutExtension($MigrationPath)
if ($fileName -match "^0*(\d+)__(.+)$") {
    $versionNo = [int]$matches[1]
    $migrationName = $matches[2].ToUpperInvariant()
} else {
    Write-Error "Migration filename must match format '<VERSION>__<name>.sql', got: $fileName"
    exit 1
}

# 4. Resolve isql.exe
if ([string]::IsNullOrWhiteSpace($IsqlPath)) {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
    $bundledIsql = Join-Path $repoRoot "firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe"
    if (Test-Path $bundledIsql) {
        $IsqlPath = $bundledIsql
    } else {
        $IsqlPath = "isql.exe"
    }
}

# 5. Check credentials from environment (never echo passwords)
$dbUser = if ($env:ISC_USER) { $env:ISC_USER } else { "SYSDBA" }
$dbPassword = if ($env:KAZUKO_FIREBIRD_PASSWORD) { $env:KAZUKO_FIREBIRD_PASSWORD } else { $env:ISC_PASSWORD }

if ([string]::IsNullOrWhiteSpace($dbPassword)) {
    Write-Error "KAZUKO_FIREBIRD_PASSWORD or ISC_PASSWORD environment variable is required."
    exit 1
}

$env:ISC_USER = $dbUser
$env:ISC_PASSWORD = $dbPassword

# 6. Check existing version and checksum in SCHEMA_MIGRATIONS
$checkQuery = "SELECT VERSION_NO, CHECKSUM_VALUE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = $versionNo;"
$checkOutput = $checkQuery | & $IsqlPath -q -ch UTF8 $Database 2>&1

$existingVersionFound = $false
$existingChecksum = ""

foreach ($line in ($checkOutput -split "`r?`n")) {
    $trimmed = $line.Trim()
    if ($trimmed -match "^\s*(\d+)\s+([A-Fa-f0-9]{64})\s*$") {
        if ([int]$matches[1] -eq $versionNo) {
            $existingVersionFound = $true
            $existingChecksum = $matches[2].ToUpperInvariant()
            break
        }
    }
}

if ($existingVersionFound) {
    if ($existingChecksum -eq $fullArtifactHash) {
        Write-Host "Migration $versionNo ($migrationName) is already applied with matching checksum."
        exit 0
    } else {
        Write-Error "Migration $versionNo already applied with DIFFERENT checksum! Recorded: $existingChecksum, Artifact: $fullArtifactHash. Aborting."
        exit 1
    }
}

# 7. Create temporary wrapper SQL (UTF-8 without BOM)
$tempWrapperPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "kazuko_mig_wrapper_$versionNo" + "_" + [System.Guid]::NewGuid().ToString("N") + ".sql"
)

# Normalize path for Firebird INPUT command (forward slashes)
$normalizedMigrationPath = $MigrationPath.Replace("\", "/")

$wrapperSql = @"
SET AUTODDL OFF;
INPUT $normalizedMigrationPath;
INSERT INTO SCHEMA_MIGRATIONS (VERSION_NO, MIGRATION_NAME, CHECKSUM_VALUE, APPLIED_AT)
VALUES ($versionNo, '$migrationName', '$fullArtifactHash', CURRENT_TIMESTAMP);
COMMIT;
"@

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempWrapperPath, $wrapperSql, $utf8NoBom)

try {
    Write-Host "Applying migration $versionNo ($migrationName)..."
    Write-Host "Artifact Checksum (SHA-256): $fullArtifactHash"

    # 8. Execute isql with bail on error (-b)
    & $IsqlPath -b -ch UTF8 -i $tempWrapperPath $Database
    $isqlExitCode = $LASTEXITCODE

    if ($isqlExitCode -ne 0) {
        Write-Error "Migration $versionNo ($migrationName) failed with isql exit code $isqlExitCode."
        exit $isqlExitCode
    }

    Write-Host "Migration $versionNo ($migrationName) applied successfully and recorded in SCHEMA_MIGRATIONS."
    exit 0
}
finally {
    # 9. Always clean up temporary wrapper script
    if (Test-Path $tempWrapperPath) {
        Remove-Item -Force $tempWrapperPath
    }
}
