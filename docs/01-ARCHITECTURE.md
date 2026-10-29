# Kiến trúc

## Báo cáo lương nhân sự

HR mở workspace của module Reports qua `IEmployeeReportsPresenter` (Core), không tham chiếu assembly Reports. `IPayrollReportService` đọc snapshot kỳ lương và phiếu chi liên kết theo công ty của session; UI/VM không chứa SQL. Hai workspace dùng khung báo cáo PDF và xuất Excel chung, không có thao tác tính/chốt/chi tiền trong báo cáo. Xem `docs/09-REPORTING.md`.

## Dependency direction

Tra cứu MST nhà cung cấp dùng `IBusinessTaxLookupService` (Core), triển khai
`VietQrBusinessTaxLookupService` (Infrastructure) qua HTTP như TestScan. Chỉ hỗ trợ
tra cứu MST gốc 10 số; không tra chi nhánh hoặc tự bỏ hậu tố để điền công ty mẹ.
MST chi nhánh vẫn nhập/lưu thủ công được. Không cần tài khoản/API key.
Bấm Tra cứu chỉ điền MST/tên/địa chỉ vào bản nháp, không tự lưu. Không xóa trường
thiếu trong phản hồi; kiểm tra MST phản hồi khớp yêu cầu, chặn lưu/chỉnh form trong
lúc tra cứu và hủy yêu cầu khi đóng dialog. Timeout 15 giây, thông báo lỗi mạng,
giới hạn lượt hoặc nguồn chưa có dữ liệu. Không migration. Form dùng hàng Grid
chung cho hai cột để giữ căn hàng khi hiện lỗi/thông báo.
Theo yêu cầu người dùng, bỏ tích hợp Xinvoice và giao diện cấu hình khóa.
VietQR thông báo ngừng endpoint này từ 01/03/2027; cần xem lại nguồn API trước mốc đó.
Nguồn: https://www.vietqr.io/tra-cuu-mst

```text
Kazuko.App ───────────────┐
Modules ───────┐          │
Shared.UI ─────┼──> Kazuko.Core
Infrastructure ┘          │
Kazuko.App ─────────> Infrastructure
```

`Kazuko.Core` không tham chiếu WPF, Firebird hoặc project nghiệp vụ. `Infrastructure` hiện thực các interface về lưu cấu hình, database, file, update và dịch vụ hệ thống. `Shared.UI` chứa theme, style và control WPF dùng chung. `Kazuko.App` chỉ composition/bootstrap và host shell.

Layout cột DataGrid/báo cáo dùng `ITableColumnLayoutStore` và DTO snapshot v1 trong
Core.Settings. Sau đăng nhập, App tải `DatabaseTableColumnLayoutSession` trước khi
mở shell; repository Firebird ghim DB/session và lưu bảng `USER_TABLE_LAYOUTS`
(migration 93), theo công ty/tài khoản/bảng. Cache/outbox trên máy phục vụ UI và
retry bất đồng bộ, xem [table-column-layout.md](table-column-layout.md).

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

Danh mục phân loại phiếu thu/chi dùng contract Core `IVoucherCategoryService`,
implementation Infrastructure, UI Finance; dữ liệu theo doanh nghiệp và hướng phiếu,
phiếu lưu snapshot tên loại. Xem `docs/finance-voucher-categories.md` (migration88).

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
quyền xem bảng lương. Migration89 nối thưởng/kỷ luật và chi lương với phiếu thu/chi
trong cùng transaction; xem `docs/employee-money-payroll.md`. Điều chỉnh nhập tay
giữ riêng với khoản tự lấy từ sổ phát sinh, tính lại không cộng trùng.

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

**Xóa ca** cần xác nhận và CAT_EMPLOYEES/Xóa. Backend chỉ xóa ca thuộc công ty của session và chưa có HR_ATTENDANCE_RECORDS; ca có chấm công phải ngừng dùng. Xóa và audit cùng transaction, FK giữ lịch sử khi có ghi đồng thời. Không cascade hay migration. Nút xóa không xuất hiện trong form thêm ca mới.

Chấm công → **Danh mục ca** dùng cùng luồng với danh mục loại thu/chi: danh sách trái, sửa ca bên phải, **Thêm ca mới** mở form con; lưu xong tải lại và chọn ca vừa lưu. `WorkShiftCatalogViewModel` dùng repository được inject; `ShiftEditDialogViewModel` dùng chung validation thêm/sửa, tên tối đa 50 ký tự, giờ tùy chọn HH:mm, cho phép ca qua đêm, không cho hai giờ trùng nhau. `WorkShiftSaveRequest.IsActive` lưu vào cột IS_ACTIVE có sẵn, không migration. Chỉ ngừng dùng, không xóa record chấm công: lưới tháng vẫn gồm ca không hoạt động nếu tháng có record của ca đó. Quyền CAT_EMPLOYEES/Xem để mở, Thêm/Sửa cho từng thao tác. Kiểm tra ở `WorkShiftCatalogTests`, `AttendancePeriodTests` và `Kazuko.WorkShifts.IntegrationChecks` trên bản restore test89.

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

## Quét QR CCCD trong form khách hàng (2026-10-22)

`CitizenIdentityQrParser` trong Core phân tích dữ liệu HID từ iMarcone thành DTO,
không phụ thuộc WPF. `CitizenScanDialogViewModel` quản lý đọc, xem lại, đối chiếu,
xác nhận thay giấy tờ và chọn hồ sơ. `CustomerEditorService` mở hộp quét từ form
chung nên danh mục, bán hàng và công nợ dùng cùng luồng và trả `CustomerEditResult`
chứa ID hồ sơ được chọn. Enter tại ô quét chỉ đọc; áp dụng chỉ sửa bản nháp.

