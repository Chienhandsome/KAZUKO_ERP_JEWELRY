# Kiến trúc

## Dependency direction

```text
Kazuko.App ───────────────┐
Modules ───────┐          │
Shared.UI ─────┼──> Kazuko.Core
Infrastructure ┘          │
Kazuko.App ─────────> Infrastructure
```

`Kazuko.Core` không tham chiếu WPF, Firebird hoặc project nghiệp vụ. `Infrastructure` hiện thực các interface về lưu cấu hình, database, file, update và dịch vụ hệ thống. `Shared.UI` chứa theme, style và control WPF dùng chung. `Kazuko.App` chỉ composition/bootstrap và host shell.

## Luồng chuẩn

```text
View → ViewModel → Application Service → Repository → Firebird
```

- View binding state và command.
- ViewModel điều phối use case, không chứa SQL hay biết chi tiết Firebird.
- Application Service giữ transaction và quy tắc nghiệp vụ liên quan nhiều repository.
- Repository chịu trách nhiệm persistence.

## Module

Module triển khai `IKazukoModule`:

```csharp
public interface IKazukoModule
{
    string Id { get; }
    Version Version { get; }
    void RegisterServices(IServiceRegistry services);
    void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services);
}
```

Quy ước ID: `Kazuko.<Domain>`, ví dụ `Kazuko.Warehouse`. Navigation key dùng chữ hoa ổn định như `WAREHOUSE` hoặc `WAREHOUSE.MATERIALS`. Permission dùng dạng `<domain>.<resource>.<action>`, ví dụ `warehouse.material.read`.

`NavigationItem` là metadata đóng góp bởi module cho cả shell và trang chủ. Ngoài key/label/View factory, descriptor có icon URI do assembly module sở hữu, mô tả, cờ hiện trên sidebar/trang chủ và trạng thái placeholder. `MainWindow` và `DashboardView` chỉ render registry, không switch theo key nghiệp vụ.

Các route placeholder dùng để dựng khung giao diện trước khi module nghiệp vụ hoàn chỉnh. Khi module thật đăng ký cùng key, `ModuleNavigationRegistry` chỉ cho phép route thật thay thế placeholder; trùng hai route thật vẫn bị từ chối. Home tile yêu cầu điều hướng qua `INavigationService`, vì vậy module không tham chiếu ngược tới `Kazuko.App`.

Đây là contract để module được kích hoạt ở lần khởi động sau có thể tự đóng góp menu/home tile. Việc khám phá và load assembly từ gói `.kazuko-module` vẫn thuộc pipeline activation, chưa được coi là hoàn thiện chỉ vì contract UI đã tồn tại.

## Session và giao tiếp giữa module

- `AuthenticatedSession` + `ICurrentSessionAccessor` (Core) giữ UserId/Username/DisplayName/RoleCode/Branch* sau login; App đăng ký singleton và clear khi logout. Không chứa mật khẩu.
- Module nghiệp vụ không tham chiếu lẫn nhau. Dialog/use case dùng chung (ví dụ khách hàng) expose contract WPF-neutral trong Core (`ICustomerEditorService`, `CustomerEditResult`) và implementation nằm trong module sở hữu.
- SQL chỉ ở Infrastructure (`FirebirdCustomerRepository`). Module Customers tham chiếu Core + Shared.UI; repository được App đăng ký tại composition root.

Tạo tài khoản trong UserManagement truyền `BranchId` của phiên đăng nhập qua
`UserSaveRequest`; repository đồng bộ `DEFAULT_BRANCH_ID` và các cột legacy bắt buộc
`BRANCH_CODE`, `BRANCH_NAME`, `ROLE_CODE`. Khi không có chi nhánh phiên, dùng chi nhánh
mặc định của người tạo rồi mới fallback chi nhánh hoạt động đầu tiên. Role đăng nhập
khớp mã nhóm nếu có trong `ROLES`; CASHIER/nhóm tự tạo dùng `SALE`, không sao chép
role của người tạo. Form tài khoản không có lựa chọn Kho; sửa tài khoản giữ kho đã lưu.

