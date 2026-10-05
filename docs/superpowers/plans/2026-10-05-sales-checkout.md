# Sales Checkout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến mock UI Bán hàng thành luồng ghi sổ nguyên tử cho bán thành phẩm, bán kèm thu đổi và mua vàng cũ, có thanh toán hỗn hợp, công nợ và in A4.

**Architecture:** `Kazuko.Core.Sales` định nghĩa model, phép tính, validation và contract; `Kazuko.Infrastructure.Sales` truy vấn và ghi toàn bộ checkout trong một Firebird transaction; `Kazuko.Modules.Sales` chỉ điều phối UI/MVVM và in. Dữ liệu bán, kho, tiền và công nợ được liên kết qua `SALES_CHECKOUTS`, idempotency key và các chứng từ con hiện có.

**Tech Stack:** C# 12, .NET 8, WPF/MVVM, Firebird 5, `FirebirdSql.Data.FirebirdClient`, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-10-05-sales-checkout-design.md`

## Global Constraints

- Target `net8.0-windows`; Core không tham chiếu WPF, Firebird hoặc module nghiệp vụ.
- Luồng dependency giữ nguyên: View → ViewModel → Application Service → Repository/Firebird.
- View/ViewModel không chứa SQL; module nghiệp vụ không tham chiếu trực tiếp module khác.
- Mọi ghi sổ checkout dùng đúng một Firebird connection/transaction và rollback toàn bộ khi lỗi trước commit.
- Migration forward-only, không sửa migration đã chạy; executor phải xác nhận `MAX(SCHEMA_MIGRATIONS.VERSION_NO) = 50` trên bản copy DB trước khi tạo migration 51, nếu khác thì dừng và đánh lại số file.
- Không apply migration vào DB thật nếu ứng dụng chưa đóng, chưa backup và chưa restore thử trên bản copy.
- UI dùng `DynamicResource`, chạy ở font 90–150%, DPI 100–200%; nút có chiều cao tối thiểu 40 px.
- Chỉ in A4 trong giai đoạn này; tab hóa đơn điện tử bị ẩn.
- Không triển khai trả hàng, đảo/hủy chứng từ, giữ đơn, lưu nháp hoặc đơn đặt chế tác.
- Không sửa hoặc commit các thay đổi Production/Sales XAML đang tồn tại ngoài phần cần thiết cho kế hoạch này.

## Review Focus

- Replay cùng `CLIENT_REQUEST_ID` nhưng payload khác phải bị từ chối, không được trả nhầm checkout cũ — khóa bằng test integration ở Task 4.
- Khách hàng bị khóa hoặc thành phẩm bị đổi chi nhánh/trạng thái sau khi UI tải phải làm toàn transaction thất bại, không để lại checkout — khóa bằng test integration ở Task 4 và Task 5.
- Tổng nhiều dòng vàng cũ phải là tổng của từng dòng đã làm tròn, không làm tròn lại từ số liệu gộp — khóa bằng unit test ở Task 1.
- `CARD` chỉ hợp lệ cho khoản thu và phải dùng tài khoản ngân hàng; khoản chi bằng thẻ phải bị từ chối — khóa bằng validator/integration test ở Task 1 và Task 5.
- Phiếu A4 có nhiều dòng phải ngắt trang, lặp header bảng và không cắt mất tổng/chữ ký — khóa bằng paginator test và kiểm tra render ở Task 8.

---

## File Map

### Core

- `src/Kazuko.Core/Sales/SalesPermissions.cs` — permission keys và checkout/status constants.
- `src/Kazuko.Core/Sales/SalesModels.cs` — draft/result/lookup/history/print DTO.
- `src/Kazuko.Core/Sales/SalesCheckoutCalculator.cs` — công thức vàng cũ và tổng ròng.
- `src/Kazuko.Core/Sales/SalesCheckoutValidator.cs` — validation thuần, không truy cập DB.
- `src/Kazuko.Core/Sales/SalesContracts.cs` — interface application/query/authorization/print data.

### Infrastructure

- `src/Kazuko.Infrastructure/Sales/FirebirdSalesAuthorizationService.cs` — đọc `ROLE_PERMISSIONS`.
- `src/Kazuko.Infrastructure/Sales/FirebirdSalesQueryRepository.cs` — lookup và lịch sử chỉ đọc.
- `src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs` — helper ghi đơn bán, xuất thành phẩm, nhập vàng cũ, offset, tiền và nợ trong transaction hiện hành.
- `src/Kazuko.Infrastructure/Sales/FirebirdSalesCheckoutApplicationService.cs` — transaction boundary, idempotency và orchestration.
- `src/Kazuko.Infrastructure/Sales/FirebirdSalesPrintDataService.cs` — snapshot dữ liệu phiếu A4.
- `src/Kazuko.Infrastructure/Warehouse/FinishedGoodsPostingSupport.cs` — primitive xuất thành phẩm dùng chung; `FirebirdFinishedGoodsIssueService` được refactor dùng primitive này.

### WPF module

