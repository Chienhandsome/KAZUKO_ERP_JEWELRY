# TASK-008: SUPPLIER VERTICAL SLICE (QUẢN LÝ NHÀ CUNG CẤP)

> **Tài liệu phân tích kiến trúc & Kế hoạch triển khai — Revision 2**  
> **Trạng thái:** BLOCKED / PENDING ARCHITECTURAL & SCHEMA DECISION  
> **Implementation-ready:** **NO**  
> **Ngày cập nhật:** 25/09/2026  
> **Căn cứ phân tích:** Metadata thực tế từ `data/data.fdb` và mã nguồn WinForms chuẩn tại `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`  

---

## 1. Tóm tắt điều hành & Kết quả kiểm tra thực tế (Executive Summary)

Sau khi tiến hành đọc trực tiếp metadata của cơ sở dữ liệu `data/data.fdb` và rà soát toàn bộ mã nguồn phân hệ Nhà Cung Cấp trong dự án WinForms được duyệt (`D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`), kết quả xác minh chính thức như sau:

1. **Về Cơ sở dữ liệu (`data/data.fdb`):**
   - **HOÀN TOÀN CHƯA CÓ BẢNG NHÀ CUNG CẤP:** Không tồn tại bảng `SUPPLIERS`, `SUPPLIER`, `PARTNERS`, `VENDORS`, hay `NCC`.
   - **HOÀN TOÀN CHƯA CÓ BẢNG NHÓM NHÀ CUNG CẤP:** Không tồn tại bảng `SUPPLIER_GROUPS` hay tương đương.
   - Bảng `CUSTOMERS` hiện có chỉ chứa `CUSTOMER_TYPE = 'INDIVIDUAL'` (không dùng chung làm đối tác/nhà cung cấp).
   - Bảng chứng từ kho `STOCK_DOCUMENTS` chỉ có trường chuỗi tự do `COUNTERPARTY_NAME VARCHAR(200)` (không có khóa ngoại `SUPPLIER_ID`).
   - Không tồn tại bảng đơn mua hàng (`PURCHASE_ORDERS`), hóa đơn mua hàng (`PURCHASE_INVOICES`) hay phiếu chi trả nợ NCC (`SUPPLIER_PAYMENTS`).
2. **Về Mã nguồn WinForms chuẩn (`KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`):**
   - Phân hệ Quản lý Nhà Cung Cấp trong WinForms (`SupplierManagementControl.cs` và `SupplierEditorDialog.cs`) được ghi rõ là **MOCK UI**:
     ```csharp
     /// <summary>Mock UI Nhà cung cấp — bám layout mẫu (toolbar + filter + master-detail + thống kê).</summary>
     private List<MockSupplierRow> allRows = [];
     ```
   - Dữ liệu hiển thị hoàn toàn là danh sách in-memory gồm 10 bản ghi mock tĩnh (`NCC001` đến `NCC010`).
   - Phân hệ Công Nợ (`DebtManagementControl.cs`) cũng là **MOCK UI** (`/// <summary>Mock UI Công nợ — bám layout mẫu (KPI + tabs + master-detail).</summary>`).
   - Bộ chạy migration `FirebirdDatabaseMigrator.cs` (gồm 46 migration) **chưa từng tạo bất kỳ bảng nào cho Nhà Cung Cấp**.
3. **Kết luận kiến trúc:**
   - TASK-008 **chưa thể bắt đầu triển khai code ngay** (`Implementation-ready: NO`).
   - Toàn bộ DDL suy đoán trước đây phải bị loại bỏ hoàn toàn. Dự án cấm tự ý chạy `CREATE TABLE` hoặc thay đổi schema nếu chưa có migration script chính thức được cấp phép và versioned theo quy định tại `docs/00-PROJECT-REQUIREMENTS.md` và `docs/04-DEVELOPMENT-WORKFLOW.md`.

---

## 2. Bằng chứng kiểm tra Cơ sở dữ liệu thật (`data/data.fdb`)

