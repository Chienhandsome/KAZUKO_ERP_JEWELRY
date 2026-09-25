<#
.SYNOPSIS
    Applies a single Firebird schema migration script with full artifact SHA-256 checksum tracking,
    hardened validation, and automated snapshot-based atomic rollback.

.DESCRIPTION
    Wraps an input migration SQL file in an atomic execution pipeline:
    - Calculates full file SHA-256 checksum of the migration artifact as-is.
    - Validates migration filename: <VERSION>__<name>.sql where name matches ^[A-Za-z0-9_]+$.
    - Checks whether the version already exists in SCHEMA_MIGRATIONS with exit code verification.
    - If identical checksum exists: skips and reports already applied (idempotent).
    - If different checksum exists: aborts with error immediately.
    - Creates a pre-migration physical snapshot backup via gbak.
    - Generates a temporary execution script managing Firebird DDL/DML boundaries under SET AUTODDL OFF.
    - Executes isql.exe with bail on error (-b) and quoted paths.
    - On ANY error during execution (DDL failure, Seed failure, Verification failure, Ledger failure):
      automatically RESTORES the database to the clean pre-migration snapshot, leaving zero partial schema.
    - On success: cleans up snapshot and wrapper script, records full artifact SHA-256 in SCHEMA_MIGRATIONS.
    - Preserves security: reads credentials from environment, never prints password.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$MigrationPath,

    [Parameter(Position = 1)]
    [string]$Database = "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb",

    [Parameter()]
    [string]$IsqlPath,

    [Parameter()]
    [string]$GbakPath
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
if ($fullArtifactHash -notmatch "^[A-FA-F0-9]{64}$") {
    Write-Error "Failed to calculate valid SHA-256 hash for artifact."
    exit 1
}

# 3. Parse and strictly validate version number and migration name from filename
$fileName = [System.IO.Path]::GetFileNameWithoutExtension($MigrationPath)
if ($fileName -match "^0*(\d+)__([A-Za-z0-9_]+)$") {
    $versionNo = [int]$matches[1]
    $migrationName = $matches[2].ToUpperInvariant()
} else {
    Write-Error "Migration filename must match format '<VERSION>__<name>.sql' where name contains only [A-Za-z0-9_], got: $fileName"
    exit 1
}

# 4. Resolve isql.exe and gbak.exe
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
if ([string]::IsNullOrWhiteSpace($IsqlPath)) {
    $bundledIsql = Join-Path $repoRoot "firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe"
    if (Test-Path $bundledIsql) {
        $IsqlPath = $bundledIsql
    } else {
        $IsqlPath = "isql.exe"
    }
}

if ([string]::IsNullOrWhiteSpace($GbakPath)) {
    $isqlDir = Split-Path -Parent $IsqlPath
    $bundledGbak = Join-Path $isqlDir "gbak.exe"
    if (Test-Path $bundledGbak) {
        $GbakPath = $bundledGbak
    } else {
        $GbakPath = "gbak.exe"
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

# 6. Check existing version and checksum in SCHEMA_MIGRATIONS with exit code verification
$checkQuery = "SELECT VERSION_NO, CHECKSUM_VALUE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = $versionNo;"
$checkOutput = $checkQuery | & $IsqlPath -q -ch UTF8 $Database 2>&1
$checkExitCode = $LASTEXITCODE

if ($checkExitCode -ne 0) {
    Write-Error "Failed to query SCHEMA_MIGRATIONS (isql exit code $checkExitCode): $checkOutput"
    exit $checkExitCode
}

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

# 7. Create pre-migration backup snapshot for 100% atomicity guarantee
$tempSnapshotPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "kazuko_snap_$versionNo" + "_" + [System.Guid]::NewGuid().ToString("N") + ".fbk"
)

Write-Host "Creating pre-migration snapshot for rollback safety..."
& $GbakPath -b -g -user $dbUser -password $dbPassword $Database $tempSnapshotPath 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create pre-migration snapshot backup with gbak (exit code $LASTEXITCODE). Aborting migration."
    exit 1
}