- `src/Modules/Kazuko.Modules.Sales/SalesViewModel.cs` — coordinator, toolbar, history và checkout command.
- `src/Modules/Kazuko.Modules.Sales/BuySellWorkspaceViewModel.cs` — bán/thu đổi.
- `src/Modules/Kazuko.Modules.Sales/BuyOldGoldWorkspaceViewModel.cs` — mua vàng cũ.
- `src/Modules/Kazuko.Modules.Sales/SalesRowViewModels.cs` — dòng bán, vàng cũ, payment và option rows.
- `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesPaymentDialog*` — phân bổ thu/chi và công nợ.
- `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesReceiptPreviewDialog*` — preview A4.
- `src/Modules/Kazuko.Modules.Sales/Services/SalesDialogServices.cs` — abstraction dialog để test ViewModel.
- `src/Modules/Kazuko.Modules.Sales/Services/SalesReceiptPrintService.cs` — tạo `FlowDocument` A4 và paginator.

### Tests

- Logic và integration Firebird ở `tests/Kazuko.Infrastructure.Tests/Sales/`.
- ViewModel/WPF ở project mới `tests/Kazuko.Modules.Sales.Tests/` target `net8.0-windows`.

---

### Task 1: Core sales model, calculator, validator và CARD

**Files:**
- Create: `src/Kazuko.Core/Sales/SalesPermissions.cs`
- Create: `src/Kazuko.Core/Sales/SalesModels.cs`
- Create: `src/Kazuko.Core/Sales/SalesCheckoutCalculator.cs`
- Create: `src/Kazuko.Core/Sales/SalesCheckoutValidator.cs`
- Create: `src/Kazuko.Core/Sales/SalesContracts.cs`
- Modify: `src/Kazuko.Core/Finance/FinanceContracts.cs`
- Modify: `src/Kazuko.Core/Finance/PaymentAllocationValidator.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesCheckoutCalculatorTests.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesCheckoutValidatorTests.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Finance/FinancialVoucherValidatorTests.cs`

**Interfaces:**
- Consumes: `AuthenticatedSession`, `PaymentAllocationDraft`.
- Produces:
  - `SalesLineDraft(long FinishedProductId, decimal AppliedGoldPricePerChi, decimal AppliedLaborAmountVnd, decimal AppliedStoneAmountVnd, string? OverrideReason)`
  - `OldGoldLineDraft(long MaterialId, decimal ItemQuantity, decimal TotalWeightChi, decimal StoneWeightChi, decimal ImpurityWeightChi, decimal ValuePercent, decimal AppliedBuyPricePerChi, string? OverrideReason, string? Notes)`
  - `SalesCheckoutDraft(Guid ClientRequestId, DateTime DocumentDate, long CustomerId, long SalespersonId, long SourceLocationId, long? OldGoldDestinationLocationId, decimal DiscountAmountVnd, IReadOnlyList<SalesLineDraft> SaleLines, IReadOnlyList<OldGoldLineDraft> OldGoldLines, IReadOnlyList<PaymentAllocationDraft> Allocations, decimal ReceivableAmountVnd, string? Notes)`
  - `SalesCheckoutTotals(decimal SaleGrossVnd, decimal DiscountAmountVnd, decimal SaleNetVnd, decimal OldGoldTotalVnd, decimal OffsetAmountVnd, decimal NetAmountVnd, decimal ReceiptAmountVnd, decimal PayoutAmountVnd, decimal ReceivableAmountVnd)`
  - `SalesPermissionSnapshot(bool CanPost, bool CanOverridePrice, bool CanApproveDebt, bool CanChangeSalesperson, bool CanReadHistory, bool CanPrintReceipt)`
  - `SalesValidationContext(bool IsWalkInCustomer, long CurrentUserId, SalesPermissionSnapshot Permissions)`
  - `SalesCheckoutCalculator.CalculateOldGold(OldGoldLineDraft line) -> OldGoldLineAmounts`
  - `SalesCheckoutCalculator.CalculateTotals(SalesCheckoutDraft draft, IReadOnlyList<OldGoldLineAmounts> oldGold) -> SalesCheckoutTotals`
  - `SalesCheckoutValidator.Validate(SalesCheckoutDraft draft, SalesCheckoutTotals totals, SalesValidationContext context) -> IReadOnlyList<string>`
  - `ISalesCheckoutApplicationService.PostAsync(SalesCheckoutDraft draft, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<SalesCheckoutResult>`
  - `ISalesQueryRepository.SearchCustomersAsync(string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<IReadOnlyList<SalesCustomerOption>>`
  - `ISalesQueryRepository.GetSalespeopleAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<IReadOnlyList<SalespersonOption>>`
  - `ISalesQueryRepository.FindAvailableProductAsync(string productCode, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<SalesProductOption?>`
  - `ISalesQueryRepository.GetOldGoldMaterialsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<IReadOnlyList<OldGoldMaterialOption>>`
  - `ISalesQueryRepository.GetOldGoldLocationsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<IReadOnlyList<SalesLocationOption>>`
  - `ISalesQueryRepository.GetCheckoutAsync(long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<SalesCheckoutDetail?>`
  - `ISalesQueryRepository.GetAdjacentCheckoutIdAsync(long checkoutId, bool next, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<long?>`
  - `ISalesAuthorizationService.GetPermissionsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<SalesPermissionSnapshot>`
  - `ISalesPrintDataService.GetReceiptAsync(long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default) -> Task<SalesReceiptDto>`
  - `PaymentMethods.Card = "CARD"`.

