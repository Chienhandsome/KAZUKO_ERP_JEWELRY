# Quản lý Database Migration & Quy chuẩn Schema Firebird

## 1. Nguyên tắc cốt lõi

- **Tiến về phía trước (Forward-only):** Mọi thay đổi schema trên Firebird phải đi qua tệp migration SQL được đánh số thứ tự tăng dần.
- **Sổ cái `SCHEMA_MIGRATIONS`:** Bảng theo dõi lịch sử migration gồm:
  - `VERSION_NO` (`INTEGER`, Khóa chính).
  - `MIGRATION_NAME` (`VARCHAR(120)`).
  - `CHECKSUM_VALUE` (`VARCHAR(64)` - mã băm SHA-256 dạng hex chữ hoa).
  - `APPLIED_AT` (`TIMESTAMP DEFAULT CURRENT_TIMESTAMP`).
- **Khớp mã băm toàn vẹn (Full Artifact SHA-256):** Checksum lưu trong `SCHEMA_MIGRATIONS.CHECKSUM_VALUE` phải là mã băm SHA-256 của toàn bộ tệp migration artifact (`<VERSION>__<name>.sql`), không lưu mã băm từng đoạn (payload hash).
- **Xác định phiên bản kế tiếp:** Luôn truy vấn `SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS` trên cơ sở dữ liệu thật trước khi đặt tên file, tuyệt đối không suy đoán số thứ tự.
- **Quy chuẩn đặt tên tệp:** `database/migrations/<VERSION_4_DIGITS>__<snake_case_name>.sql`, ví dụ: `0030__supplier_core.sql`.
- **An toàn giao dịch (Atomic Execution):**
  - Migration artifact được thực thi qua công cụ áp dụng tự động (`database/tools/Invoke-KazukoMigration.ps1`).
  - Toàn bộ thay đổi DDL chạy dưới chế độ `SET AUTODDL OFF;`.
  - Không nhúng lệnh chèn sổ cái (`INSERT INTO SCHEMA_MIGRATIONS`) vào chính tệp artifact; việc ghi nhận sổ cái được quản lý bởi công cụ áp dụng và chỉ thực thi khi toàn bộ DDL/Seed thành công.
  - Khi gặp bất kỳ lỗi nào, `isql -b` dừng ngay lập tức, toàn bộ giao dịch được ROLLBACK và không để lại bảng, cột, index, seed hay ledger dở dang.
- **Bảo mật:** Không bao giờ ghi thông tin xác thực/mật khẩu vào tệp SQL, script, log hay git. Toàn bộ credential được lấy từ biến môi trường (`$env:ISC_USER`, `$env:KAZUKO_FIREBIRD_PASSWORD` / `$env:ISC_PASSWORD`).
- **Sao lưu bắt buộc:** Luôn tạo backup `.fbk` bằng `gbak.exe` và khôi phục thử nghiệm vào file tạm trước khi áp dụng migration lên cơ sở dữ liệu production/development.

---

## 2. Quy trình áp dụng Migration an toàn

### Bước 1: Sao lưu và Khôi phục kiểm tra
```powershell
$env:ISC_USER = "SYSDBA"
$env:ISC_PASSWORD = $env:KAZUKO_FIREBIRD_PASSWORD
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

# Tạo bản sao lưu
& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\gbak.exe" -b -v "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb" "data\backups\data_backup_$timestamp.fbk"

# Khôi phục thử nghiệm
& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\gbak.exe" -c -v "data\backups\data_backup_$timestamp.fbk" "data\backups\test_verify.fdb"

# Xóa file tạm sau khi xác nhận restore thành công
Remove-Item -Force "data\backups\test_verify.fdb"
```

### Bước 2: Áp dụng Migration bằng công cụ chuẩn hóa (`Invoke-KazukoMigration.ps1`)
Công cụ `database/tools/Invoke-KazukoMigration.ps1` tự động:
1. Tính mã băm SHA-256 thật của tệp artifact (artifact nguyên bản không chứa COMMIT trung gian).
2. Trích xuất và kiểm tra chặt chẽ số version và tên migration (`^[A-Za-z0-9_]+$`).
3. Kiểm tra version trong `SCHEMA_MIGRATIONS` kèm kiểm tra mã thoát lệnh (exit code).
4. Tạo snapshot sao lưu tự động (`gbak -b`) trước khi chạy để đảm bảo hoàn nguyên 100% nếu có bất kỳ lỗi nào.
5. Tạo tệp wrapper thực thi an toàn dưới `SET AUTODDL OFF;`, phân tách ranh giới DDL và DML/Seed ở cấp runner để tương thích với kiến trúc Firebird engine, kèm quote đường dẫn tránh lỗi khoảng trắng.
6. Khi gặp bất kỳ lỗi nào (lỗi DDL, lỗi Seed, lỗi kiểm tra Schema, lỗi Ledger), tự động hoàn nguyên cơ sở dữ liệu về nguyên trạng ban đầu từ snapshot (`gbak -c -rep`), bảo đảm atomicity tuyệt đối (zero schema leak).
7. Ghi nhận sổ cái `SCHEMA_MIGRATIONS` với đầy đủ full artifact SHA-256 và dọn dẹp file tạm.

