# Manual test — Sales checkout MVP

Disposable Firebird copy only. **Never** open or overwrite `data\data.fdb`.

## Prerequisites

- Worktree: `D:\Working\KAZUKO_ERP_JEWELRY\.worktrees\sales-checkout`
- Branch: `feature/sales-checkout`
- Firebird 5 client (bundled):  
  `D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64`
- Fixture (schema v51):  
  `.superpowers\sdd\2026-10-05-sales-checkout\task3-fixtures\sales_checkout_revised_v51_final.fdb`

## 1. Create / refresh the manual-test database

From the worktree root:

```powershell
cd D:\Working\KAZUKO_ERP_JEWELRY\.worktrees\sales-checkout

$env:KAZUKO_FIREBIRD_HOME = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64"
$env:KAZUKO_FIREBIRD_PASSWORD = "masterkey"

.\database\tools\Prepare-SalesCheckoutManualTestDb.ps1
```

This copies the fixture to:

`data\backups\sales_checkout_manual_test.fdb`

then seeds:

| Item | Value |
|------|--------|
| Login | `admin` / `mvp-test` |
| Branch | `CN_TRUNG_TAM` |
| Sellable products | `TP-CN-2026-000003`, `TP-CN-2026-000004`, `TP-CN-2026-000005` (DISPLAYED on `KHO_BAN_HANG`, retail price `750`) |
| Walk-in customer | `KH-VL` |
| Named customers | `KH000001` … |
| Old-gold materials | e.g. `GOLD_750`, `GOLD_9999` (have `OLD_GOLD_PRICE_ID`) |
| Cash fund | existing `QTM` on branch |
| Bank account | `MVP_BANK` |

Re-run the script any time to wipe and recreate the disposable copy.

Optional password override:

```powershell
.\database\tools\Prepare-SalesCheckoutManualTestDb.ps1 -LoginPassword "your-temp-password"
```

## 2. Point the app at the test DB (do not hard-code)

The app loads `KAZUKO_FIREBIRD_*` env vars, but **`%LocalAppData%\KazukoERPJewelry\connection.json` overrides `DatabasePath` if present**.

Before launch:

```powershell
$manualDb = "D:\Working\KAZUKO_ERP_JEWELRY\.worktrees\sales-checkout\data\backups\sales_checkout_manual_test.fdb"
$profilePath = Join-Path $env:LOCALAPPDATA "KazukoERPJewelry\connection.json"

# Backup any existing profile so you can restore after testing
if (Test-Path $profilePath) {
    Copy-Item $profilePath "$profilePath.mvp-backup" -Force
}

New-Item -ItemType Directory -Force -Path (Split-Path $profilePath) | Out-Null
@{
    Host = "localhost"
    Port = 3050
    DatabasePath = $manualDb
    UserName = "SYSDBA"
    UseEmbedded = $true
} | ConvertTo-Json | Set-Content -Path $profilePath -Encoding UTF8

$env:KAZUKO_FIREBIRD_HOME = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64"
$env:KAZUKO_FIREBIRD_PASSWORD = "masterkey"
$env:KAZUKO_FIREBIRD_DATABASE = $manualDb
$env:KAZUKO_FIREBIRD_EMBEDDED = "1"
```

Restore the previous profile when finished:

```powershell
$profilePath = Join-Path $env:LOCALAPPDATA "KazukoERPJewelry\connection.json"
if (Test-Path "$profilePath.mvp-backup") {
    Move-Item "$profilePath.mvp-backup" $profilePath -Force
}
```

## 3. Launch

```powershell
cd D:\Working\KAZUKO_ERP_JEWELRY\.worktrees\sales-checkout
dotnet run --project src\Kazuko.App\Kazuko.App.csproj
```

Sign in:

- User: `admin`
- Password: `mvp-test` (unless overridden when preparing the DB)
- Branch: **Chi nhánh trung tâm** (`CN_TRUNG_TAM`)

Open **BÁN HÀNG** from the sidebar/home.

## 4. Smoke matrix (MVP)

Hướng dẫn dưới đây giữ fixture MVP cũ; với build hiện tại, bill là A5 ngang/dọc, nút **In hóa đơn** ở tab Hóa đơn. Không còn nút In A4 ở header Bán hàng. In dùng máy mặc định, không mở PrintDialog.

Check each row; mark Pass/Fail in your notes (no screenshots required in repo).

| # | Scenario | Steps | Expect |
|---|----------|-------|--------|
| 1 | SALE cash | Customer `KH-VL` or named; scan `TP-CN-2026-000003`; Hoàn tất → CASH full | Checkout posted; product becomes SOLD; form clears / success shown |
| 2 | SALE mixed | Named customer; product; CASH + BANK_TRANSFER (+ CARD optional) summing to net | Settlement allocations match; no error |
| 3 | SALE receivable | Named customer; empty cash; receivable with debt permission | Receivable posted; walk-in must be rejected for debt |
| 4 | TRADE_IN | Named customer; one sale line + one old-gold line; pay/receive net | Offset once; net receipt or payout as calculated |
| 5 | OLD_GOLD only | Tab mua vàng cũ; named customer; payout CASH/BANK only | Posted; CARD payout rejected by dialog |
| 6 | Fail preserve | Wrong payment total or unavailable product; post fails | Inputs preserved; can correct and retry |
| 7 | Double submit | Rapid double click Hoàn tất | One post only (command busy / single flight) |
| 8 | Theme/font | Light/Dark; font 100% and 125%; resize window | Layout usable; no hard-coded crash |
| 9 | Xem/in hóa đơn A5 | Sau thanh toán hoặc Hóa đơn → **In hóa đơn** | Xem trước A5 ngang/dọc; In thẳng máy mặc định, fit/center/auto-rotate; Đóng không đổi checkout |

Out of MVP (skip): e-invoice, draft/hold, hủy/đảo, PIECE/WEIGHT, tiền thối.

## 5. Automated gate (optional before manual)

```powershell
cd D:\Working\KAZUKO_ERP_JEWELRY\.worktrees\sales-checkout
$env:KAZUKO_FIREBIRD_HOME = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64"

dotnet test tests\Kazuko.Modules.Sales.Tests\Kazuko.Modules.Sales.Tests.csproj
dotnet test tests\Kazuko.Infrastructure.Tests\Kazuko.Infrastructure.Tests.csproj --filter FullyQualifiedName~Sales
```

Known baseline: eight `INVENTORY_SCOPE` failures elsewhere in the suite are pre-existing and unrelated to sales checkout.

## 6. Safety checklist

- [ ] Manual DB path is under `data\backups\`, not `data\data.fdb`
- [ ] Live DB was not opened by the prepare script
- [ ] After testing, connection profile restored or pointed back to the intended DB
- [ ] Migration 51 was not edited