- [ ] **Step 1: Viết test thất bại cho công thức và làm tròn**

Thêm các test `CalculateOldGold_subtracts_stone_and_impurity_then_converts`, `CalculateOldGold_rounds_weight_to_four_decimals_and_money_away_from_zero`, `CalculateTotals_sums_already_rounded_old_gold_lines`, và ba case net dương/0/âm. Assert công thức trong spec, bao gồm `3.2 - 0.1 - 0.1 = 3.0`, quy đổi 75% thành `2.25` chỉ.

- [ ] **Step 2: Chạy test calculator để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj --filter FullyQualifiedName~SalesCheckoutCalculatorTests`

Expected: FAIL vì namespace/type `Kazuko.Core.Sales` chưa tồn tại.

- [ ] **Step 3: Tạo model và calculator tối thiểu**

Khai báo chính xác các record `SalesLineDraft`, `OldGoldLineDraft`, `OldGoldLineAmounts`, `SalesCheckoutDraft`, `SalesCheckoutTotals`, `SalesCheckoutResult`, lookup/history/receipt DTO. `SalesCheckoutDraft` chứa `Guid ClientRequestId`, `DateTime DocumentDate`, customer/salesperson/source/destination ids, discount, sale lines, old-gold lines, allocations, receivable amount và notes.

- [ ] **Step 4: Chạy test calculator để xác nhận xanh**

Run: lệnh ở Step 2.

Expected: PASS toàn bộ `SalesCheckoutCalculatorTests`.

- [ ] **Step 5: Viết test thất bại cho validation và CARD**

Test tên cụ thể: `Rejects_walk_in_customer_for_old_gold`, `Rejects_debt_without_named_customer_or_permission`, `Rejects_override_without_reason_or_permission`, `Rejects_changed_salesperson_without_permission`, `Rejects_card_for_outgoing_payment`, `Requires_bank_account_for_card`, `Rejects_negative_or_overdrawn_weights`, `Rejects_payment_mismatch`.

- [ ] **Step 6: Chạy validator tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SalesCheckoutValidatorTests|FullyQualifiedName~FinancialVoucherValidatorTests"`

Expected: FAIL vì validator/hằng CARD chưa được triển khai.

- [ ] **Step 7: Implement validator, permission/constants và contracts**

Định nghĩa `SalesCheckoutTypes` (`SALE`, `TRADE_IN`, `OLD_GOLD_PURCHASE`), sáu permission trong spec và `SalesValidationContext`. Mở rộng `PaymentAllocationValidator` để `CARD` dùng `BankAccountId`, không dùng `CashFundId`; validator sales từ chối CARD khi `totals.NetAmount < 0`.

- [ ] **Step 8: Chạy toàn bộ unit tests liên quan**

Run: lệnh ở Step 6.

Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/Kazuko.Core/Sales src/Kazuko.Core/Finance tests/Kazuko.Infrastructure.Tests/Sales tests/Kazuko.Infrastructure.Tests/Finance/FinancialVoucherValidatorTests.cs
git commit -m "feat: define sales checkout domain"
```

### Task 2: Migration 51 cho checkout, snapshot, offset và CARD

**Files:**
- Create: `database/migrations/0051__sales_checkout_core.sql`
- Create: `database/verification/verify_sales_checkout_core.sql`
- Modify: `docs/05-DATABASE-MIGRATIONS.md`

**Interfaces:**
- Consumes: tables `SALES_ORDERS`, `SALES_ORDER_ITEMS`, `ORDER_PAYMENTS`, `STOCK_DOCUMENTS`, `STOCK_DOCUMENT_LINES`, `FINANCIAL_SETTLEMENTS`, `RECEIVABLE_DEBTS`, `CASH_VOUCHERS`, `PERMISSIONS`, `ROLE_PERMISSIONS`.
- Produces: `SALES_CHECKOUTS`, `SALES_OLD_GOLD_LINES`, `SALES_CHECKOUT_OFFSETS`; canonical sequences `SALES_CHECKOUT` and `SALES_OLD_GOLD_PURCHASE`; schema columns and constraints used by Tasks 3–8.

- [ ] **Step 1: Xác nhận version trên bản copy DB**

Run query: `SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS;`

Expected: `50`. Nếu khác, dừng Task 2 và đổi đồng bộ tên/version migration cùng tài liệu trước khi viết SQL.

- [ ] **Step 2: Viết verification script trước**

Script fail-fast phải kiểm tra version 51; ba bảng mới; mọi PK/FK/unique/check/index; `CLIENT_REQUEST_ID`, `REQUEST_HASH`, bốn cột totals; hai sequence; sáu permission; các snapshot columns; `CARD` trong constraint của `CASH_VOUCHERS`, `STOCK_DOCUMENT_PAYMENTS`, `FINANCIAL_SETTLEMENT_ALLOCATIONS`, `PAYABLE_DEBT_PAYMENT_ALLOCATIONS` và `ORDER_PAYMENTS`; linkage mới trên `ORDER_PAYMENTS`.

- [ ] **Step 3: Chạy verification trên bản copy chưa migrate để xác nhận đỏ**

Run: `& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe" -b -ch UTF8 -i "database\verification\verify_sales_checkout_core.sql" "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\backups\sales_checkout_verify.fdb"`

