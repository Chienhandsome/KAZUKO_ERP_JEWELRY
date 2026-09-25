# TASK-008A: SUPPLIER SCHEMA MIGRATION

## Assigned Agent
Antigravity

## Status
DONE

## Goal
Tạo migration chính thức cho danh mục Nhà cung cấp và Nhóm nhà cung cấp, bổ sung cột `SUPPLIER_ID` (nullable) vào bảng `STOCK_DOCUMENTS`, sao lưu và khôi phục kiểm tra qua `gbak.exe`, áp dụng an toàn lên cơ sở dữ liệu Firebird 5.0 `data\data.fdb` qua `isql.exe`, ghi nhận sổ cái `SCHEMA_MIGRATIONS` với mã băm SHA-256, chuẩn bị sẵn sàng schema cho TASK-008B.

## Context
- Đã kiểm tra metadata thực tế của `data\data.fdb`:
  - Trước migration có 46 bảng người dùng, chưa có bảng `SUPPLIERS` hay `SUPPLIER_GROUPS`.
  - Bảng `SCHEMA_MIGRATIONS` ban đầu có 29 bản ghi (từ phiên bản 1 đến 29). Phiên bản migration kế tiếp chính xác là **30** (không phải 47).
  - Bảng `STOCK_DOCUMENTS` trước đây chỉ lưu tên chuỗi tự do `COUNTERPARTY_NAME` và chưa có khóa ngoại nhà cung cấp.
  - Bảng `CUSTOMERS` lưu khách hàng độc lập, không dùng chung cho NCC.
- WinForms gốc tại `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` là Mock UI (`MockSupplierRow`) và tuyệt đối không bị mở hay sửa đổi.
- Quy chuẩn định danh, ràng buộc, transaction và audit log tuân thủ chặt chẽ các bảng hiện hành.

## Implementation Details

### 1. Artifacts Created
- `database/migrations/0030__supplier_core.sql`: Script DDL, composite constraints, indexes, seed data và ledger insert.
- `database/verification/verify_supplier_schema.sql`: Script kiểm tra metadata, constraints, seed, scoping và transaction test.
- `docs/05-DATABASE-MIGRATIONS.md`: Tài liệu quy chuẩn migration, sao lưu, chi tiết version 30 và quy trình rollback.
- `.ai/PROJECT.md`: Cập nhật ghi nhận schema phân hệ Nhà cung cấp.

### 2. Migration Version & Checksums
- **Version:** `30`
- **Migration Name:** `SUPPLIER_CORE`
- **Payload SHA-256 (lưu trong `SCHEMA_MIGRATIONS`):** `ED312F73F4CA762E9E1219A9E10C10C85444FD56B54BE9441DE066642C3026D5`
- **File SHA-256 (`0030__supplier_core.sql`):** `41ACD8BF9B02AD17F1B98239F8DBEDF76FA015E0B67254B61189936B2427088D`

### 3. Schema Created & Updated
1. **Bảng `SUPPLIER_GROUPS`:**
   - Cột: `ID`, `COMPANY_ID`, `CODE`, `NAME`, `DESCRIPTION`, `IS_ACTIVE`, `IS_DEFAULT`, `DISPLAY_ORDER`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
   - Constraints: `PK_SUPPLIER_GROUPS`, `FK_SUPPLIER_GROUPS_COMPANY`, `UQ_SUPPLIER_GROUPS_CODE`, `UQ_SUPPLIER_GROUPS_COMP_ID`, `FK_SUPPLIER_GROUPS_CREATE_USER`, `FK_SUPPLIER_GROUPS_UPDATE_USER`.
   - Index: `IX_SUPPLIER_GROUPS_ACTIVE` (`COMPANY_ID, IS_ACTIVE, DISPLAY_ORDER`).