```powershell
powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0030__supplier_core.sql"
```

### Bước 3: Xác minh Schema tự động (Fail-fast Verification)
Tệp `database/verification/verify_supplier_schema.sql` tự động ném ngoại lệ `EX_VERIFY_FAILED` và dừng ngay lập tức nếu thiếu bảng, sai cột, sai nullability, thiếu khóa chính/khóa ngoại/unique/chỉ mục, sai số lượng seed hoặc sai checksum ledger.

```powershell
& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe" -b -ch UTF8 -i "database\verification\verify_supplier_schema.sql" localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb
```

---

## 3. Chi tiết Migration Version 30: `0030__supplier_core.sql`

- **Tệp nguồn:** `database/migrations/0030__supplier_core.sql`
- **Phiên bản:** `30`
- **Tên migration:** `SUPPLIER_CORE`
- **Số lệnh COMMIT trong artifact:** `0` (Artifact sạch, hoàn toàn không chứa COMMIT)
- **Mã băm Artifact SHA-256 (Tệp & Sổ cái giống nhau hoàn toàn):**
  `64FA278236812ABCAFCCD305E2B8CC4B6DB1FAA255345C3A501DC86A2C283373`

### 3.1. Bảng `SUPPLIER_GROUPS`
Lưu trữ danh mục nhóm nhà cung cấp phục vụ phân loại và quản lý quan hệ đối tác:
- **Cột:** `ID` (PK, Identity), `COMPANY_ID` (FK `COMPANIES`), `CODE` (VARCHAR(30) ASCII), `NAME` (VARCHAR(150)), `DESCRIPTION` (VARCHAR(500)), `IS_ACTIVE` (BOOLEAN), `IS_DEFAULT` (BOOLEAN), `DISPLAY_ORDER` (INTEGER), `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
- **Ràng buộc & Chỉ mục:**
  - Khóa chính: `PK_SUPPLIER_GROUPS` (`ID`).
  - Khóa duy nhất: `UQ_SUPPLIER_GROUPS_CODE` (`COMPANY_ID, CODE`).
  - Khóa duy nhất hỗ trợ FK composite: `UQ_SUPPLIER_GROUPS_COMP_ID` (`COMPANY_ID, ID`).
  - Khóa ngoại công ty: `FK_SUPPLIER_GROUPS_COMPANY`.
  - Khóa ngoại kiểm toán: `FK_SUPPLIER_GROUPS_CREATE_USER`, `FK_SUPPLIER_GROUPS_UPDATE_USER`.
  - Chỉ mục tìm kiếm / sắp xếp: `IX_SUPPLIER_GROUPS_ACTIVE` (`COMPANY_ID, IS_ACTIVE, DISPLAY_ORDER`).
  - **Ràng buộc duy nhất có điều kiện (Partial Unique Index):**
    `UQ_SUPPLIER_GROUPS_ONE_DEFAULT` trên `(COMPANY_ID) WHERE IS_DEFAULT = TRUE AND IS_ACTIVE = TRUE`.
    *Cơ chế này được Firebird 5.0 hỗ trợ nguyên bản, bảo đảm ở tầng cơ sở dữ liệu mỗi công ty chỉ có tối đa một nhóm NCC mặc định hoạt động.*

### 3.2. Bảng `SUPPLIERS`
Lưu trữ thông tin chi tiết nhà cung cấp theo chuẩn nghiệp vụ WinForms đã xác thực:
- **Cột:** `ID` (PK, Identity), `COMPANY_ID` (FK `COMPANIES`), `CREATED_BRANCH_ID` (FK `BRANCHES`), `SUPPLIER_GROUP_ID` (FK `SUPPLIER_GROUPS`), `CODE` (VARCHAR(30) ASCII), `NAME` (VARCHAR(200)), `PHONE`, `EMAIL`, `ADDRESS`, `REGION`, `TAX_CODE`, `CONTACT_NAME`, `CONTACT_TITLE`, `NOTES`, `IS_ACTIVE`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
- **Không lưu các cột suy đoán:** Tuyệt đối không thêm `CREDIT_LIMIT`, `BANK_ACCOUNT`, `IMAGE_PATH`, `TOTAL_PURCHASES`, hoặc `CURRENT_DEBT`.
- **Ràng buộc & Chỉ mục:**
  - Khóa chính: `PK_SUPPLIERS` (`ID`).
  - Khóa duy nhất: `UQ_SUPPLIERS_CODE` (`COMPANY_ID, CODE`).
  - Khóa duy nhất hỗ trợ FK composite: `UQ_SUPPLIERS_COMP_ID` (`COMPANY_ID, ID`).
  - Khóa ngoại composite bảo đảm đồng bộ công ty: `FK_SUPPLIERS_GROUP` (`COMPANY_ID, SUPPLIER_GROUP_ID`) tham chiếu `SUPPLIER_GROUPS(COMPANY_ID, ID)`.
  - Khóa ngoại chi nhánh và kiểm toán: `FK_SUPPLIERS_BRANCH`, `FK_SUPPLIERS_CREATE_USER`, `FK_SUPPLIERS_UPDATE_USER`.
  - Chỉ mục tra cứu: `IX_SUPPLIERS_COMPANY_ACTIVE`, `IX_SUPPLIERS_GROUP`, `IX_SUPPLIERS_PHONE`, `IX_SUPPLIERS_TAX_CODE`.

### 3.3. Bảng `STOCK_DOCUMENTS` (Cập nhật)
- **Thêm cột:** `SUPPLIER_ID BIGINT NULL`.
- **Khóa ngoại:** `FK_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`) tham chiếu `SUPPLIERS(COMPANY_ID, ID)`.
  *Khóa ngoại composite cho phép giá trị `SUPPLIER_ID` là `NULL` cho các chứng từ kho nội bộ (điều chuyển, xuất sản xuất), nhưng khi có liên kết NCC thì bắt buộc NCC phải thuộc cùng `COMPANY_ID`.*
- **Chỉ mục:** `IX_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`).
- **Bảo toàn dữ liệu:** Cột `COUNTERPARTY_NAME` giữ nguyên làm text snapshot; dữ liệu lịch sử giữ nguyên và có `SUPPLIER_ID = NULL`.

### 3.4. Dữ liệu mồi (Seed Data)
Được seed tự động theo `COMPANY CODE = 'KAZUKO'` (không hard-code ID):
1. `VANG` - "Vàng" (`IS_DEFAULT = TRUE`, `DISPLAY_ORDER = 1`)
2. `DA_QUY` - "Đá quý" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 2`)
3. `KIM_LOAI` - "Kim loại" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 3`)
4. `KHAC` - "Khác" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 4`)