Expected: FAIL tại kiểm tra migration/table đầu tiên; DB thật không bị chạm.

- [ ] **Step 4: Viết migration 51 atomic**

`SALES_CHECKOUTS` có type/status check, signed `NET_AMOUNT_VND`, unique `(BRANCH_ID, CLIENT_REQUEST_ID)`, `REQUEST_HASH CHAR(64)`, child ids và audit fields. `SALES_OLD_GOLD_LINES` lưu toàn bộ input/derived/source/applied price, `STOCK_DOCUMENT_LINE_ID`, constraints trọng lượng và unique line. `SALES_CHECKOUT_OFFSETS` unique checkout, liên kết sales order/stock document, amount > 0. Thêm `FINISHED_PRODUCT_ID`, source/applied snapshot và override reason vào `SALES_ORDER_ITEMS`; thêm settlement/voucher linkage vào `ORDER_PAYMENTS`; mở rộng purpose `RETAIL_SALE` và CARD constraints. Seed permission/sequence idempotent. Không có `COMMIT` trong artifact; có rollback notes ở header.

- [ ] **Step 5: Apply migration bằng runner trên bản copy**

Run: `powershell -File database/tools/Invoke-KazukoMigration.ps1 -MigrationPath database/migrations/0051__sales_checkout_core.sql -Database "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\backups\sales_checkout_verify.fdb"`

Expected: migration 51 applied, runner ghi checksum và không để snapshot tạm.

- [ ] **Step 6: Chạy verification để xác nhận xanh**

Run: lệnh Step 3 trên DB copy đã migrate.

Expected: exit code 0, không có `EX_VERIFY_FAILED`.

- [ ] **Step 7: Cập nhật tài liệu migration**

Thêm section version 51 vào `docs/05-DATABASE-MIGRATIONS.md`: artifact, verification, bảng/cột/constraint, sequence, permission, backup/rollback và lệnh chạy trên bản copy.

- [ ] **Step 8: Commit**

```powershell
git add database/migrations/0051__sales_checkout_core.sql database/verification/verify_sales_checkout_core.sql docs/05-DATABASE-MIGRATIONS.md
git commit -m "feat: add sales checkout schema"
```

### Task 3: Firebird lookup và authorization

**Files:**
- Create: `src/Kazuko.Infrastructure/Sales/FirebirdSalesAuthorizationService.cs`
- Create: `src/Kazuko.Infrastructure/Sales/FirebirdSalesQueryRepository.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesQueryRepositoryTests.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesAuthorizationServiceTests.cs`

**Interfaces:**
- Consumes: Core contracts Task 1, schema Task 2, `AuthenticatedSession`.
- Produces: query/permission implementations for application service and ViewModels.

- [ ] **Step 1: Viết integration tests thất bại**

Seed hai chi nhánh, một khách lẻ, một khách có hồ sơ, user/role permissions, sales-floor location, old-gold material và thành phẩm. Test tìm sản phẩm chỉ trả trạng thái `IN_STOCK`/`DISPLAYED` đúng branch/sales-floor; customer/staff/material/location lookup đúng scope; history/adjacent checkout không rò branch; ADMIN có toàn quyền; role thường chỉ có permission seed.

- [ ] **Step 2: Chạy tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SalesQueryRepositoryTests|FullyQualifiedName~SalesAuthorizationServiceTests"`

Expected: FAIL vì implementation chưa tồn tại.

- [ ] **Step 3: Implement `FirebirdSalesAuthorizationService`**

`GetPermissionsAsync` đọc role hiện hành của `session.UserId` cùng `ROLE_PERMISSIONS.IS_ALLOWED`; ADMIN trả snapshot true cho sáu sales permissions. Không tin riêng `session.RoleCode` cho role thường.

- [ ] **Step 4: Implement `FirebirdSalesQueryRepository`**

Mọi query lọc `COMPANY_ID`/`BRANCH_ID`, active state và giới hạn số kết quả. `FindAvailableProductAsync` trả id/code/name/purity/weights/source location/source gold price/source labor/source stone/row version để UI hiển thị, nhưng posting vẫn tải lại.

- [ ] **Step 5: Chạy tests để xác nhận xanh**