Phân quyền nhóm ở UI và application boundary, ánh xạ thao tác và phối hợp role cũ:
xem `docs/11-GROUP-AUTHORIZATION.md`.

## Module package updates (TASK-007A)

Core contracts trong `Kazuko.Core.Updates`:

- `IModulePackageInstaller` — validate + stage only (không load DLL).
- `ITrustedPublicKeyStore` / `IHostVersionProvider` / `IModulePackageSignatureVerifier`.
- Manifest v2: `packageFormatVersion`, payload allow-list (path/sha256/length), publisher/keyId, `RSASSA-PSS-SHA256`.

Infrastructure `ModulePackageInstaller` thực hiện kiểm tra ZIP an toàn và staging.
`IModuleActivationCatalog` (TASK-007B1) promote staging → Installed và ghi `PendingActivation`.
Cold-start activation, migration và UI Import thuộc task sau.

## Form động

Database chỉ lưu mô tả đã giới hạn:

- Control type nằm trong allow-list.
- Binding trỏ tới field/use case đã đăng ký.
- Validation dùng rule type và tham số JSON.
- Action trỏ tới command key do module cung cấp.
- Không lưu/running arbitrary code.

Schema dự kiến gồm `FORM_DEFINITIONS`, `FORM_FIELDS`, `FORM_ACTIONS`, `FORM_PERMISSIONS` và version/audit tương ứng. JSON phải được validate bằng schema trước khi kích hoạt.

## Database migration

- Migration chỉ tiến về phía trước và có số version duy nhất.
- Nội dung migration có checksum; production phải dừng nếu checksum của migration đã chạy bị thay đổi.
- Migration module chạy sau backup và trước activation.
- Mỗi migration phải idempotent khi hợp lý và có kế hoạch phục hồi dữ liệu.

## Báo cáo / in ấn

Quyết định mẫu báo cáo A4 và xem trước/in: xem `docs/09-REPORTING.md` (FastReport Open Source + file `.frx`).

## Hồ sơ nhân viên và cấu hình lương

Hồ sơ nhân viên chỉ lưu cách tính lương, lương ca/lương tháng và ngày nghỉ tuần.
Không còn lương cơ bản/phụ cấp trong DTO, form, repository và schema sau migration 81.
Migration 80 chuyển lương cơ bản cũ sang lương tháng; migration 81 bỏ hai cột cũ và
constraint liên quan. Chạy migration theo thứ tự bằng runner, giữ bản sao gbak trước
khi xóa cột để khôi phục dữ liệu cùng phiên bản ứng dụng trước nếu cần.

## Bảng lương tháng

`IPayrollService` (Infrastructure) giữ transaction cho tính nháp, điều chỉnh, chốt/mở chốt.
Mỗi công ty có tối đa một bảng lương mỗi tháng. Công chuẩn = ngày trong tháng trừ ngày
nghỉ tuần của nhân viên; theo ca nhân số ca chuẩn/ngày của kỳ (mặc định 1, chỉnh 1–24).
Theo ca: công thực tế × lương ca. Lương tháng: công thực tế × lương tháng / công chuẩn.
Công theo ngày đếm ngày phân biệt; theo ca đếm ô chấm công. Đi làm luôn được tính;
nghỉ có phép chỉ tính khi bật lựa chọn của kỳ, nghỉ không phép không tính. Ngày có nhiều
ca chỉ tính một công với lương theo ngày. Công ngoài ngày nghỉ tuần vẫn được tính theo
đơn giá thường và cảnh báo khi vượt công chuẩn; chưa tự tính hệ số tăng ca/ngày lễ.
Không tự sinh công cho ngày chưa chấm. Chỉ tính chấm công từ ngày vào làm, mẫu số vẫn là
công chuẩn toàn tháng. Nhân viên hoạt động và nhân viên có chấm công trong kỳ được đưa vào.
Số tiền làm tròn VND, AwayFromZero. Thưởng/khấu trừ không âm, cần diễn giải, thực nhận không âm.