---

## 4. Hướng dẫn Rollback (Khi cần thiết)

> **LƯU Ý:** Không thực hiện rollback khi migration đã áp dụng thành công và không có yêu cầu hủy.

### Trường hợp 1: Database phát triển / Chưa phát sinh dữ liệu nghiệp vụ
Nếu cần thu hồi migration 30 khi chưa có dữ liệu phát sinh ngoài seed data, thực thi tuần tự các lệnh DDL sau bằng `isql.exe`:

```sql
-- 1. Xóa khóa ngoại và chỉ mục trên STOCK_DOCUMENTS
ALTER TABLE STOCK_DOCUMENTS DROP CONSTRAINT FK_STOCK_DOC_SUPPLIER;
DROP INDEX IX_STOCK_DOC_SUPPLIER;
ALTER TABLE STOCK_DOCUMENTS DROP SUPPLIER_ID;
COMMIT;

-- 2. Xóa bảng SUPPLIERS
DROP TABLE SUPPLIERS;
COMMIT;

-- 3. Xóa bảng SUPPLIER_GROUPS
DROP TABLE SUPPLIER_GROUPS;
COMMIT;

-- 4. Xóa bản ghi migration khỏi sổ cái
DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 30;
COMMIT;
```

### Trường hợp 2: Database đã có dữ liệu sản xuất (Production)
- Khi cơ sở dữ liệu đã phát sinh giao dịch nhập kho hoặc thông tin NCC thực tế, **tuyệt đối không chạy lệnh DROP thô bạo**.
- Quy trình chuẩn là:
  1. Đưa hệ thống về chế độ bảo trì.
  2. Khôi phục lại bản backup toàn vẹn tạo trước thời điểm xảy ra sự cố (`gbak -c`).
  3. Hoặc phát hành một bản migration sửa lỗi tiến về phía trước (Forward-fix migration) để xử lý tương thích mà không làm mất dữ liệu.