Run: lệnh Step 2.

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Kazuko.Infrastructure/Sales/FirebirdSalesAuthorizationService.cs src/Kazuko.Infrastructure/Sales/FirebirdSalesQueryRepository.cs tests/Kazuko.Infrastructure.Tests/Sales
git commit -m "feat: add sales lookup and authorization"
```

### Task 4: Atomic sale-only checkout, receipt, debt và idempotency

**Files:**
- Create: `src/Kazuko.Infrastructure/Warehouse/FinishedGoodsPostingSupport.cs`
- Modify: `src/Kazuko.Infrastructure/Warehouse/FirebirdFinishedGoodsIssueService.cs`
- Create: `src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs`
- Create: `src/Kazuko.Infrastructure/Sales/FirebirdSalesCheckoutApplicationService.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesCheckoutSaleIntegrationTests.cs`

**Interfaces:**
- Consumes: `SalesCheckoutDraft`, calculator/validator, authorization, migration 51, `FirebirdFinancialSettlementService.PostInTransactionAsync`, voucher/debt tables.
- Produces: working `ISalesCheckoutApplicationService.PostAsync` for `SALE`; `SalesCheckoutPostingSupport.PostSaleAsync(FbConnection connection, FbTransaction transaction, long companyId, SalesCheckoutDraft draft, SalesCheckoutTotals totals, SalesPermissionSnapshot permissions, AuthenticatedSession session, CancellationToken cancellationToken) -> Task<SalePostResult>`; `PostReceiptAsync(FbConnection connection, FbTransaction transaction, long companyId, long checkoutId, SalePostResult sale, IReadOnlyList<PaymentAllocationDraft> allocations, AuthenticatedSession session, CancellationToken cancellationToken) -> Task<FinancialSettlementResult?>`; `CreateReceivableAsync(FbConnection connection, FbTransaction transaction, long companyId, long checkoutId, SalePostResult sale, decimal amountVnd, AuthenticatedSession session, CancellationToken cancellationToken) -> Task<long?>`.

- [ ] **Step 1: Viết integration tests thất bại cho sale**

Tests: `Posts_sale_and_finished_goods_issue_atomically`, `Posts_mixed_cash_bank_card_receipts`, `Creates_receivable_for_authorized_named_customer`, `Rejects_walk_in_debt`, `Rejects_customer_locked_after_lookup`, `Rolls_back_when_product_changes_branch_or_status`, `Only_one_concurrent_checkout_can_sell_product`, `Replays_same_request_and_hash_without_duplicate`, `Rejects_same_request_id_with_different_hash`, `Audits_price_override_debt_approval_and_salesperson_change`.

- [ ] **Step 2: Chạy sale integration tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.cs --filter FullyQualifiedName~SalesCheckoutSaleIntegrationTests`

Expected: FAIL vì checkout service/posting support chưa tồn tại.

- [ ] **Step 3: Extract primitive xuất thành phẩm**

Tạo `FinishedGoodsPostingSupport.PostRetailIssueAsync(FbConnection connection, FbTransaction transaction, long companyId, AuthenticatedSession session, long salesOrderId, long sourceLocationId, IReadOnlyList<long> finishedProductIds, CancellationToken cancellationToken) -> Task<long>`. Dùng `WITH LOCK`, `ROW_VERSION` và conditional update. Refactor `FirebirdFinishedGoodsIssueService` gọi primitive tương ứng mà không đổi hành vi/test hiện có.

- [ ] **Step 4: Implement request hash và idempotency**

Canonicalize toàn draft theo thứ tự dòng, format invariant và SHA-256. Cùng branch/id/hash trả checkout đã post với `AlreadyPosted=true`; cùng id khác hash ném lỗi nghiệp vụ. Unique violation do hai request đồng thời phải được đọc lại và xử lý theo cùng quy tắc.

- [ ] **Step 5: Implement sale posting trong một transaction**

Tải lại permissions/customer/salesperson/product/price; kiểm tra override; tạo `SALES_CHECKOUTS`, `SALES_ORDERS`, snapshot items, issue doc/movements; ghi settlement + vouchers + linked `ORDER_PAYMENTS`; tạo `RECEIVABLE_DEBTS` cho residual; audit; cập nhật child ids; commit. Không tạo settlement nếu receipt thực bằng 0.

- [ ] **Step 6: Chạy sale integration và regression kho/tài chính**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.cs --filter "FullyQualifiedName~SalesCheckoutSaleIntegrationTests|FullyQualifiedName~MaterialReceiptIntegrationTests|FullyQualifiedName~FinancialVoucherIntegrationTests"`

Expected: PASS, bao gồm tests cũ sau refactor primitive.

- [ ] **Step 7: Commit**

```powershell
git add src/Kazuko.Infrastructure/Warehouse src/Kazuko.Infrastructure/Sales tests/Kazuko.Infrastructure.Tests/Sales
git commit -m "feat: post atomic retail sales"
```

### Task 5: Old-gold purchase, trade-in, offset và payout

**Files:**
- Modify: `src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs`
- Modify: `src/Kazuko.Infrastructure/Sales/FirebirdSalesCheckoutApplicationService.cs`
- Modify: `src/Kazuko.Infrastructure/Customers/FirebirdCustomerRepository.cs`
- Modify: `src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesCheckoutOldGoldIntegrationTests.cs`
- Create test: `tests/Kazuko.Infrastructure.Tests/Customers/CustomerRepositoryTests.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Finance/DebtSettlementIntegrationTests.cs`

**Interfaces:**
- Consumes: service/support Task 4, stock posting helpers, voucher posting in current transaction.
- Produces: `TRADE_IN` và `OLD_GOLD_PURCHASE`; `SalesCheckoutPostingSupport.PostOldGoldReceiptAsync(FbConnection connection, FbTransaction transaction, long companyId, long checkoutId, SalesCheckoutDraft draft, IReadOnlyList<OldGoldLineAmounts> amounts, AuthenticatedSession session, CancellationToken cancellationToken) -> Task<OldGoldPostResult>`; `PostOffsetAsync(FbConnection connection, FbTransaction transaction, long checkoutId, long salesOrderId, long stockDocumentId, decimal amountVnd, CancellationToken cancellationToken) -> Task<long?>`; `PostPayoutAsync(FbConnection connection, FbTransaction transaction, long companyId, long checkoutId, OldGoldPostResult oldGold, IReadOnlyList<PaymentAllocationDraft> allocations, AuthenticatedSession session, CancellationToken cancellationToken) -> Task<IReadOnlyList<long>>`.

- [ ] **Step 1: Viết integration tests thất bại**

Tests: `Posts_trade_in_with_positive_net_receipt`, `Posts_zero_net_without_cash_voucher`, `Posts_negative_net_payout`, `Posts_standalone_old_gold_purchase`, `Creates_one_traceable_stock_line_per_old_gold_line`, `Rejects_walk_in_or_inactive_customer`, `Rejects_card_payout`, `Rolls_back_sale_when_old_gold_stock_post_fails`, `Sums_rounded_old_gold_lines`, `Customer_metrics_count_offset_once_without_cash`, `Debt_ledger_counts_offset_once_without_cash`.

- [ ] **Step 2: Chạy tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.cs --filter FullyQualifiedName~SalesCheckoutOldGoldIntegrationTests`

