# Quản lý Database Migration & Quy chuẩn Schema Firebird

## 1. Nguyên tắc cốt lõi

- **Tiến về phía trước (Forward-only):** Mọi thay đổi schema trên Firebird phải đi qua tệp migration SQL được đánh số thứ tự tăng dần.
- **Sổ cái `SCHEMA_MIGRATIONS`:** Bảng theo dõi lịch sử migration gồm:
  - `VERSION_NO` (`INTEGER`, Khóa chính).
  - `MIGRATION_NAME` (`VARCHAR(120)`).
  - `CHECKSUM_VALUE` (`VARCHAR(64)` - mã băm SHA-256 dạng hex chữ hoa).
  - `APPLIED_AT` (`TIMESTAMP DEFAULT CURRENT_TIMESTAMP`).
- **Xác định phiên bản kế tiếp:** Luôn truy vấn `SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS` trên cơ sở dữ liệu thật trước khi đặt tên file, tuyệt đối không suy đoán số thứ tự.
- **Quy chuẩn đặt tên tệp:** `database/migrations/<VERSION_4_DIGITS>__<snake_case_name>.sql`, ví dụ: `0030__supplier_core.sql`.
- **An toàn giao dịch:** Migration script phải chạy có kiểm soát giao dịch (`COMMIT;`), có kiểm tra tiền điều kiện (pre-flight checks), và ghi sổ cái sau cùng khi toàn bộ DDL/Seed thành công.
- **Bảo mật:** Không bao giờ ghi thông tin xác thực/mật khẩu vào tệp SQL, script, log hay git.
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

### Bước 2: Áp dụng Migration bằng `isql.exe`
```powershell
& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe" -b -ch UTF8 -i "database\migrations\0030__supplier_core.sql" localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb
```

### Bước 3: Xác minh Schema sau Migration
```powershell
& ".\firebird\Firebird-5.0.4.1812-0-windows-x64\isql.exe" -ch UTF8 -i "database\verification\verify_supplier_schema.sql" localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb
```

---

## 3. Chi tiết Migration Version 30: `0030__supplier_core.sql`

- **Tệp nguồn:** `database/migrations/0030__supplier_core.sql`
- **Phiên bản:** `30`
- **Tên migration:** `SUPPLIER_CORE`
- **Mã băm Payload SHA-256 (ghi nhận sổ cái):** `ED312F73F4CA762E9E1219A9E10C10C85444FD56B54BE9441DE066642C3026D5`
- **Mã băm Tệp SHA-256:** `41ACD8BF9B02AD17F1B98239F8DBEDF76FA015E0B67254B61189936B2427088D`

### 3.1. Bảng `SUPPLIER_GROUPS`
Lưu trữ danh mục nhóm nhà cung cấp phục vụ phân loại và chiết khấu/chính sách:
- Cột: `ID` (PK, Identity), `COMPANY_ID` (FK `COMPANIES`), `CODE` (VARCHAR(30) ASCII), `NAME` (VARCHAR(150)), `DESCRIPTION` (VARCHAR(500)), `IS_ACTIVE` (BOOLEAN), `IS_DEFAULT` (BOOLEAN), `DISPLAY_ORDER` (INTEGER), `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
- Ràng buộc & Chỉ mục:
  - Khóa chính: `PK_SUPPLIER_GROUPS` (`ID`).
  - Khóa duy nhất: `UQ_SUPPLIER_GROUPS_CODE` (`COMPANY_ID, CODE`).
  - Khóa duy nhất hỗ trợ FK composite: `UQ_SUPPLIER_GROUPS_COMP_ID` (`COMPANY_ID, ID`).
  - Khóa ngoại công ty: `FK_SUPPLIER_GROUPS_COMPANY`.
  - Khóa ngoại kiểm toán người dùng: `FK_SUPPLIER_GROUPS_CREATE_USER`, `FK_SUPPLIER_GROUPS_UPDATE_USER`.
  - Chỉ mục tìm kiếm / sắp xếp: `IX_SUPPLIER_GROUPS_ACTIVE` (`COMPANY_ID, IS_ACTIVE, DISPLAY_ORDER`).

### 3.2. Bảng `SUPPLIERS`
Lưu trữ thông tin chi tiết nhà cung cấp theo chuẩn nghiệp vụ WinForms đã xác thực:
- Cột: `ID` (PK, Identity), `COMPANY_ID` (FK `COMPANIES`), `CREATED_BRANCH_ID` (FK `BRANCHES`), `SUPPLIER_GROUP_ID` (FK `SUPPLIER_GROUPS`), `CODE` (VARCHAR(30) ASCII), `NAME` (VARCHAR(200)), `PHONE`, `EMAIL`, `ADDRESS`, `REGION`, `TAX_CODE`, `CONTACT_NAME`, `CONTACT_TITLE`, `NOTES`, `IS_ACTIVE`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
- Không lưu các cột suy đoán: Không thêm `CREDIT_LIMIT`, `BANK_ACCOUNT`, `IMAGE_PATH`, `TOTAL_PURCHASES`, hoặc `CURRENT_DEBT`.
- Ràng buộc & Chỉ mục:
  - Khóa chính: `PK_SUPPLIERS` (`ID`).
  - Khóa duy nhất: `UQ_SUPPLIERS_CODE` (`COMPANY_ID, CODE`).
  - Khóa duy nhất hỗ trợ FK composite: `UQ_SUPPLIERS_COMP_ID` (`COMPANY_ID, ID`).
  - Khóa ngoại composite bảo đảm đồng bộ công ty: `FK_SUPPLIERS_GROUP` (`COMPANY_ID, SUPPLIER_GROUP_ID`) tham chiếu `SUPPLIER_GROUPS(COMPANY_ID, ID)`.
  - Khóa ngoại chi nhánh và kiểm toán: `FK_SUPPLIERS_BRANCH`, `FK_SUPPLIERS_CREATE_USER`, `FK_SUPPLIERS_UPDATE_USER`.
  - Chỉ mục tra cứu: `IX_SUPPLIERS_COMPANY_ACTIVE`, `IX_SUPPLIERS_GROUP`, `IX_SUPPLIERS_PHONE`, `IX_SUPPLIERS_TAX_CODE`.

### 3.3. Bảng `STOCK_DOCUMENTS` (Cập nhật)
- Thêm cột: `SUPPLIER_ID BIGINT NULL`.
- Khóa ngoại: `FK_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`) tham chiếu `SUPPLIERS(COMPANY_ID, ID)`. Khóa ngoại composite cho phép giá trị `SUPPLIER_ID` là `NULL` cho các chứng từ không gắn NCC (như điều chuyển, xuất sản xuất), nhưng khi có giá trị thì bắt buộc NCC phải thuộc cùng `COMPANY_ID`.
- Chỉ mục: `IX_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`).
- Bảo toàn dữ liệu: Cột `COUNTERPARTY_NAME` giữ nguyên làm text snapshot; dữ liệu lịch sử giữ nguyên và có `SUPPLIER_ID = NULL`.

### 3.4. Dữ liệu mồi (Seed Data)
Được seed cho công ty có mã `KAZUKO`:
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