# 8. Prepare wrapper SQL
# Firebird engine architecture requires metadata changes (DDL) to commit before DML can compile against new relations.
# To keep the migration artifact clean (0 COMMITs in artifact) and provide true atomicity,
# the runner injects the DDL/DML transaction boundary in the temporary execution wrapper,
# and backs it with snapshot restore on ANY failure.
$tempWrapperPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "kazuko_mig_wrapper_$versionNo" + "_" + [System.Guid]::NewGuid().ToString("N") + ".sql"
)

$artifactRawContent = [System.IO.File]::ReadAllText($MigrationPath, [System.Text.Encoding]::UTF8)

# Check if artifact has a Seed section boundary
$seedMarker = "-- Step 6: Seed"
if ($artifactRawContent.Contains($seedMarker)) {
    $markerIndex = $artifactRawContent.IndexOf($seedMarker)
    $ddlPart = $artifactRawContent.Substring(0, $markerIndex).TrimEnd()
    $dmlPart = $artifactRawContent.Substring($markerIndex)

    $wrapperSql = @"
$ddlPart

-- Publish DDL schema changes so that subsequent DML can compile against new tables
COMMIT;

$dmlPart

INSERT INTO SCHEMA_MIGRATIONS (VERSION_NO, MIGRATION_NAME, CHECKSUM_VALUE, APPLIED_AT)
VALUES ($versionNo, '$migrationName', '$fullArtifactHash', CURRENT_TIMESTAMP);
COMMIT;
"@
} else {
    $normalizedMigrationPath = $MigrationPath.Replace("\", "/")
    $wrapperSql = @"
SET AUTODDL OFF;
INPUT "$normalizedMigrationPath";
INSERT INTO SCHEMA_MIGRATIONS (VERSION_NO, MIGRATION_NAME, CHECKSUM_VALUE, APPLIED_AT)
VALUES ($versionNo, '$migrationName', '$fullArtifactHash', CURRENT_TIMESTAMP);
COMMIT;
"@
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempWrapperPath, $wrapperSql, $utf8NoBom)

$migrationSucceeded = $false

try {
    Write-Host "Applying migration $versionNo ($migrationName)..."
    Write-Host "Artifact Checksum (SHA-256): $fullArtifactHash"

    # 9. Execute isql with bail on error (-b)
    & $IsqlPath -b -ch UTF8 -i $tempWrapperPath $Database
    $isqlExitCode = $LASTEXITCODE

    if ($isqlExitCode -ne 0) {
        throw "Migration $versionNo ($migrationName) failed with isql exit code $isqlExitCode."
    }

    $migrationSucceeded = $true
    Write-Host "Migration $versionNo ($migrationName) applied successfully and recorded in SCHEMA_MIGRATIONS."
    exit 0
}
catch {
    Write-Warning "Migration execution failed: $_"
    Write-Warning "Triggering automated atomic rollback: restoring database from pre-migration snapshot..."

    & $GbakPath -c -rep -user $dbUser -password $dbPassword $tempSnapshotPath $Database 2>&1 | Out-Null
    $restoreExitCode = $LASTEXITCODE

    if ($restoreExitCode -ne 0) {
        Write-Error "CRITICAL: Database restore failed with gbak exit code $restoreExitCode! Snapshot preserved at: $tempSnapshotPath"
        exit 1
    } else {
        Write-Host "Database successfully restored to clean pre-migration state. Zero partial schema left behind."
    }

    exit 1
}
finally {
    # 10. Clean up temporary files
    if (Test-Path $tempWrapperPath) {
        Remove-Item -Force $tempWrapperPath
    }
    if ($migrationSucceeded -and (Test-Path $tempSnapshotPath)) {
        Remove-Item -Force $tempSnapshotPath
    }
}