### 2.1. Danh sách 46 bảng người dùng thực tế trong `data/data.fdb`
Truy vấn trực tiếp từ `RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL`:
```text
1. AUDIT_LOG                 17. LOCATIONS                   33. ROLE_PERMISSIONS
2. BACKUP_HISTORY            18. MATERIALS                   34. SALES_ORDERS
3. BRANCHES                  19. ORDER_ITEM_DESIGNS          35. SALES_ORDER_ITEMS
4. COMPANIES                 20. ORDER_PAYMENTS              36. SCHEMA_MIGRATIONS
5. CURRENCY_RATES            21. ORDER_STATUS_HISTORY        37. SCOPED_SETTINGS
6. CUSTOMERS                 22. PERMISSIONS                 38. STOCKTAKES
7. CUSTOMER_ACTIVITIES       23. PRODUCTION_MATERIAL_PLANS   39. STOCKTAKE_LINES
8. CUSTOMER_ADDRESSES        24. PRODUCTION_OPERATIONS       40. STOCK_BALANCES
9. CUSTOMER_CONTACTS         25. PRODUCTION_ORDERS           41. STOCK_DOCUMENTS
10. CUSTOMER_GROUPS          26. PRODUCTION_QC_INSPECTIONS   42. STOCK_DOCUMENT_LINES
11. CUSTOMER_MEDIA           27. PRODUCTION_QC_MEDIA         43. STOCK_MOVEMENTS
12. DESIGN_BOMS              28. PRODUCTION_STATUS_HISTORY   44. SYSTEM_SETTINGS
13. DESIGN_BOM_LINES         29. PRODUCTION_TIMELINE         45. USERS
14. DOCUMENT_SEQUENCES       30. ROLES                       46. USER_SCOPES
15. FINISHED_PRODUCTS        31. GOLD_PRICES
16. FINISHED_PRODUCT_MEDIA   32. GOLD_PRICE_HISTORY
```
*(Khẳng định: Không có bất kỳ bảng nào liên quan đến Nhà Cung Cấp).*

### 2.2. Chi tiết cấu trúc bảng chứng từ kho `STOCK_DOCUMENTS`
Bảng chứng từ kho duy nhất hiện tại có khả năng phát sinh luồng nhập hàng:
```text
Cột                      Kiểu dữ liệu        Độ dài   Scale   NotNull   Default / Check
-------------------------------------------------------------------------------------------------
ID                       BIGINT              8        0       TRUE      PRIMARY KEY
COMPANY_ID               BIGINT              8        0       TRUE      FK -> COMPANIES(ID)
BRANCH_ID                BIGINT              8        0       TRUE      FK -> BRANCHES(ID)
CODE                     VARCHAR             50       0       TRUE      UNIQUE(COMPANY_ID, CODE)
DOCUMENT_TYPE            VARCHAR             30       0       TRUE      CHECK ('OPENING', 'RECEIPT', 'ISSUE', 'TRANSFER')
DOCUMENT_DATE            DATE                4        0       TRUE
SOURCE_LOCATION_ID       BIGINT              8        0       FALSE     FK -> LOCATIONS(ID)
DESTINATION_LOCATION_ID  BIGINT              8        0       FALSE     FK -> LOCATIONS(ID)
PRODUCTION_ORDER_ID      BIGINT              8        0       FALSE     FK -> PRODUCTION_ORDERS(ID)
REFERENCE_NO             VARCHAR             400      0       FALSE
COUNTERPARTY_NAME        VARCHAR             800      0       FALSE     (Chuỗi tên đối tác tự do)
NOTES                    VARCHAR             4000     0       FALSE
STATUS_CODE              VARCHAR             20       0       TRUE      CHECK ('DRAFT', 'POSTED', 'CANCELLED')
POSTED_BY                BIGINT              8        0       FALSE     FK -> USERS(ID)
POSTED_AT                TIMESTAMP           8        0       FALSE
CANCELLED_BY             BIGINT              8        0       FALSE     FK -> USERS(ID)
CANCELLED_AT             TIMESTAMP           8        0       FALSE
CANCELLATION_REASON      VARCHAR             2000     0       FALSE
CREATED_BY               BIGINT              8        0       FALSE     FK -> USERS(ID)
CREATED_AT               TIMESTAMP           8        0       TRUE      CURRENT_TIMESTAMP
UPDATED_BY               BIGINT              8        0       FALSE     FK -> USERS(ID)
UPDATED_AT               TIMESTAMP           8        0       FALSE
PURPOSE_CODE             VARCHAR             30       0       TRUE      CHECK CK_STOCK_PURPOSE
```

