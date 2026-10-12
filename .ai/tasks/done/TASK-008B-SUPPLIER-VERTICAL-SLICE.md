# TASK-008B: SUPPLIER VERTICAL SLICE IMPLEMENTATION

## Assigned Agent
Antigravity

## Status
DONE

## Goal
Triển khai trọn vẹn vertical slice Nhà cung cấp (Supplier Management) end-to-end theo schema migration 0030 đã được duyệt trên database `data/data.fdb`.

## Implementation Details

### 1. Core Domain & Contracts (`src/Kazuko.Core/Suppliers/`)
- `SupplierContracts.cs`:
  - DTOs: `SupplierListItem`, `SupplierDetail`, `SupplierGroupOption`, `SupplierSearchFilter`, `SupplierSaveRequest`, `SupplierEditResult`.
  - Service contracts: `ISupplierRepository` và `ISupplierEditorService`.
  - Tuân thủ Clean Architecture: không có dependency ngược tới WPF hay database engine.

### 2. Persistence Layer (`src/Kazuko.Infrastructure/Suppliers/`)
- `FirebirdSupplierRepository.cs`:
  - `GetGroupsAsync(bool activeOnly)`: Đọc danh mục nhóm nhà cung cấp đã seed theo công ty KAZUKO.
  - `SearchAsync(SupplierSearchFilter filter)`: Tìm kiếm linh hoạt đa trường (Mã NCC, Tên, SĐT, Email, MST, Người liên hệ), tính tổng tiền mua từ các chứng từ kho nhập `STOCK_DOCUMENTS` (RECEIPT / POSTED), công nợ trả về `0` chuẩn mực khi chưa có phân hệ thanh toán NCC.
  - `GetByIdAsync(long id)`: Đọc chi tiết thông tin nhà cung cấp.
  - `FindDuplicateWarningAsync(SupplierSaveRequest request)`: Kiểm tra trùng lặp MST hoặc Số điện thoại với nhà cung cấp khác cùng công ty, loại trừ chính bản ghi đang sửa.
  - `SaveAsync(SupplierSaveRequest request, AuthenticatedSession session)`:
    - Chuẩn hóa chuỗi (null-safe trim) và validate dữ liệu bắt buộc (Tên, SĐT, Nhóm hoạt động, định dạng Email).
    - Quản lý giao dịch nguyên tử (`FbTransaction`): Sinh mã `NCC{id:000000}` tự động theo ID bản ghi.
    - Ghi nhận kiểm toán an toàn qua `AuditLogWriter`: `SUPPLIER_CREATED`, `SUPPLIER_UPDATED`, `SUPPLIER_GROUP_CHANGED` sau khi giao dịch nghiệp vụ đã commit thành công.
  - `SetActiveAsync(long id, bool isActive, AuthenticatedSession session)`: Ngừng hợp tác hoặc kích hoạt lại, ghi audit `SUPPLIER_ARCHIVED` / `SUPPLIER_REACTIVATED`.

### 3. WPF UI Module (`src/Modules/Kazuko.Modules.Suppliers/`)
- `Kazuko.Modules.Suppliers.csproj`: Liên kết resource icon `hop.png` (`/Kazuko.Modules.Suppliers;component/Assets/icons/hop.png`).
- `SupplierView.xaml` & `SupplierView.xaml.cs`:
  - Bố cục chuẩn theo thiết kế được duyệt: Header thẻ thống kê KPI (Tổng NCC, Đang hợp tác, Ngừng hợp tác, Tổng tiền hàng nhập, Tổng công nợ).
  - Thanh công cụ tìm kiếm và lọc đa tiêu chí (từ khóa, nhóm, khu vực, trạng thái).
  - DataGrid chuyên nghiệp xen kẽ dòng `SurfaceBrush` / `SurfaceAltBrush`, hiển thị STT, Mã NCC, Tên, Nhóm, SĐT, Khu vực, Người liên hệ, Tổng mua, Công nợ, Lần mua cuối, Badge trạng thái `SuccessBrush` / `TextSecondaryBrush`, cột thao tác nhanh.
  - Phím tắt F1 tạo nhanh nhà cung cấp mới.
  - Master-detail drawer bên dưới hiển thị chi tiết đối tác và tóm tắt công nợ/nhập hàng.
- `SupplierViewModel.cs` & `SupplierRowViewModel.cs`: Điều phối toàn bộ nghiệp vụ View, cập nhật thống kê KPI, quản lý chọn dòng, chuyển trạng thái và mở dialog chỉnh sửa.
- `SupplierEditDialog.xaml` & `SupplierEditDialog.xaml.cs`: Form thêm/sửa trực quan, thông báo lỗi trường, cảnh báo trùng lặp MST/SĐT có xác nhận tiếp tục lưu, phím tắt Enter lưu và Escape hủy.
- `SupplierEditorService.cs`: Hiện thực `ISupplierEditorService` phục vụ mở dialog từ bất kỳ module nào mà không vi phạm ranh giới kiến trúc.
- `SuppliersModule.cs`: Đăng ký module `Kazuko.Suppliers`, thay thế sạch sẽ placeholder navigation `SUPPLIER` (SortOrder 30, hiển thị cả Sidebar và Home).

### 4. Composition Root (`src/Kazuko.App/`)
- `Kazuko.App.csproj`: Thêm project reference tới `Kazuko.Modules.Suppliers`.
- `App.xaml.cs`: Đăng ký singleton `ISupplierRepository` (`FirebirdSupplierRepository`) và nạp `SuppliersModule` vào pipeline khởi động.

### 5. Automated Tests & Quality Assurance (`tests/Kazuko.Infrastructure.Tests/Suppliers/`)
- `SupplierRepositoryTests.cs`:
  - Kiểm thử validation bắt buộc: Tên rỗng, SĐT rỗng, Nhóm không hợp lệ, Email sai định dạng, Session null.
  - Kiểm thử phương thức `ToString()` và tính bất biến của các DTO records.
  - Kiểm thử kiểm tra trùng lặp `FindDuplicateWarningAsync`.
  - Kiểm thử tích hợp `GetGroupsAsync` và `SearchAsync`.
  - Toàn bộ 81/81 test pass 100%.

## Build & Verification
- `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release` -> Passed (0 Warning, 0 Error).
- `dotnet test KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build` -> Passed (81/81 tests).
- `git diff --check` -> Clean (0 whitespace/formatting issue).
- Database `data/data.fdb` không bị tác động ngoài schema migration 30 chính thức.