---

## 5. Chi tiết Migration Version 31: `0031__human_resources_core.sql`

- **Tệp nguồn:** `database/migrations/0031__human_resources_core.sql`
- **Phiên bản:** `31`
- **Tên migration:** `HUMAN_RESOURCES_CORE`
- **Mô tả:** Tạo `HR_DEPARTMENTS`, `HR_POSITIONS`, `HR_EMPLOYEES`; seed danh mục phòng ban/chức vụ cho `KAZUKO`; **không** seed nhân viên. Nhân viên **không** gắn `DEPARTMENT_ID`/`POSITION_ID` (danh mục giữ cho tab cơ cấu sau).
- **Verification:** `database/verification/verify_human_resources_schema.sql`
- **Áp dụng:**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0031__human_resources_core.sql"
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0032__hr_employee_drop_dept_pos.sql"
  ```
  Migration 32 gỡ cột phòng ban/chức vụ khỏi `HR_EMPLOYEES` nếu bản 31 cũ đã tạo các cột đó (idempotent nếu đã không còn cột).
- **Rollback (dev, chưa có dữ liệu NV):** `DROP TABLE HR_EMPLOYEES;` → `DROP TABLE HR_POSITIONS;` → `DROP TABLE HR_DEPARTMENTS;` rồi khôi phục `.fbk` / xóa ledger 31–32 theo quy trình mục 4. Production ưu tiên restore backup.

---

## 6. Migration Version 33: `0033__inventory_unit_standardization.sql`

- **Phiên bản:** `33` — chuẩn hóa đơn vị tồn NVL về `CHI` / `VIEN` / `CARAT` / `CAI`.
- **Bảng audit:** `UNIT_CONVERSION_RUNS`, `UNIT_CONVERSION_AUDIT`, `UNIT_CONVERSION_EXCEPTIONS`.
- **Verification:** `database/verification/verify_inventory_unit_standardization.sql`.
- **Quy tắc:** vàng `g`→`CHI` (qty÷3.75, cost×3.75); `Chi`→`CHI`; `LUONG`→qty×10; đá/phụ kiện chuẩn hóa mã; đơn vị lạ → exceptions (không đoán).
- **Rollback:** restore `.fbk` trước khi chạy; không DROP audit nếu đã có ngoại lệ cần điều tra.
- **Invariant nghiệp vụ:** xem `docs/06-WAREHOUSE-STOCK-INVARIANTS.md`.

## 7. Migration Version 34: `0034__warehouse_finance_craft_flow.sql`

- **Phiên bản:** `34` — mở rộng `STOCK_DOCUMENTS` / lines; thêm thợ ngoài, holdings, settlement; quỹ / TKNH / phiếu chi; payment allocation; công nợ phải trả; đánh dấu xuất TP trên `SALES_ORDERS`.
- **Verification:** `database/verification/verify_warehouse_finance_craft_schema.sql`.
- **Áp dụng (sau backup):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0033__inventory_unit_standardization.sql"
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0034__warehouse_finance_craft_flow.sql"
  ```
- **Rollback production:** restore snapshot `gbak` tạo trước migration 33/34.

---

## 8. Migration Version 35: `0035__financial_settlement_and_voucher_idempotency.sql`

- **Phiên bản:** `35` — chuẩn hóa phiếu thu/chi dùng chung + settlement đa phương thức.
- **Thay đổi:**
  - `CASH_VOUCHERS.SOURCE_ALLOCATION_TYPE` / `SOURCE_ALLOCATION_ID` + partial unique index `UQ_CV_SOURCE_ALLOC` (chống sinh trùng phiếu theo allocation).
  - Bảng `FINANCIAL_SETTLEMENTS`, `FINANCIAL_SETTLEMENT_ALLOCATIONS`, `FINANCIAL_SETTLEMENT_APPLICATIONS`.
  - Sequence `CASH_VOUCHER_IN`, `FINANCIAL_SETTLEMENT`.
