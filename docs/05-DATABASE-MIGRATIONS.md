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