Expected: FAIL vì các checkout type chưa được hỗ trợ.

- [ ] **Step 3: Implement nhập kho vàng cũ trong transaction hiện hành**

Tạo posted `STOCK_DOCUMENTS` purpose `OLD_GOLD_PURCHASE`, một `STOCK_DOCUMENT_LINES`/movement/balance change cho mỗi `SALES_OLD_GOLD_LINES`. Lưu `PAID_AMOUNT_VND = oldGoldTotal`, `DEBT_AMOUNT_VND = 0`; tiền thực và offset được chứng minh bằng linkage, không tạo payable debt.

- [ ] **Step 4: Implement offset và payout**

Offset bằng `min(saleNet, oldGoldTotal)` và ghi một `SALES_CHECKOUT_OFFSETS`; không tạo cash voucher. Phần net âm tạo `STOCK_DOCUMENT_PAYMENTS` + voucher OUT cho từng allocation cash/bank; tổng phải bằng tuyệt đối net âm, CARD bị từ chối. Net dương tiếp tục dùng receipt/debt Task 4.

- [ ] **Step 5: Cập nhật projection khách hàng và công nợ**

Các query tổng đã thanh toán/còn nợ cộng tiền thực từ `ORDER_PAYMENTS` và offset từ `SALES_CHECKOUT_OFFSETS` đúng một lần; không suy diễn offset thành cash voucher. Ledger hiển thị một dòng “Bù trừ vàng cũ” tham chiếu mã checkout.

- [ ] **Step 6: Chạy old-gold và regression tests**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.cs --filter "FullyQualifiedName~SalesCheckout|FullyQualifiedName~MaterialReceiptIntegrationTests|FullyQualifiedName~DebtSettlementIntegrationTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Kazuko.Infrastructure/Sales src/Kazuko.Infrastructure/Customers/FirebirdCustomerRepository.cs src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs tests/Kazuko.Infrastructure.Tests/Sales tests/Kazuko.Infrastructure.Tests/Customers/CustomerRepositoryTests.cs tests/Kazuko.Infrastructure.Tests/Finance/DebtSettlementIntegrationTests.cs
git commit -m "feat: post old gold and trade in checkout"
```

### Task 6: Sales ViewModels và test project WPF

**Files:**
- Create: `tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj`
- Create: `tests/Kazuko.Modules.Sales.Tests/SalesViewModelTests.cs`
- Create: `tests/Kazuko.Modules.Sales.Tests/WorkspaceViewModelTests.cs`
- Modify: `KAZUKO_ERP_JEWELRY.sln`
- Modify: `src/Modules/Kazuko.Modules.Sales/SalesViewModel.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/BuySellWorkspaceViewModel.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/BuyOldGoldWorkspaceViewModel.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/SalesRowViewModels.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/Services/SalesDialogServices.cs`

**Interfaces:**
- Consumes: Core query/application/auth contracts Tasks 1, 3–5; `ICurrentSessionAccessor`.
- Produces: `SalesViewModel(ISalesQueryRepository queries, ISalesCheckoutApplicationService checkout, ISalesAuthorizationService authorization, ICashFundRepository cashFunds, IBankAccountRepository bankAccounts, ICurrentSessionAccessor sessionAccessor, ISalesPaymentDialogService paymentDialog, ISalesReceiptPresenter receiptPresenter)`; `ISalesPaymentDialogService.Show(SalesPaymentDialogRequest request) -> SalesPaymentDialogResult?`; `ISalesReceiptPresenter.ShowAsync(long checkoutId, CancellationToken cancellationToken = default) -> Task`.

- [ ] **Step 1: Tạo WPF test project và tests đỏ**

Target `net8.0-windows`, `UseWPF=true`; reference Core và Sales module. Tests: initial async load replaces all mock options, product scan adds one line/rejects duplicate, old-gold inputs recalculate read-only fields, remove/clear updates totals, failed post preserves state/error, successful post resets and calls receipt presenter, command disabled while posting, Previous/Next loads read-only checkout.

- [ ] **Step 2: Chạy ViewModel tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj`

Expected: FAIL vì constructor/contracts/module files chưa được refactor.

- [ ] **Step 3: Split ViewModels và xóa dữ liệu mock**

