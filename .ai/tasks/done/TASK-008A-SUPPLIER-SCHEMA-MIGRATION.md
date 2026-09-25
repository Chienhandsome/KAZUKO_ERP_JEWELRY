# TASK-008A: SUPPLIER SCHEMA MIGRATION

## Assigned Agent
Antigravity

## Status
DONE (Hotfixed for Strict Migration Safety & 100% Atomicity)

## Goal
Tạo migration chính thức cho danh mục Nhà cung cấp và Nhóm nhà cung cấp, bổ sung cột `SUPPLIER_ID` (nullable) vào bảng `STOCK_DOCUMENTS`, sao lưu và khôi phục kiểm tra qua `gbak.exe`, áp dụng an toàn lên cơ sở dữ liệu Firebird 5.0 `data\data.fdb` qua công cụ wrapper atomic `Invoke-KazukoMigration.ps1`, ghi nhận sổ cái `SCHEMA_MIGRATIONS` với mã băm SHA-256 của toàn bộ artifact, xác minh tự động fail-fast, chuẩn bị sẵn sàng schema cho TASK-008B.

## Context & Hotfix Rationale
- **Vấn đề cần Hotfix:**
  1. File migration 0030 trước đó có lệnh `COMMIT;` trung gian giữa DDL và Seed. Khi có lỗi xảy ra sau DDL (trong lúc seed), giao dịch seed bị rollback nhưng schema DDL đã bị commit dở dang vào database, làm hỏng tính toàn vẹn (atomicity).
  2. Artifact cần được loại bỏ hoàn toàn lệnh `COMMIT;` (0 COMMIT trong artifact).
  3. Áp dụng cơ chế bảo vệ giao dịch ở tầng runner (`Invoke-KazukoMigration.ps1`): tự động tạo snapshot trước khi chạy và tự động hoàn nguyên cơ sở dữ liệu về nguyên trạng khi xảy ra bất kỳ lỗi nào.
  4. Thắt chặt công cụ áp dụng: kiểm tra tên migration `^[A-Za-z0-9_]+$`, escape đường dẫn có khoảng trắng, kiểm tra exit code của truy vấn sổ cái.
  5. Kiểm thử bắt buộc gây lỗi tại 2 điểm: sau khi tạo `SUPPLIER_GROUPS` và sau khi seed ít nhất một group. Cả 2 trường hợp đều phải bảo đảm sạch 100% schema và ledger.

## Implementation Details

### 1. Artifacts Created & Updated
- `database/migrations/0030__supplier_core.sql`: DDL bảng, chỉ mục, partial unique index, seed 4 nhóm NCC và pre/post validations. Hoàn toàn KHÔNG chứa lệnh `COMMIT;` (0 COMMIT).
- `database/tools/Invoke-KazukoMigration.ps1`: Công cụ wrapper tự động hóa tính SHA-256, kiểm tra idempotency, snapshot sao lưu tự động và hoàn nguyên khi có sự cố, quản lý ranh giới DDL/DML tương thích kiến trúc Firebird.
- `database/verification/verify_supplier_schema.sql`: Script kiểm tra metadata fail-fast, xác minh toàn bộ bảng, cột, khóa, partial index, seed, ledger checksum và functional rollback test.
- `docs/05-DATABASE-MIGRATIONS.md`: Tài liệu hoàn chỉnh về quy chuẩn migration, wrapper, sao lưu, chi tiết version 30 và quy trình rollback.
- `.ai/PROJECT.md`: Cập nhật ghi nhận schema phân hệ Nhà cung cấp.

### 2. Migration Version & Checksums
- **Version:** `30`
- **Migration Name:** `SUPPLIER_CORE`
- **Số lệnh COMMIT trong artifact:** `0`
- **Full Artifact SHA-256 (Tệp & Sổ cái giống nhau 100%):**
  `64FA278236812ABCAFCCD305E2B8CC4B6DB1FAA255345C3A501DC86A2C283373`

### 3. Schema Created & Updated
1. **Bảng `SUPPLIER_GROUPS`:**
   - Cột: `ID`, `COMPANY_ID`, `CODE`, `NAME`, `DESCRIPTION`, `IS_ACTIVE`, `IS_DEFAULT`, `DISPLAY_ORDER`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
   - Constraints: `PK_SUPPLIER_GROUPS`, `FK_SUPPLIER_GROUPS_COMPANY`, `UQ_SUPPLIER_GROUPS_CODE`, `UQ_SUPPLIER_GROUPS_COMP_ID`, `FK_SUPPLIER_GROUPS_CREATE_USER`, `FK_SUPPLIER_GROUPS_UPDATE_USER`.
   - Index: `IX_SUPPLIER_GROUPS_ACTIVE` (`COMPANY_ID, IS_ACTIVE, DISPLAY_ORDER`).
   - **Partial Unique Index:** `UQ_SUPPLIER_GROUPS_ONE_DEFAULT` (`(COMPANY_ID) WHERE IS_DEFAULT = TRUE AND IS_ACTIVE = TRUE`) bảo đảm tối đa 1 nhóm mặc định hoạt động ở cấp độ database engine.
