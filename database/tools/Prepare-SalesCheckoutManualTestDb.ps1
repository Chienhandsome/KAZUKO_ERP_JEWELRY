<#
.SYNOPSIS
    Creates/refreshes the disposable sales-checkout manual-test Firebird database.

.DESCRIPTION
    Copies the schema-v51 fixture into data\backups\sales_checkout_manual_test.fdb
    (never touches data\data.fdb), then seeds sellable products, a bank account,
    and resets the disposable admin login password for UI manual tests.
#>

[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$OutputDatabase,
    [string]$LoginPassword = "mvp-test",
    [string]$FirebirdHome
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}

$fixture = Join-Path $RepoRoot ".superpowers\sdd\2026-10-05-sales-checkout\task3-fixtures\sales_checkout_revised_v51_final.fdb"
if (-not (Test-Path -LiteralPath $fixture)) {
    throw "Fixture not found: $fixture"
}

if ([string]::IsNullOrWhiteSpace($OutputDatabase)) {
    $OutputDatabase = Join-Path $RepoRoot "data\backups\sales_checkout_manual_test.fdb"
}

$liveDatabase = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "data\data.fdb"))
$OutputDatabase = [System.IO.Path]::GetFullPath($OutputDatabase)
if ([string]::Equals($OutputDatabase, $liveDatabase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to overwrite live database: $liveDatabase"
}

if ([string]::IsNullOrWhiteSpace($FirebirdHome)) {
    if (-not [string]::IsNullOrWhiteSpace($env:KAZUKO_FIREBIRD_HOME) -and (Test-Path -LiteralPath $env:KAZUKO_FIREBIRD_HOME)) {
        $FirebirdHome = $env:KAZUKO_FIREBIRD_HOME
    } else {
        $candidate = Join-Path $RepoRoot "firebird\Firebird-5.0.4.1812-0-windows-x64"
        if (Test-Path -LiteralPath $candidate) {
            $FirebirdHome = $candidate
        } else {
            $mainRepoCandidate = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64"
            if (Test-Path -LiteralPath $mainRepoCandidate) {
                $FirebirdHome = $mainRepoCandidate
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($FirebirdHome)) {
    throw "Set KAZUKO_FIREBIRD_HOME or pass -FirebirdHome."
}

$clientLibrary = Join-Path $FirebirdHome "fbclient.dll"
if (-not (Test-Path -LiteralPath $clientLibrary)) {
    throw "fbclient.dll not found under Firebird home: $FirebirdHome"
}

$backupDir = Split-Path -Parent $OutputDatabase
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

if (Test-Path -LiteralPath $OutputDatabase) {
    Remove-Item -LiteralPath $OutputDatabase -Force
}

Copy-Item -LiteralPath $fixture -Destination $OutputDatabase -Force

$seedProject = Join-Path $PSScriptRoot "SalesCheckoutManualSeed\SalesCheckoutManualSeed.csproj"
$env:KAZUKO_FIREBIRD_HOME = $FirebirdHome
if ([string]::IsNullOrWhiteSpace($env:KAZUKO_FIREBIRD_PASSWORD)) {
    $env:KAZUKO_FIREBIRD_PASSWORD = "masterkey"
}

Write-Host "Preparing $OutputDatabase"
dotnet run --project $seedProject -- $OutputDatabase $clientLibrary $LoginPassword
if ($LASTEXITCODE -ne 0) {
    throw "Seed failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Manual-test DB ready."
Write-Host "  Database : $OutputDatabase"
Write-Host "  User     : admin"
Write-Host "  Password : $LoginPassword"
Write-Host "  Branch   : CN_TRUNG_TAM"
Write-Host "Launch steps are in docs/manual-tests/sales-checkout-mvp.md"