### 2.3. Chi tiết bảng chi tiết dòng chứng từ kho `STOCK_DOCUMENT_LINES`
```text
Cột                      Kiểu dữ liệu        Độ dài   Scale   NotNull   Ghi chú
-------------------------------------------------------------------------------------------------
ID                       BIGINT              8        0       TRUE      PRIMARY KEY
DOCUMENT_ID              BIGINT              8        0       TRUE      FK -> STOCK_DOCUMENTS(ID) ON DELETE CASCADE
LINE_NUMBER              INTEGER             4        0       TRUE      UNIQUE(DOCUMENT_ID, LINE_NUMBER)
MATERIAL_ID              BIGINT              8        0       TRUE      FK -> MATERIALS(ID)
QUANTITY                 NUMERIC(18,4)       8        -4      TRUE      CHECK > 0
UNIT_COST                NUMERIC(18,2)       8        -2      TRUE      CHECK >= 0
LINE_AMOUNT              NUMERIC(18,2)       8        -2      TRUE      CHECK >= 0 (QUANTITY * UNIT_COST)
LOT_NO                   VARCHAR             400      0       FALSE
NOTES                    VARCHAR             2000     0       FALSE
PRODUCTION_MATERIAL_PLAN_ID BIGINT           8        0       FALSE     FK -> PRODUCTION_MATERIAL_PLANS(ID)
```

---

## 3. Đối chiếu chi tiết Mã nguồn WinForms chuẩn (`KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`)

### 3.1. Phân hệ Quản lý Nhà Cung Cấp (`SupplierManagementControl.cs`)
1. **Bản chất dữ liệu:**
   - Là UserControl dạng Mock UI hoàn toàn:
     ```csharp
     public sealed class SupplierManagementControl : UserControl
     {
         private List<MockSupplierRow> allRows = [];
         private List<MockSupplierRow> filteredRows = [];
         private MockSupplierRow? selected;
     ```
   - Không chứa bất kỳ câu lệnh SQL `SELECT`, `INSERT`, `UPDATE`, `DELETE` nào.
   - Được nạp sẵn 10 bản ghi mẫu:
     - `Row(1, "NCC001", "Công ty Vàng Bạc Phú Quý", "0903.111.222", "123 Lê Lợi, Q.1, TP.HCM", "TP. Hồ Chí Minh", "Vàng", 450_000_000, "Đang hợp tác", "0301234567", "contact@phuquy.vn", "Nguyễn Văn Phú", "Giám đốc kinh doanh", "Đối tác cung cấp vàng nguyên liệu 9999", null, new DateTime(2023, 1, 15))`
     - `Row(2, "NCC002", "Công ty TNHH Đá Quý Việt", ...)`
     - ... đến `NCC010`.
2. **Bộ lọc trên giao diện WinForms:**
   - Ô tìm kiếm từ khóa (`headerSearch`): Lọc theo `Code` hoặc `Name`.
   - Lọc Nhóm NCC (`groupFilter`): Các giá trị tĩnh gồm `"Tất cả nhóm NCC"`, `"Vàng"`, `"Đá quý"`, `"Kim loại"`, `"Khác"`.
   - Lọc Trạng thái (`statusFilter`): `"Tất cả trạng thái"`, `"Đang hợp tác"`, `"Ngừng hợp tác"`.
   - Lọc Khu vực (`regionFilter`): `"Tất cả khu vực"`, `"Hà Nội"`, `"TP. Hồ Chí Minh"`, `"Đà Nẵng"`, `"Cần Thơ"`, `"Khác"`.
   - Lọc Ngày (`fromDate`, `toDate`): Lọc theo `CreatedOn`.
3. **Thống kê đầu bảng:**
   - `statTotal`: Tổng số bản ghi NCC.
   - `statActive`: Số NCC có trạng thái "Đang hợp tác".
   - `statInactive`: Số NCC có trạng thái "Ngừng hợp tác".
   - `statDebt`: Tổng cộng dồn cột `Debt` của các NCC đang lọc.