`SalesViewModel` nhận `ISalesQueryRepository`, `ISalesCheckoutApplicationService`, `ISalesAuthorizationService`, `ICurrentSessionAccessor`, payment dialog và receipt presenter. Hai workspace nhận lookup/calculator context từ coordinator; row models giữ IDs/snapshot chứ không chỉ chuỗi hiển thị. Dùng `AsyncRelayCommand` cho load/post.

- [ ] **Step 4: Implement command và state transitions**

`PayCommand` build draft với `Guid.NewGuid()` đúng một lần mỗi attempt; lỗi giữ key/draft để retry an toàn; thay đổi input sau lỗi tạo key mới. Thành công reset và preview; history set `IsReadOnly=true`, New trở lại edit mode.

- [ ] **Step 5: Chạy ViewModel tests để xác nhận xanh**

Run: lệnh Step 2.

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add KAZUKO_ERP_JEWELRY.sln src/Modules/Kazuko.Modules.Sales tests/Kazuko.Modules.Sales.Tests
git commit -m "feat: connect sales view models"
```

### Task 7: Payment dialog và bind mock UI

**Files:**
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesPaymentDialog.xaml`
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesPaymentDialog.xaml.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesPaymentDialogViewModel.cs`
- Modify: `src/Modules/Kazuko.Modules.Sales/Services/SalesDialogServices.cs`
- Modify: `src/Modules/Kazuko.Modules.Sales/SalesView.xaml`
- Modify: `src/Modules/Kazuko.Modules.Sales/BuySellTabView.xaml`
- Modify: `src/Modules/Kazuko.Modules.Sales/BuyOldGoldTabView.xaml`
- Modify: `src/Modules/Kazuko.Modules.Sales/Themes/SalesResources.xaml`
- Test: `tests/Kazuko.Modules.Sales.Tests/SalesPaymentDialogViewModelTests.cs`

**Interfaces:**
- Consumes: ViewModels Task 6 và các option đã tải qua `ICashFundRepository`/`IBankAccountRepository` hiện có.
- Produces: valid `SalesPaymentDialogResult` with allocations and receivable amount.

- [ ] **Step 1: Viết dialog ViewModel tests đỏ**

Tests: add/remove allocation, cash selects fund only, bank/card selects account only, receipt total plus debt equals positive net, payout requires exact full allocation, CARD hidden/rejected for payout, confirm disabled on mismatch, named-customer/permission gates debt.

- [ ] **Step 2: Chạy tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj --filter FullyQualifiedName~SalesPaymentDialogViewModelTests`

Expected: FAIL vì dialog ViewModel chưa tồn tại.

- [ ] **Step 3: Implement dialog và service**

Dialog owner-centered, ScrollViewer, Enter xác nhận/Escape hủy, semantic DynamicResources, inline errors. Không đóng dialog khi validation sai. Service chỉ chuyển result về coordinator; không gọi checkout trực tiếp.

- [ ] **Step 4: Rebind ba màn hình mock**

Thay string options/mock rows bằng typed collections/commands mới, đánh dấu derived old-gold fields read-only, bind loading/error/read-only state, giữ F9 thanh toán. Ẩn tab hóa đơn điện tử và loại bỏ binding/property không còn dùng.

- [ ] **Step 5: Chạy WPF tests và build Sales module**

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj`

Run: `dotnet build src/Modules/Kazuko.Modules.Sales/Kazuko.Modules.Sales.csproj`

Expected: PASS, không warning/error.

- [ ] **Step 6: Commit**

```powershell
git add src/Modules/Kazuko.Modules.Sales tests/Kazuko.Modules.Sales.Tests
git commit -m "feat: add sales payment workflow"
```

### Task 8: History và A4 receipt preview/print

**Files:**
- Create: `src/Kazuko.Infrastructure/Sales/FirebirdSalesPrintDataService.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/Services/SalesReceiptPrintService.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesReceiptPreviewDialog.xaml`
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesReceiptPreviewDialog.xaml.cs`
- Create: `src/Modules/Kazuko.Modules.Sales/Dialogs/SalesReceiptPreviewDialogViewModel.cs`
- Modify: `src/Modules/Kazuko.Modules.Sales/Services/SalesDialogServices.cs`
- Test: `tests/Kazuko.Infrastructure.Tests/Sales/SalesPrintDataServiceTests.cs`
- Test: `tests/Kazuko.Modules.Sales.Tests/SalesReceiptPrintServiceTests.cs`

**Interfaces:**
- Consumes: immutable checkout/schema Tasks 2, 4, 5; `ISalesPrintDataService` Task 1.
- Produces: branch-scoped receipt DTO and `SalesReceiptPrintService.CreateDocument(SalesReceiptDto) -> FlowDocument` configured A4.

- [ ] **Step 1: Viết print data tests đỏ**

Assert DTO chứa snapshot dòng bán/vàng cũ, gross/offset/net, từng dòng tiền, debt, cashier/salesperson/branch; query từ branch khác trả null/lỗi truy cập; giá hiện tại thay đổi không làm đổi DTO.

- [ ] **Step 2: Viết paginator tests đỏ**

Với receipt 80 dòng, assert `PageSize` A4, `PageCount > 1`, mỗi page có header bảng và block tổng/chữ ký không bị tách mất. Với receipt ngắn, assert một page.