2. **Bảng `SUPPLIERS`:**
   - Cột: `ID`, `COMPANY_ID`, `CREATED_BRANCH_ID`, `SUPPLIER_GROUP_ID`, `CODE`, `NAME`, `PHONE`, `EMAIL`, `ADDRESS`, `REGION`, `TAX_CODE`, `CONTACT_NAME`, `CONTACT_TITLE`, `NOTES`, `IS_ACTIVE`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
   - Không chứa: `CREDIT_LIMIT`, `BANK_ACCOUNT`, `IMAGE_PATH`, `TOTAL_PURCHASES`, `CURRENT_DEBT`.
   - Constraints: `PK_SUPPLIERS`, `FK_SUPPLIERS_COMPANY`, `FK_SUPPLIERS_BRANCH`, `FK_SUPPLIERS_GROUP` (composite FK tham chiếu `SUPPLIER_GROUPS(COMPANY_ID, ID)`), `UQ_SUPPLIERS_CODE`, `UQ_SUPPLIERS_COMP_ID`, `FK_SUPPLIERS_CREATE_USER`, `FK_SUPPLIERS_UPDATE_USER`.
   - Indexes: `IX_SUPPLIERS_COMPANY_ACTIVE`, `IX_SUPPLIERS_GROUP`, `IX_SUPPLIERS_PHONE`, `IX_SUPPLIERS_TAX_CODE`.
3. **Bảng `STOCK_DOCUMENTS` (Cập nhật):**
   - Thêm cột: `SUPPLIER_ID BIGINT NULL`.
   - Khóa ngoại composite: `FK_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`) tham chiếu `SUPPLIERS(COMPANY_ID, ID)`.
   - Index: `IX_STOCK_DOC_SUPPLIER` (`COMPANY_ID, SUPPLIER_ID`).
   - `COUNTERPARTY_NAME` giữ nguyên. Dữ liệu cũ giữ nguyên với `SUPPLIER_ID = NULL`.

### 4. Seed Data
4 nhóm nhà cung cấp cho `COMPANY CODE = 'KAZUKO'` (lấy dynamic ID từ bảng `COMPANIES`):
1. `VANG` - "Vàng" (`IS_DEFAULT = TRUE`, `DISPLAY_ORDER = 1`)
2. `DA_QUY` - "Đá quý" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 2`)
3. `KIM_LOAI` - "Kim loại" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 3`)
4. `KHAC` - "Khác" (`IS_DEFAULT = FALSE`, `DISPLAY_ORDER = 4`)

### 5. Backup & Restore Verification
- **Bản sao lưu trước migration:** `data\backups\data_pre_task008a_20260925_154133.fbk` (Kích thước: 273,408 bytes).
  - Xác minh restore qua `gbak -c` vào database tạm: Thành công (Exit code 0).
- **Bản sao lưu sau migration:** `data\backups\data_post_task008a_20260925_154659.fbk`.
  - Xác minh restore qua `gbak -c` vào database tạm: Thành công (Exit code 0).

### 6. Apply & Post-Migration Verification
- Lệnh áp dụng: `isql.exe -b -ch UTF8 -i database\migrations\0030__supplier_core.sql localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb` -> Thành công (Exit code 0).
- Lệnh xác minh: `isql.exe -ch UTF8 -i database\verification\verify_supplier_schema.sql localhost:D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb` -> Toàn bộ metadata, khóa chính, khóa ngoại, unique constraint, chỉ mục và seed data khớp chính xác (Exit code 0).
- Kiểm tra tính toàn vẹn 46 bảng cũ: Giữ nguyên số lượng bản ghi đầy đủ.
- Kiểm tra transaction mẫu: Chèn thử nghiệm nhà cung cấp mẫu, truy vấn tính tổng mua (`TOTAL_PURCHASES` qua `STOCK_DOCUMENTS` + `STOCK_DOCUMENT_LINES`), sau đó rollback thành công, database sạch.
- Kiểm tra cô lập dữ liệu công ty: Vi phạm composite FK `COMPANY_ID` bị Firebird chặn đúng theo thiết kế.

## Build / Verification
- `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release` -> Passed (0 Warning, 0 Error).
- `dotnet test KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build` -> Passed (33/33 tests).
- `git diff --check` -> Passed.
- Xác nhận không có database binary (`.fdb`, `.fbk`) hay password trong git staging.
- Database WinForms gốc `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` hoàn toàn không bị tác động.