- **Verification:** `database/verification/verify_financial_settlement_schema.sql`.
- **Áp dụng (sau backup):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0035__financial_settlement_and_voucher_idempotency.sql"
  ```
- **Rollback (dev, chưa có dữ liệu settlement):**
  ```sql
  DROP TABLE FINANCIAL_SETTLEMENT_APPLICATIONS;
  DROP TABLE FINANCIAL_SETTLEMENT_ALLOCATIONS;
  DROP TABLE FINANCIAL_SETTLEMENTS;
  DROP INDEX UQ_CV_SOURCE_ALLOC;
  DROP INDEX IX_CV_SOURCE_ALLOC;
  ALTER TABLE CASH_VOUCHERS DROP SOURCE_ALLOCATION_ID;
  ALTER TABLE CASH_VOUCHERS DROP SOURCE_ALLOCATION_TYPE;
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 35;
  ```
  Production: restore snapshot `gbak` trước migration 35.

---

## 9. Migration Version 36: `0036__finished_product_movements.sql`

- **Phiên bản:** `36` — ledger biến động thành phẩm + dòng output phiếu nhập chế tác.
- **Bảng:** `FINISHED_PRODUCT_MOVEMENTS`, `STOCK_CRAFT_OUTPUT_LINES`.
- **Verification:** `database/verification/verify_finished_product_movements_schema.sql`.
- **Áp dụng:**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0036__finished_product_movements.sql"
  ```
- **Rollback (dev):** `DROP TABLE STOCK_CRAFT_OUTPUT_LINES;` → `DROP TABLE FINISHED_PRODUCT_MOVEMENTS;` → xóa ledger 36. Production: restore `.fbk`.

---

## 10. Migration Version 37: `0037__receivable_debts_loss_compensation.sql`

- **Phiên bản:** `37` — phải thu thợ bồi thường hao hụt.
- **Thay đổi:** cột trách nhiệm/bồi thường trên `STOCK_CRAFT_SETTLEMENT_LINES`; bảng `RECEIVABLE_DEBTS`, `RECEIVABLE_DEBT_COLLECTIONS`.
- **Verification:** `database/verification/verify_receivable_debts_schema.sql`.
- **Áp dụng:**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0037__receivable_debts_loss_compensation.sql"
  ```
- **Rollback (dev):** gỡ FK/cột bồi thường → `DROP TABLE RECEIVABLE_DEBT_COLLECTIONS;` → `DROP TABLE RECEIVABLE_DEBTS;` → xóa ledger 37. Production: restore `.fbk`.

---

## 11. Migration Version 38: `0038__document_reversal_and_party_credits.sql`

- **Phiên bản:** `38` — sổ hủy/đảo + credit NCC/khách (không phải thu/chi).
- **Bảng:** `DOCUMENT_REVERSALS` (unique theo nguồn → không đảo hai lần), `PARTY_CREDITS`.
- **Cột hủy:** `CASH_VOUCHERS` / `FINANCIAL_SETTLEMENTS` thêm `CANCELLED_*` / `CANCELLATION_REASON`.
- **Verification:** `database/verification/verify_document_reversal_schema.sql`.
- **Áp dụng:**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0038__document_reversal_and_party_credits.sql"
  ```
- **Quy tắc:** credit giữ tiền cấn lần sau **không** tạo `CASH_VOUCHERS`. Hủy chứng từ đảo movement + voucher + debt/settlement trong cùng transaction; chặn khi còn chứng từ phụ thuộc chưa đảo.

---

## 12. Migration Version 39: `0039__customer_photo_path.sql`