### 3.2. Hộp thoại Thêm/Sửa Nhà Cung Cấp (`SupplierEditorDialog.cs`)
1. **Cấu trúc DTO in-memory `MockSupplierRow`:**
   ```csharp
   public sealed record MockSupplierRow(
       int Stt,
       string Code,
       string Name,
       string Phone,
       string Address,
       string Region,
       string GroupName,
       decimal Debt,
       string Status,
       string TaxCode,
       string Email,
       string ContactName,
       string ContactTitle,
       string Notes,
       string? ImagePath,
       DateTime CreatedOn);
   ```
2. **Quy tắc sinh mã:**
   - Trong WinForms mock:
     ```csharp
     codeBox.Text = $"NCC{DateTime.Now:HHmmss}";
     ```
   - Khi chỉnh sửa bản ghi có sẵn: `codeBox.ReadOnly = true;` (không cho phép sửa mã).
3. **Quy tắc Validation & Cảnh báo:**
   - Kiểm tra rỗng Mã NCC: `"Vui lòng nhập mã nhà cung cấp."` -> Focus vào `codeBox`.
   - Kiểm tra rỗng Tên NCC: `"Vui lòng nhập tên nhà cung cấp."` -> Focus vào `nameBox`.
   - Kiểm tra trùng lặp mã:
     ```csharp
     if (existing == null && allRows.Any(r => r.Code.Equals(result.Code, StringComparison.OrdinalIgnoreCase)))
     {
         MessageBox.Show(FindForm(), $"Mã NCC '{result.Code}' đã tồn tại (mock).", "TRÙNG MÃ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
         return;
     }
     ```
   - Cột công nợ `Debt` khi tạo mới luôn gán bằng `0` (`Debt: 0`).

---

## 4. Đặc tả Asset, Icon và Pack URI

Dựa trên cấu trúc solution hiện tại và mẫu chuẩn đã được duyệt tại `Kazuko.Modules.Customers`:

1. **Giao diện mẫu đối chiếu:**
   - Tệp ảnh: `src/Assets/examples/NCC.png`
   - Bố cục: Header thẻ thống kê (Tổng NCC, Đang hợp tác, Ngừng hợp tác, Tổng nợ) -> Toolbar tìm kiếm + filter comboboxes -> DataGrid danh sách NCC -> Chi tiết xem nhanh bên dưới.
2. **Tệp Icon chuẩn:**
   - Đường dẫn thực tế trong repository: `src/Assets/icons/hop.png` (Hình hộp hàng, đã tồn tại sẵn, kích thước 8,404 bytes).
3. **Cách thức nhúng Resource vào module `Kazuko.Modules.Suppliers`:**
   - Trong tệp project `src/Modules/Kazuko.Modules.Suppliers/Kazuko.Modules.Suppliers.csproj`:
     ```xml
     <ItemGroup>
       <Resource Include="..\..\Assets\icons\hop.png">
         <Link>Assets\icons\hop.png</Link>
       </Resource>
     </ItemGroup>
     ```
   - Trong `SuppliersModule.cs`:
     ```csharp
     private const string AssetRoot = "/Kazuko.Modules.Suppliers;component/Assets/icons/";

     navigation.Register(new NavigationItem(
         Key: "SUPPLIER",
         Label: "NHÀ CUNG CẤP",
         IconGlyph: string.Empty,
         SortOrder: 30,
         CreateView: null,
         ViewFactory: () => new SupplierView
         {
             DataContext = new SupplierViewModel(supplierRepo, supplierEditor, sessionAccessor)
         },
         IconUri: AssetRoot + "hop.png",
         Description: "Danh mục và công nợ nhà cung cấp",
         ShowInSidebar: true,
         ShowOnHome: true,
         IsPlaceholder: false));
     ```

---

## 5. Bảng Mapping Chi tiết: UI Field vs Database Hiện tại

Bảng đối chiếu toàn diện giữa các trường hiển thị trên UI và khả năng lưu trữ trong cơ sở dữ liệu `data.fdb` hiện hành:

| UI Field / Control | WinForms Property | Kiểu dữ liệu UI | Bắt buộc | Validation UI | Trạng thái trong DB `data.fdb` |
|---|---|---|---|---|---|
| **Mã nhà cung cấp** | `Code` | `string` (max 30) | **Có** | Không rỗng, không trùng, định dạng `NCCxxxxxx` | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Tên nhà cung cấp** | `Name` | `string` (max 200) | **Có** | Không rỗng, tối thiểu 2 ký tự | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Nhóm NCC** | `GroupName` | `string` / `long Id` | **Có** | Phải chọn một nhóm hợp lệ | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Mã số thuế** | `TaxCode` | `string` (max 50) | Không | Định dạng MST (10 hoặc 13 số nếu nhập), cảnh báo trùng | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Số điện thoại** | `Phone` | `string` (max 50) | Không | Định dạng số điện thoại 9-11 số, cảnh báo trùng | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Email** | `Email` | `string` (max 100) | Không | Định dạng email hợp lệ | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Địa chỉ** | `Address` | `string` (max 255) | Không | Tối đa 255 ký tự | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Khu vực** | `Region` | `string` (max 100) | Không | Chọn Tỉnh/Thành hoặc tự nhập | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Người liên hệ** | `ContactName` | `string` (max 100) | Không | Tối đa 100 ký tự | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Chức vụ liên hệ** | `ContactTitle` | `string` (max 100) | Không | Tối đa 100 ký tự | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Ghi chú** | `Notes` | `string` (max 1000) | Không | Tối đa 1000 ký tự | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Trạng thái** | `Status` | `bool` (`IsActive`) | **Có** | True = Đang hợp tác, False = Ngừng hợp tác | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Ảnh / Logo** | `ImagePath` | `string?` | Không | Đường dẫn file hoặc URI ảnh | **CHƯA CÓ CỘT / BẢNG** *(Blocker)* |
| **Tổng tiền mua** | `TotalPurchases` | `decimal` | Readonly | Tính tổng tiền hàng đã nhập từ NCC | Không thể tính chính xác vì `STOCK_DOCUMENTS` chỉ có chuỗi `COUNTERPARTY_NAME` |
| **Công nợ hiện tại** | `Debt` | `decimal` | Readonly | Công nợ còn phải trả cho NCC | Không có bảng thanh toán NCC trong DB |

---

## 6. Quy chuẩn Transaction & Audit Log (Tuân thủ Convention của Repository)

Khi tầng persistence được triển khai sau khi schema được phê duyệt, việc quản lý transaction và ghi nhận kiểm toán bắt buộc phải tuân thủ nghiêm ngặt quy chuẩn từ `FirebirdCustomerRepository`:

1. **Transaction chính cô lập hoàn toàn việc ghi dữ liệu nghiệp vụ:**
   - Khởi tạo transaction qua `await connection.BeginTransactionAsync(cancellationToken)`.
   - Biến cờ `var committed = false;` để giám sát trạng thái commit.
   - Toàn bộ thao tác `INSERT` / `UPDATE` nhà cung cấp được thực thi trên transaction này.
2. **Nguyên tắc Commit & Audit Log:**
   - Phải `await transaction.CommitAsync(cancellationToken);` thành công **TRƯỚC** khi gọi `AuditLogWriter`.
   - Sau khi commit thành công, đặt cờ `committed = true;`.
   - Gọi `AuditLogWriter.WriteAsync` bên ngoài transaction chính với các thông tin:
     - `ACTION`: `"SUPPLIER_CREATED"`, `"SUPPLIER_UPDATED"`, `"SUPPLIER_DEACTIVATED"`, `"SUPPLIER_ACTIVATED"`.
     - `ENTITY_TYPE`: `"SUPPLIERS"`.
     - `ENTITY_ID`: `id.ToString()`.
     - `USER_ID`, `USERNAME`, `BRANCH_ID` lấy từ `AuthenticatedSession`.