2. **Bảng `SUPPLIERS`:**
   - Cột: `ID`, `COMPANY_ID`, `CREATED_BRANCH_ID`, `SUPPLIER_GROUP_ID`, `CODE`, `NAME`, `PHONE`, `EMAIL`, `ADDRESS`, `REGION`, `TAX_CODE`, `CONTACT_NAME`, `CONTACT_TITLE`, `NOTES`, `IS_ACTIVE`, `CREATED_BY`, `CREATED_AT`, `UPDATED_BY`, `UPDATED_AT`.
   - Không chứa các cột suy đoán: `CREDIT_LIMIT`, `BANK_ACCOUNT`, `IMAGE_PATH`, `TOTAL_PURCHASES`, `CURRENT_DEBT`.
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
- **Bản sao lưu trước migration:** `data\backups\data_pre_task008a_20260925_154134.fbk` (Kích thước: 273,408 bytes).
  - Xác minh restore qua `gbak -c` vào database tạm: Thành công (Exit code 0).
- **Bản sao lưu trước hotfix:** `data\backups\data_pre_hotfix_20260925_155857.fbk`.
  - Xác minh restore qua `gbak -c` vào database tạm: Thành công (Exit code 0).

### 6. Atomicity Test Suite
Đã kiểm thử trên database tạm phục hồi từ backup:
- **Bằng chứng lỗi partial schema trước hotfix:**
  Chèn lỗi cố ý sau lệnh `COMMIT;` trung gian (ngay sau seed đầu tiên). Khi chạy `isql -b`, transaction seed bị rollback nhưng DDL đã commit trước đó vẫn tồn tại:
  `SUPPLIER_GROUPS = 1`, `SUPPLIERS = 1`, `STOCK_DOCUMENTS.SUPPLIER_ID = 1`, `SEEDED_GROUPS = 0`, `LEDGER_V30 = 0`. Database bị kẹt ở trạng thái schema dở dang mà không có sổ cái.
- **Fault Test 1 (Lỗi cố ý sau khi tạo `SUPPLIER_GROUPS`):**
  Chèn lệnh lỗi ngay sau bước tạo bảng `SUPPLIER_GROUPS`. `Invoke-KazukoMigration.ps1` phát hiện lỗi `isql`, kích hoạt hoàn nguyên tự động từ snapshot. Kết quả: `SUPPLIER_GROUPS = 0`, `SUPPLIERS = 0`, `STOCK_DOCUMENTS.SUPPLIER_ID = 0`, `LEDGER_V30 = 0` (100% sạch).
- **Fault Test 2 (Lỗi cố ý sau khi seed ít nhất một group):**
  Chèn lệnh lỗi ngay sau câu lệnh seed nhóm `VANG`. `Invoke-KazukoMigration.ps1` phát hiện lỗi `isql`, kích hoạt hoàn nguyên tự động từ snapshot. Kết quả: `SUPPLIER_GROUPS = 0`, `SUPPLIERS = 0`, `STOCK_DOCUMENTS.SUPPLIER_ID = 0`, `LEDGER_V30 = 0` (100% sạch).
- **Test apply migration thật:** Áp dụng sạch qua `Invoke-KazukoMigration.ps1`, toàn bộ DDL, index, partial index, seed 4 groups và sổ cái được commit thành công (Exit code 0).
- **Test chạy fail-fast verification:** `verify_supplier_schema.sql` xác thực thành công toàn bộ điều kiện, bao gồm functional rollback test (Exit code 0).
- **Test chạy lại cùng version (Idempotency):** Nhận diện đã áp dụng với checksum khớp `64FA2782...`, thoát an toàn (Exit code 0).
- **Test chạy lại với checksum khác:** Phát hiện lệch checksum và từ chối thực thi (Exit code 1).

### 7. Apply & Post-Migration Verification trên Development DB
- Xác nhận trạng thái trước rollback: `SUPPLIERS` count = 0, `STOCK_DOCUMENTS.SUPPLIER_ID` đều null, `SUPPLIER_GROUPS` count = 4.
- Tạo bản backup kiểm tra mới: `data\backups\data_pre_atomicity_fix_20260925_163422.fbk` (Restore verified qua `gbak -c`).
- Rollback an toàn migration 30 cũ trên `data\data.fdb` (xác nhận về 0 sạch sẽ).
- Áp dụng migration 30 mới (0 COMMIT trong artifact, full SHA-256 = `64FA2782...`) qua `Invoke-KazukoMigration.ps1` thành công (Exit code 0).
- Xác minh bằng `verify_supplier_schema.sql` thành công (Exit code 0).
- Dữ liệu 46 bảng người dùng cũ nguyên vẹn.
- Partial unique index chặn thành công bản ghi chèn trùng `IS_DEFAULT = TRUE AND IS_ACTIVE = TRUE`.

## Build / Verification
- `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release` -> Passed (0 Warning, 0 Error).
- `dotnet test KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build` -> Passed (33/33 tests).
- `git diff --check` -> Passed.
- Xác nhận không có database binary (`.fdb`, `.fbk`) hay password trong git staging.
- Database WinForms gốc `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` hoàn toàn không bị tác động.