- **Phiên bản:** `39` — thêm `CUSTOMERS.PHOTO_PATH` (VARCHAR 500) để lưu đường dẫn ảnh hồ sơ khách hàng (copy LocalAppData giống nhân viên).
- **Verification:** `database/verification/verify_customer_photo_schema.sql`.
- **Áp dụng:**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0039__customer_photo_path.sql"
  ```
- **Rollback (dev):** `ALTER TABLE CUSTOMERS DROP PHOTO_PATH;` → xóa ledger 39. Production: restore `.fbk`.

---

## 13. Migration Version 45: `0045__production_order_core.sql`

- **Phiên bản:** `45` — lõi lệnh sản xuất (header, dòng mẫu, nhân viên gắn lệnh, đính kèm ảnh).
- **Bảng:** `PRODUCTION_ORDERS`, `PRODUCTION_ORDER_LINES`, `PRODUCTION_ORDER_WORKERS`, `PRODUCTION_ATTACHMENTS`.
- **Legacy WinForms:** bảng `PRODUCTION_ORDERS` cũ (PRODUCT_NAME/QUANTITY, status PLANNED/WAITING_QC…) được **nâng cấp tại chỗ**: thêm `ORDER_SOURCE`, `ORDER_DATE`, `EXECUTION_TYPE`, kho, `ROW_VERSION`, `COMPLETED_*`; nới CHECK status để có `DRAFT`; giữ cột cũ để đọc lịch sử.
- **Ràng buộc module mới:** CHECK nguồn/hình thức/status; unique filtered `SALES_ORDER_ID`; index danh sách.
- **Sequence:** seed `DOCUMENT_SEQUENCES` với `DOCUMENT_TYPE = 'PRODUCTION_ORDER'` (prefix hiển thị `LSX`).
- **Verification:** `database/verification/verify_production_order_core.sql`.
- **Checksum:** computed by `Invoke-KazukoMigration.ps1` on apply.
- **Áp dụng (sau backup, đóng app):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0045__production_order_core.sql"
  ```
- **Rollback (dev, chưa có lệnh SX thật):** DROP theo thứ tự attachments → workers → lines → orders; xóa ledger 45. Production: restore `.fbk`.

---

## 14. Migration Version 46: `0046__production_execution_qc.sql`

- **Phiên bản:** `46` — giao/nhận NVL, QC và cấu thành cho lệnh sản xuất.
- **Mở rộng:** `STOCK_DOCUMENTS` types `PRODUCTION_MATERIAL_ISSUE` / `PRODUCTION_RETURN_RECEIPT` / `PRODUCTION_FINISHED_RECEIPT`; purposes `PRODUCTION_MATERIAL_DELIVERY` / `PRODUCTION_RETURN` / `PRODUCTION_FINISHED_RECEIPT`; FK `PRODUCTION_ORDER_ID`; unique filtered index mỗi loại chứng từ active/lệnh.
- **Bảng:** `PRODUCTION_MATERIAL_BALANCES`, `PRODUCTION_QC_UNITS`, `PRODUCTION_QC_ATTEMPTS`, `PRODUCTION_UNIT_COMPONENTS`, `PRODUCTION_QC_IMAGES`.
- **Sequence:** seed `PRODUCTION_MATERIAL_ISSUE` (GX), `PRODUCTION_RETURN_RECEIPT` (NT), `PRODUCTION_FINISHED_RECEIPT` (NTP).
- **Preflight:** orphan `PRODUCTION_ORDER_ID` trên stock/finished/craft output → dừng, không tự sửa.
- **Verification:** `database/verification/verify_production_execution_qc.sql`.
- **Áp dụng (sau backup, đóng app):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0046__production_execution_qc.sql"
  ```
- **Rollback production:** restore `.fbk` trước migration 46.

---

## 15. Migration Version 47: `0047__production_completion_guards.sql`

- **Phiên bản:** `47` — guard hoàn tất: FK thành phẩm → lệnh, liên kết QC unit, unique công nợ theo lệnh.
- **Thay đổi:** FK `FINISHED_PRODUCTS.PRODUCTION_ORDER_ID`; cột/unique `PRODUCTION_QC_UNIT_ID`; unique filtered `PAYABLE_DEBTS` với `ORIGIN_DOCUMENT_TYPE = 'PRODUCTION_ORDER'`; index danh sách lệnh.
- **Verification:** `database/verification/verify_production_completion_guards.sql`.
- **Áp dụng (sau backup, đóng app):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0047__production_completion_guards.sql"
  ```
- **Rollback production:** restore `.fbk` trước migration 47.

---

## 16. Migration Version 48: `0048__production_attachment_line_number.sql`