3. **Xử lý ngoại lệ an toàn:**
   - Bọc lệnh ghi `AuditLogWriter` trong khối an toàn; lỗi ghi audit log **tuyệt đối không được phép làm rollback hoặc làm hỏng dữ liệu nghiệp vụ đã commit thành công**.
   - Khối `catch` chung:
     ```csharp
     catch
     {
         if (!committed)
         {
             try
             {
                 await transaction.RollbackAsync(cancellationToken);
             }
             catch
             {
                 // Bỏ qua lỗi rollback; rethrow lỗi gốc
             }
         }
         throw;
     }
     ```

---

## 7. Đề xuất Hợp đồng Core & DTO (Chuẩn hóa dựa trên thực tế)

Mặc dù việc lưu trữ cơ sở dữ liệu bị chặn cho đến khi có migration, tầng Hợp đồng Core (`Kazuko.Core.Suppliers`) vẫn được chuẩn hóa theo đúng các trường nghiệp vụ thực tế rút ra từ WinForms:

```csharp
namespace Kazuko.Core.Suppliers;

using Kazuko.Core.Authentication;

public sealed record SupplierListItem(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Email,
    string SupplierGroupName,
    string Region,
    decimal TotalPurchases,
    decimal CurrentDebt,
    DateTime? LastPurchaseDate,
    bool IsActive,
    string Address,
    string ContactName,
    string ContactTitle,
    string TaxCode,
    string Notes,
    long SupplierGroupId);

public sealed record SupplierDetail(
    long Id,
    string Code,
    string Name,
    long SupplierGroupId,
    string SupplierGroupName,
    string ContactName,
    string ContactTitle,
    string Phone,
    string Email,
    string Address,
    string Region,
    string TaxCode,
    string Notes,
    bool IsActive);

public sealed record SupplierGroupOption(long Id, string Code, string Name, bool IsActive, bool IsDefault)
{
    public override string ToString() => Name;
}

public sealed record SupplierSearchFilter(
    string SearchText,
    long GroupId,
    string Region,
    bool? IsActive);

public sealed record SupplierSaveRequest(
    long? Id,
    string Name,
    long SupplierGroupId,
    string ContactName,
    string ContactTitle,
    string Phone,
    string Email,
    string Address,
    string Region,
    string TaxCode,
    string Notes,
    bool IsActive);

public sealed record SupplierEditResult(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Address,
    bool IsActive);

public interface ISupplierEditorService
{
    SupplierEditResult? ShowCreate();
    SupplierEditResult? ShowEdit(long supplierId);
}

public interface ISupplierRepository
{
    Task<IReadOnlyList<SupplierGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<SupplierDetail?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<string> FindDuplicateWarningAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierEditResult> SaveAsync(
        SupplierSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
```

---

## 8. Bằng chứng, Công thức và Điểm nghẽn (Final Verification Section)

### 8.1. Evidence Inspected (Bằng chứng đã đối soát)
1. **Firebird Embedded Database `data/data.fdb`:**
   - Đã kết nối và đọc schema trực tiếp thông qua engine `FirebirdSql.Data.FirebirdClient` và `fbclient.dll` phiên bản `5.0.4.1812`.
   - Toàn bộ 46 bảng người dùng trong database đã được liệt kê đầy đủ.
   - Tìm kiếm hệ thống (`RDB$RELATIONS`, `RDB$RELATION_FIELDS`) cho thấy: **Không có bất kỳ bảng hoặc cột nào chứa từ khóa `SUPPLIER`, `VENDOR`, `PARTNER`, hay `NCC`**.
   - Bảng `CUSTOMERS` chỉ lưu khách hàng cá nhân (`CUSTOMER_TYPE = 'INDIVIDUAL'`).
   - Bảng `STOCK_DOCUMENTS` chỉ lưu đối tác dạng chuỗi tự do `COUNTERPARTY_NAME`, không có liên kết quan hệ (Foreign Key).
2. **WinForms Approved Project `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`:**
   - `src/KazukoErp.Desktop/Controls/SupplierManagementControl.cs`: Là Mock UI với danh sách in-memory `List<MockSupplierRow> allRows`.
   - `src/KazukoErp.Desktop/Forms/SupplierEditorDialog.cs`: Là hộp thoại thao tác trên `MockSupplierRow`.
   - `src/KazukoErp.Desktop/Controls/DebtManagementControl.cs`: Là Mock UI công nợ in-memory.
   - `src/KazukoErp.Desktop/Data/FirebirdDatabaseMigrator.cs`: 46 migration tạo bảng từ `SCHEMA_MIGRATIONS` đến `STOCKTAKE_LINES`, không có migration nào cho Nhà Cung Cấp.