Migration 82 lưu header HR_PAYROLL_PERIODS và dòng HR_PAYROLL_LINES. Dòng lưu snapshot JSON
cấu hình nhân viên/chấm công cùng đơn giá, công, số tiền; độc lập thay đổi hồ sơ tương lai.
Tính lại giữ điều chỉnh cũ; nếu nhân viên có điều chỉnh không còn trong nguồn thì dừng,
yêu cầu bỏ điều chỉnh trước khi tính lại. Thay đổi dùng revision chống ghi đè; bảng chốt
không cho tính lại hoặc điều chỉnh. Chốt kiểm tra hash nguồn để từ chối bảng đã lỗi thời.
Mở chốt cần quyền HR_PAYROLL_REOPEN/Sửa và lý do; các thao tác ghi audit trong transaction.
Quyền HR_PAYROLL dùng Xem/Thêm(tính)/Sửa(điều chỉnh,chốt). Export cần DATA_EXPORT cùng
quyền xem bảng lương. Không liên kết chi tiền ở giai đoạn này.

## Khách vãng lai

Migration 83 thêm CUSTOMERS.IS_WALK_IN (boolean, mặc định false). Cờ này phân biệt
khách hệ thống dùng chung cho giao dịch vãng lai với khách có hồ sơ, độc lập
CUSTOMER_TYPE và nhóm khách. Chỉ migration chuyển ba mã legacy KH-VL/WALK_IN/KHACH_LE
sang cờ; runtime không suy luận từ mã. Không đổi ID hoặc chuyển chứng từ lịch sử.

Khách hệ thống được xem lịch sử nhưng backend Customers từ chối sửa hồ sơ/đổi
trạng thái. Tạo mới qua form luôn là khách có hồ sơ. Sales/Wholesale/Craft đọc lại
cờ từ database khi ghi nghiệp vụ; cấm ghi nợ, thu đổi/mua vàng cũ và đơn đặt với
khách vãng lai. Lookup mua bán ưu tiên khách vãng lai ID nhỏ nhất đang hoạt động;
lookup vàng cũ và đơn đặt chỉ chọn khách có hồ sơ.

Danh mục và báo cáo in có lọc Có hồ sơ/Vãng lai/Tất cả, mặc định Có hồ sơ. Top,
khách chưa quay lại và phân bố CRM loại vãng lai. Tổng hợp doanh thu theo hai loại
dùng cùng kỳ, công ty và trạng thái chứng từ như Top (bỏ DRAFT/CANCELLED); tổng
cộng không áp giới hạn Top N. Báo cáo doanh thu chung vẫn bao gồm cả hai loại.

Triển khai/khôi phục: xem docs/manual-tests/walk-in-customer.md.

## Lưu kỳ chấm công

Migration 84 thêm HR_ATTENDANCE_PERIODS, khóa (COMPANY_ID,PERIOD_YEAR,PERIOD_MONTH).
Kỳ được lưu ngay khi bấm Thêm kỳ tháng, độc lập số ô đã chấm; không tạo ô chấm công
giả. Migration đưa các tháng có record cũ vào danh mục mà không đổi records/lương.
GetAttendanceMonthsAsync trả cả kỳ trống và HasData; alias Year/Month phải có dấu
nháy kép vì là từ khóa Firebird. Làm mới tải lại cả danh sách kỳ và lưới, lỗi tải
có thể thử lại; UI chỉ hiển thị kỳ mới sau khi backend lưu thành công. Khi chưa
có kỳ, thao tác Thêm tạo tháng hiện tại; các lần tiếp theo tạo tháng kế tiếp.

CreateAttendanceMonthAsync cần CAT_EMPLOYEES/Thêm; DeleteAttendanceMonthAsync cần
CAT_EMPLOYEES/Xóa. Xóa kỳ có record bị backend chặn trong transaction. Gán trạng
thái ô và xóa kỳ cùng khóa header để tránh xóa kỳ đang được ghi. Xóa trạng thái
cuối cùng vẫn giữ header kỳ; chỉ xóa kỳ trống bằng thao tác riêng. Các thay đổi kỳ
ghi audit cùng transaction. Kỳ chấm công và snapshot bảng lương là hai nghiệp vụ
riêng: xóa kỳ chấm công trống không xóa bảng lương.