- **Phiên bản:** `48` — gắn đính kèm lệnh sản xuất với dòng mẫu.
- **Thay đổi:** thêm `PRODUCTION_ATTACHMENTS.LINE_NUMBER` (nullable, CHECK > 0 hoặc null); index `IX_PA_LINE`.
- **Verification:** `database/verification/verify_production_attachment_line_number.sql`.
- **Áp dụng (sau backup, đóng app):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0048__production_attachment_line_number.sql"
  ```
- **Rollback (dev):** `DROP INDEX IX_PA_LINE;` → `ALTER TABLE PRODUCTION_ATTACHMENTS DROP CONSTRAINT CK_PA_LINE_NO;` → `ALTER TABLE PRODUCTION_ATTACHMENTS DROP LINE_NUMBER;` → xóa ledger 48. Production: restore `.fbk`.

---

## 17. Migration Version 49: `0049__production_sales_order_id_nullable.sql`

- **Phiên bản:** `49` — nới `PRODUCTION_ORDERS.SALES_ORDER_ID` về nullable (lệnh `STOCK` không gắn đơn bán).
- **Lý do:** schema WinForms/legacy có thể để cột NOT NULL; lưu bản nháp nguồn kho bị lỗi `validation error ... SALES_ORDER_ID ... null`.
- **Giữ:** `CK_PO_SOURCE` (STOCK ⇒ null, SALES_ORDER ⇒ not null).
- **Verification:** `database/verification/verify_production_sales_order_id_nullable.sql`.
- **Áp dụng (sau backup, đóng app; cần 48 trước):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0049__production_sales_order_id_nullable.sql"
  ```
- **Rollback production:** restore `.fbk` trước migration 49.

---

## 18. Migration Version 50: `0050__production_legacy_columns_nullable.sql`

- **Phiên bản:** `50` — nới các cột header legacy WinForms trên `PRODUCTION_ORDERS` để module mới lưu lệnh `STOCK`/`DRAFT` không cần đơn bán / thiết kế / BOM cấp lệnh.
- **Cột (nếu còn NOT NULL):** `SALES_ORDER_ITEM_ID`, `CUSTOMER_ID`, `DESIGN_ID`, `BOM_ID`, `PRODUCT_NAME`, `QUANTITY`.
- **CHECK:** thay `CK_PRODUCTION_QTY` bằng `CK_PO_LEGACY_QTY` (`QUANTITY IS NULL OR QUANTITY > 0`).
- **Verification:** `database/verification/verify_production_legacy_columns_nullable.sql`.
- **Áp dụng (sau backup, đóng app; cần 49 trước):**
  ```powershell
  powershell -File "database\tools\Invoke-KazukoMigration.ps1" -MigrationPath "database\migrations\0050__production_legacy_columns_nullable.sql"
  ```
- **Rollback production:** restore `.fbk` trước migration 50.

---

## 19. Migration Version 51: `0051__sales_checkout_core.sql`

- **Phiên bản:** `51` — lõi checkout bất biến cho `SALE`, `TRADE_IN` và `OLD_GOLD_PURCHASE`.
- **Tệp nguồn:** `database/migrations/0051__sales_checkout_core.sql`.
- **Verification:** `database/verification/verify_sales_checkout_core.sql` (fail-fast bằng `EX_VERIFY_FAILED`).
- **Artifact SHA-256 đã xác minh trên bản copy:**
  `2390791BAEE7795E980AD35DBE34857F2C3C82A7BBD6E79D48E00C9BD5EA3E96`.
- **Tính nguyên tử:** artifact không tự kết thúc transaction; `Invoke-KazukoMigration.ps1` quản lý snapshot, ranh giới DDL/DML, ledger và khôi phục khi lỗi.

### 19.1. Schema checkout và snapshot

- `SALES_CHECKOUTS` lưu công ty/chi nhánh, mã/ngày/loại checkout, khách hàng, nhân viên bán hàng, kho, bốn tổng tiền (`SALE_GROSS_VND`, `OLD_GOLD_TOTAL_VND`, `DISCOUNT_AMOUNT_VND`, `NET_AMOUNT_VND`), các chứng từ con, audit và trạng thái `POSTED`.
- `NET_AMOUNT_VND` là số có dấu và được ràng buộc bằng `SALE_GROSS_VND - DISCOUNT_AMOUNT_VND - OLD_GOLD_TOTAL_VND`; chỉ ba thành phần đầu vào phải không âm.
- Idempotency dùng `CLIENT_REQUEST_ID CHAR(36)` và `REQUEST_HASH CHAR(64)`; unique `(BRANCH_ID, CLIENT_REQUEST_ID)` ngăn ghi hai checkout cho cùng yêu cầu trong một chi nhánh.
- `SALES_OLD_GOLD_LINES` lưu toàn bộ input/derived snapshot: số món, trọng lượng tổng/đá/tạp chất/vàng/quy đổi, phần trăm giá trị, giá mua nguồn/áp dụng, thành tiền, override/lý do, material và một `STOCK_DOCUMENT_LINE_ID` duy nhất để truy xuất từng lô.
- `SALES_CHECKOUT_OFFSETS` lưu đúng một khoản bù trừ dương cho mỗi checkout, liên kết trực tiếp `SALES_ORDERS` và phiếu nhập vàng cũ; khoản này không phải `CASH_VOUCHERS`.
- `SALES_ORDER_ITEMS` thêm `FINISHED_PRODUCT_ID`, source/applied snapshots cho giá vàng/tiền công/tiền đá, `IS_PRICE_OVERRIDE` và `PRICE_OVERRIDE_REASON`.
- `ORDER_PAYMENTS` thêm liên kết tới settlement, allocation và voucher; unique allocation ngăn một dòng tiền thực bị phản ánh hai lần.