### 8.2. Confirmed Schema (Hiện trạng Schema xác nhận)
- **Bảng Nhà Cung Cấp:** Chưa tồn tại trong cơ sở dữ liệu.
- **Bảng Nhóm Nhà Cung Cấp:** Chưa tồn tại trong cơ sở dữ liệu.
- **Bảng Chứng từ Mua hàng / Nhập hàng NCC:** Chưa có bảng chuyên biệt; chỉ có `STOCK_DOCUMENTS` với `DOCUMENT_TYPE = 'RECEIPT'` và `COUNTERPARTY_NAME`.
- **Bảng Thanh toán / Công nợ NCC:** Chưa tồn tại (bảng `ORDER_PAYMENTS` chỉ phục vụ thanh toán đơn bán hàng của khách hàng qua `ORDER_ID`).

### 8.3. Confirmed Debt Formula (Công thức công nợ xác nhận)
- Trong WinForms gốc: Không có công thức SQL tính toán công nợ NCC thực tế; giá trị `Debt` được lưu tĩnh trên bản ghi Mock hoặc gán cố định bằng `0`.
- Trên thực tế cơ sở dữ liệu: **Chưa thể xác lập công thức tính công nợ tự động** do thiếu bảng ghi nhận các khoản thanh toán cho nhà cung cấp (`SUPPLIER_PAYMENTS`).
- **Quyết định:** Nếu chưa có phân hệ Thanh toán NCC (`FINANCE` / `PURCHASE_PAYMENTS`), cột công nợ và tổng mua trên giao diện NCC trong vertical slice đầu tiên phải được thiết kế hiển thị rõ ràng là `0` hoặc hiển thị thông báo "Chưa phát sinh chứng từ mua" kèm ghi chú quyết định minh bạch, tuyệt đối không bịa đặt số liệu giả.

### 8.4. Remaining Blockers & Questions (Điểm nghẽn cần quyết định)
1. **BLOCKER 1: Cần Migration chính thức cho Nhà Cung Cấp.**
   - Theo quy định `AGENTS.md` và `docs/00-PROJECT-REQUIREMENTS.md`, Agent không được tự ý sửa database hoặc tự tạo DDL trái chuẩn.
   - **Câu hỏi cho Lead/Architect:** Dự án sẽ cấp một task migration riêng (ví dụ `TASK-008-MIGRATION`) để tạo bảng `SUPPLIERS` và `SUPPLIER_GROUPS`, hay sẽ sử dụng phương án đối tác chung (`PARTNERS`)?
2. **BLOCKER 2: Liên kết giữa Nhập kho (`STOCK_DOCUMENTS`) và Nhà Cung Cấp.**
   - Bảng `STOCK_DOCUMENTS` hiện chỉ lưu `COUNTERPARTY_NAME VARCHAR(200)`. Khi nhập kho mua nguyên liệu từ NCC, hệ thống sẽ thêm cột `SUPPLIER_ID` vào `STOCK_DOCUMENTS` hay sẽ tạo phân hệ Đơn mua hàng (`PURCHASE_ORDERS`) riêng?
3. **BLOCKER 3: Xử lý hiển thị trong khi chờ Schema.**
   - Trong trường hợp muốn hoàn thiện giao diện WPF cho Nhà Cung Cấp trước (UI slice), module có được phép sử dụng `InMemorySupplierRepository` (seed dữ liệu từ WinForms mock) để kiểm tra giao diện, phím tắt, chuyển trang thay thế placeholder, trước khi gắn `FirebirdSupplierRepository` thật hay không?

### 8.5. Implementation-ready
```text
Implementation-ready: NO
```
*(Yêu cầu phải có quyết định chính thức từ Architect/Lead về Schema Migration của phân hệ Nhà Cung Cấp trước khi viết mã nguồn persistence).*