- [ ] **Step 3: Chạy print tests để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj --filter FullyQualifiedName~SalesPrintDataServiceTests`

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj --filter FullyQualifiedName~SalesReceiptPrintServiceTests`

Expected: FAIL vì print services chưa tồn tại.

- [ ] **Step 4: Implement print data service**

Dùng các query tập trung theo header, dòng bán, dòng vàng cũ và payment trên cùng connection để đọc snapshot theo branch; không gọi bảng giá hiện hành.

- [ ] **Step 5: Implement A4 FlowDocument và preview**

Đặt `PageWidth=793.7`, `PageHeight=1122.5` device-independent pixels (A4 ở 96 DPI), page padding hợp lý, repeating `TableRowGroup` header, `KeepTogether` cho totals/signatures. Preview có In/Đóng; `PrintDialog` chỉ chạy sau lựa chọn người dùng. Lỗi in hiển thị nhưng không thay đổi checkout.

- [ ] **Step 6: Chạy print tests để xác nhận xanh**

Run: hai lệnh Step 3.

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Kazuko.Infrastructure/Sales src/Modules/Kazuko.Modules.Sales tests/Kazuko.Infrastructure.Tests/Sales tests/Kazuko.Modules.Sales.Tests
git commit -m "feat: add sales history and A4 receipts"
```

### Task 9: DI, navigation permission, full verification và docs

**Files:**
- Modify: `src/Kazuko.App/App.xaml.cs`
- Modify: `src/Modules/Kazuko.Modules.Sales/SalesModule.cs`
- Test: all projects/solution

**Interfaces:**
- Consumes: all previous tasks.
- Produces: runnable module wired through constructor injection and permission-aware navigation.

- [ ] **Step 1: Viết composition smoke test đỏ**

Add a focused test in `tests/Kazuko.Modules.Sales.Tests/SalesModuleCompositionTests.cs` that registers fakes, calls `SalesModule.RegisterNavigation`, creates the View and asserts a fully constructed `SalesViewModel`; navigation requires `sales.checkout.post`.

- [ ] **Step 2: Chạy smoke test để xác nhận đỏ**

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj --filter FullyQualifiedName~SalesModuleCompositionTests`

Expected: FAIL vì DI wiring cũ vẫn `new SalesViewModel()` không dependency.

- [ ] **Step 3: Wire services và module**

Đăng ký query, authorization, checkout và print-data singleton trong `App.RegisterServices`. `SalesModule` resolve service/session/dialog presenters và tạo ViewModel bằng constructor injection; đặt `RequiredPermission` nhất quán.

- [ ] **Step 4: Chạy test theo lớp**

Run: `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj`

Run: `dotnet test tests/Kazuko.Modules.Sales.Tests/Kazuko.Modules.Sales.Tests.csproj`

Expected: PASS toàn bộ.

- [ ] **Step 5: Build và test solution sạch**

Run: `dotnet build KAZUKO_ERP_JEWELRY.sln`

Run: `dotnet test KAZUKO_ERP_JEWELRY.sln --no-build`

Expected: không warning/error; tất cả test pass hoặc Firebird integration test chỉ skip theo đúng convention hiện có khi test DB không sẵn sàng.

- [ ] **Step 6: Kiểm tra UI thủ công theo ma trận**

Chạy Light/Dark; font 90/100/125/150%; DPI 100/150/200%; minimum/maximized; keyboard/F9; empty/loading/error; sale/trade-in/old-gold; mixed payment/debt; history; preview A4 ít dòng và nhiều trang. Ghi kết quả vào commit/PR notes, không tạo file ảnh tạm trong repo.

- [ ] **Step 7: Rà soát migration trên bản copy cuối cùng**

Chạy migration runner + verification Task 2 từ một bản restore sạch, sau đó chạy integration tests trỏ vào bản copy đã migrate. Không apply DB thật trong task implementation nếu người dùng chưa yêu cầu rõ.

- [ ] **Step 8: Rà soát diff và commit wiring**

```powershell
git diff --check
git status --short
git add src/Kazuko.App/App.xaml.cs src/Modules/Kazuko.Modules.Sales/SalesModule.cs tests/Kazuko.Modules.Sales.Tests/SalesModuleCompositionTests.cs
git commit -m "feat: complete sales checkout workflow"
```

Chỉ stage những file thực sự thay đổi trong Task 9; không stage các thay đổi unrelated đang có sẵn trong working tree.

---

## Final Acceptance

- Ba luồng `SALE`, `TRADE_IN`, `OLD_GOLD_PURCHASE` chạy từ UI tới Firebird và trả receipt DTO.
- Mỗi checkout có một transaction, idempotency hash và audit đầy đủ.
- Kho thành phẩm, kho vàng cũ, tiền, offset và công nợ nhất quán sau mọi test lỗi/concurrency.
- Không còn dữ liệu mock hoặc command rỗng trong module Sales.
- Tab hóa đơn điện tử bị ẩn; không có code trả hàng/giữ đơn/đặt chế tác ngoài phạm vi.
- A4 preview/in dùng snapshot đã ghi, không chụp bitmap UI.
- Migration 51 và verification pass trên bản copy; build/test solution xanh.