### 19.2. Constraint, index, CARD và purpose

- Ba bảng mới có PK/FK/unique/check đầy đủ; index phục vụ lịch sử theo chi nhánh/ngày, khách hàng, nhân viên, chứng từ con, material và stock line.
- `CK_STOCK_PURPOSE` giữ các purpose hiện có và bổ sung `RETAIL_SALE`.
- `CARD` được bổ sung vào allow-list và dùng tài khoản ngân hàng trong `CASH_VOUCHERS`, `STOCK_DOCUMENT_PAYMENTS`, `FINANCIAL_SETTLEMENT_ALLOCATIONS` và `PAYABLE_DEBT_PAYMENT_ALLOCATIONS`.
- `ORDER_PAYMENTS.CK_ORDER_PAYMENT_METHOD` chấp nhận `BANK_TRANSFER` và tiếp tục giữ các giá trị legacy `BANK`/`OTHER`, đồng thời giữ `CARD`.
- Quy tắc nghiệp vụ vẫn từ chối `CARD` cho khoản chi ở Core/Application Service; constraint dùng chung chỉ bảo đảm CARD là phương thức gắn tài khoản ngân hàng.

### 19.3. Sequence và permission seed

- Seed idempotent `DOCUMENT_SEQUENCES` theo từng chi nhánh/năm cho:
  - `SALES_CHECKOUT` — tiền tố nghiệp vụ `MB` cho sale/trade-in.
  - `SALES_OLD_GOLD_PURCHASE` — tiền tố nghiệp vụ `MVC` cho mua vàng cũ độc lập.
- Seed sáu permission: `sales.checkout.post`, `sales.price.override`, `sales.debt.approve`, `sales.salesperson.change`, `sales.history.read`, `sales.receipt.print`.
- `ADMIN`/`MANAGER` nhận sáu quyền mặc định; `SALE` nhận post/history/print. Seed chỉ thêm mapping còn thiếu và không ghi đè một mapping đã tồn tại.

### 19.4. Chạy chỉ trên bản copy

Không dùng các lệnh dưới đây với `data\data.fdb`. Trước khi tạo migration 51 phải xác nhận bản copy đã đi qua version 50.

```powershell
$copyDatabase = "localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\backups\sales_checkout_verify.fdb"
$isql = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe"
$gbak = "D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64\gbak.exe"

"SET LIST ON; SELECT MAX(VERSION_NO) AS MAX_VERSION FROM SCHEMA_MIGRATIONS;" |
    & $isql -q -ch UTF8 $copyDatabase

powershell -File "database\tools\Invoke-KazukoMigration.ps1" `
    -MigrationPath "database\migrations\0051__sales_checkout_core.sql" `
    -Database $copyDatabase -IsqlPath $isql -GbakPath $gbak

& $isql -b -ch UTF8 `
    -i "database\verification\verify_sales_checkout_core.sql" `
    $copyDatabase
```

### 19.5. Backup và rollback

- Trước production phải đóng ứng dụng embedded, tạo `.fbk` bằng `gbak`, restore thử thành công, rồi mới chạy runner.
- Nếu runner báo lỗi, snapshot tự động được restore và version 51 không được ghi vào `SCHEMA_MIGRATIONS`.
- Production đã có checkout: restore snapshot trước migration 51 hoặc phát hành forward-fix; không DROP snapshot/chứng từ đã post.
- Development chưa có checkout: có thể gỡ index/FK/check và các cột thêm trên `ORDER_PAYMENTS`/`SALES_ORDER_ITEMS`, khôi phục các payment/purpose constraint version 50, rồi DROP theo thứ tự `SALES_CHECKOUT_OFFSETS` → `SALES_OLD_GOLD_LINES` → `SALES_CHECKOUTS` và xóa ledger 51. Ưu tiên restore `.fbk` để tránh bỏ sót seed/constraint.