`ICustomerRepository.FindIdentityMatchesAsync` trả danh sách có kiểu, gồm hồ sơ
ngừng hoạt động, theo công ty của chi nhánh trong phiên đăng nhập. So khớp chính
xác CCCD 12 số hoặc CMND cũ 9 số trong TAX_CODE, giữ số 0 đầu, không dò theo tên.
`GetByIdAsync` nhận session tùy chọn để editor đọc lại đúng phạm vi. Không tự
kích hoạt hồ sơ ngừng hoạt động, không gán CCCD cho khách vãng lai hệ thống.

Không thêm schema: tái sử dụng TAX_CODE, NAME, BIRTH_OR_ESTABLISHED_DATE, GENDER,
ADDRESS, ID_ISSUE_DATE. CMND cũ là hint đối chiếu trong request lưu, không lưu QR
thô/lịch sử quét. SĐT và nơi cấp cần người dùng nhập; các trường không có trong QR
được giữ nguyên. Parser hỗ trợ đúng cấu trúc 7/6/5 trường của TestScan, ngày hợp lệ,
Unicode NFC và giới hạn độ dài; chuỗi lỗi không được áp dụng.

Trong SaveAsync, TAX_CODE đúng 12 chữ số ASCII được xem là CCCD: trùng bị chặn
bắt buộc, kể cả nhập tay. Quy tắc cảnh báo MST độ dài khác giữ nguyên. Save khóa
hàng công ty trước khi đọc trùng/ghi trong transaction ReadCommitted + RecVersion
+ Wait (timeout 10 giây); mọi save khách hàng cùng công ty đi qua khóa này để
hai quầy không tạo cùng CCCD. Không áp unique constraint lên trường MST dùng chung.
Nhóm và hồ sơ được kiểm tra theo cùng companyId trước khi ghi. Kiểm thử transaction
dùng database tạm, không ghi khách hàng thử vào DATA.FDB chính.

Hướng dẫn kiểm tra thiết bị và rollback: `docs/manual-tests/customer-cccd-scan.md`.

Cột Từ CCCD trong preview cho phép chỉnh cả sáu trường trước áp dụng bằng draft
`CitizenFieldChange` có thông báo thay đổi; không sửa snapshot Trên form. Mỗi lần
sửa vô hiệu kết quả đối chiếu cũ, tăng revision để bỏ phản hồi muộn; sửa số CCCD
reset xác nhận đổi danh tính. ApplyAsync validate draft theo cùng parser Core,
ngày nhập dd/MM/yyyy, rồi đối chiếu lại trước khi áp dụng/chọn hồ sơ trùng.

## Bộ lọc tab lịch sử mua hàng và công nợ khách hàng

`CustomerActivityTabViewModel` sở hữu riêng bộ chọn khách hàng, từ/đến ngày,
dữ liệu, trạng thái loading/error và cancellation token cho mỗi tab. CustomerViewModel
có hai instance HistoryTab/DebtTab; thay SelectedCustomer hoặc bộ lọc ở danh sách
không thay chúng. Mới mở chưa chọn khách ở tab nào. Bộ chọn tải khách bằng query
riêng, có tìm mã/tên/SĐT, không lấy từ tập Customers đang lọc ở danh mục.
Shortcut Xem tất cả/Xem chi tiết ở card danh mục chủ động chọn khách vào đúng tab.
Preview ở danh mục chỉ tải 5 giao dịch và snapshot cho dòng được chọn, không tải
lịch sử đầy đủ/ledger của tab độc lập.

GetPurchaseHistoryAsync nhận thêm fromDate/toDate tùy chọn sau cancellationToken,
lọc trong SQL trước FIRST 100; ngày cuối gồm cả ngày. Không migration. Tab công nợ
lọc khoảng ngày trên các khoản thanh toán/hoàn tiền của ledger, snapshot công nợ
và phân bổ thu tiền vẫn trên toàn bộ khoản mở của khách. Thu tiền lấy ID từ
DebtTab.SelectedCustomer, không lấy dòng chọn trong danh mục. Sau thu tiền reload
bộ lọc hiện có; hủy/đổi khách trong lúc tải bỏ phản hồi cũ. Xem
`docs/manual-tests/customer-activity-filters.md`.

## Bảng giá TV

PriceBoard hỗ trợ cửa sổ HDMI/Miracast và trang HTTP chỉ đọc trên LAN, dùng giá
bán lẻ hiện có. Core giữ cấu hình/snapshot, Infrastructure lưu JSON cục bộ và phục
vụ tài nguyên web nhúng; module WPF điều phối phiên đăng nhập, cập nhật giá và
WebView2. Phần phát chạy trong tiến trình riêng (`--price-board-host`) với tray,
đọc giá qua nguồn chỉ đọc theo công ty. Đóng/đăng xuất ERP chỉ detach điều khiển;
host vẫn giữ HTTP/cửa sổ và tự cập nhật DB, kiểm tra quyền phát định kỳ. IPC chỉ
cho cùng tài khoản Windows, singleton theo DB/chi nhánh/phiên Windows; không đưa
credential vào command line/file. Miracast dùng picker Cast của Windows, sau kết nối người dùng chọn màn
hình đích; ITvDisplayService giữ thao tác UI hệ thống, theo dõi thay đổi màn hình
và không tự chọn màn hình chính khi TV ngắt. Không migration database. Xem
[thiết lập và hành vi](tv-price-board.md).
