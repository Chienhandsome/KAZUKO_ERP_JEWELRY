# Phân quyền theo nhóm

Xóa danh mục ca dùng CAT_EMPLOYEES/Xóa (`DeleteWorkShiftAsync`); xóa loại thu/chi dùng FIN_CASH_IN/Xóa hoặc FIN_CASH_OUT/Xóa theo hướng (`IVoucherCategoryService.DeleteAsync`). Add/Edit không thay thế Delete. UI hỏi xác nhận trước gọi backend; backend chặn mục đã có dữ liệu sử dụng và audit khi xóa thành công.

Báo cáo lương tháng và phiếu lương cá nhân yêu cầu đồng thời **REP_VIEW/Xem + HR_PAYROLL/Xem**. `IPayrollReportService` có guard riêng trước rule ReportService chung, kiểm tra actual session qua proxy. In/mở/lưu PDF thêm REP_EXPORT/Xem; Excel thêm DATA_EXPORT/Xem. Mất quyền xem sẽ xóa snapshot và PDF tạm ở workspace. Không thêm grant mặc định hay migration.

Migration 76 tạo nhóm/quyền; migration 77 mở rộng danh mục từ 28 lên 41 chức năng.
Không sửa checksum migration 76 đã áp dụng. Chạy migration 77 qua runner chuẩn,
backup và restore thử trước khi áp dụng database chính. Rollback dùng backup trước 77.

## Quy tắc

- Xem là điều kiện của Thêm/Sửa/Xóa. Ba quyền thao tác độc lập.
- Thiếu nhóm, nhóm ngừng hoạt động, thiếu dòng quyền hoặc Bị khóa: từ chối.
- Admin hệ thống được xác định từ USERS + ROLES đang hoạt động trong database,
  không tin RoleCode do caller truyền vào. Admin không phụ thuộc ma trận nhóm.
- Chỉ Admin hệ thống được gán tài khoản vào nhóm ADMIN hoặc sửa quyền nhóm này.
- Giữ các giới hạn chi nhánh, kiểm tra dữ liệu và chống ghi trùng hiện có.
- Các quyền cũ về sửa giá, duyệt bán nợ, đổi nhân viên bán hàng cần cả grant nhóm
  tương ứng và grant ROLE_PERMISSIONS cũ. Không dùng điều kiện OR.
- Các bước tự sinh trong một nghiệp vụ (bán hàng xuất kho, hoàn thành LSX sinh
  công nợ, thu/chi công nợ sinh phiếu) thuộc quyền thao tác khởi đầu và transaction
  hiện có; không đòi thêm quyền thao tác thủ công ở các module đích.

## Các lớp kiểm soát

`IAuthorizationService`/`FirebirdAuthorizationService` tải snapshot theo tài khoản
hiện tại. `NavigationAccess` ánh xạ route vào chức năng; sidebar, home tile và
điều hướng đều dùng cùng policy. Route mới chưa ánh xạ bị từ chối.

`PermissionAccess.Rule` gắn tường minh trong XAML cho nút/tab. Nút không có quyền
bị vô hiệu hóa; tab không có quyền bị ẩn. Binding quyền kết hợp với binding cũ và
CanExecute; các ViewModel Kho/Bán hàng/Công nợ/Tài chính chỉ tải các vùng được xem.

Tại composition root, repository/application service nghiệp vụ được bọc bằng
`AuthorizedServiceProxy<T>`. `BackendAccess` là bảng ánh xạ contract/method sang
quyền. Guard đọc quyền mới nhất trước khi gọi implementation, kiểm tra user/branch
so với session thật; không gọi implementation khi bị từ chối. Các helper nội bộ
chạy trong transaction của use case đã được cho phép. Khi thêm public service
hoặc phương thức nghiệp vụ phải bổ sung policy và đăng ký guard, không tự mở quyền.

Các danh sách tra cứu phục vụ chức năng đã được cấp (khách hàng trong bán hàng,
loại vàng trong nhập kho, quỹ/phương thức thanh toán) không yêu cầu quyền quản lý
danh mục đó. Thao tác tạo/sửa danh mục vẫn cần quyền riêng.

Menu/tab nhận thay đổi quyền trong tối đa 15 giây. Ghi dữ liệu kiểm tra lại ngay
trước khi chạy; nếu quyền bị thu hồi, thao tác bị từ chối và dữ liệu đang nhập vẫn
ở form. Logout xóa snapshot. In/mở PDF bên ngoài cần REP_EXPORT; xuất Excel dữ liệu
nghiệp vụ cần DATA_EXPORT. Xem báo cáo trong app chỉ cần REP_VIEW.

Từ chối quyền và thay đổi ma trận được ghi qua AuditLogWriter hiện có; thay đổi
ma trận ghi kèm quyền trước đó và yêu cầu mới trong transaction của thao tác lưu.

Báo cáo Top khách hàng trong màn Khách hàng (`ICustomerRepository.GetTopCustomersAsync`)
được ánh xạ `REP_VIEW` tại `BackendAccess`, gồm cả đường gọi qua proxy của Admin.
Các phương thức chưa được ánh xạ vẫn bị từ chối mặc định.

Nhóm NCC dùng quyền `CAT_SUPPLIERS`: xem danh sách/chi tiết dùng Xem;
lưu nhóm mới dùng Thêm, lưu nhóm đã có dùng Sửa, xóa nhóm dùng Xóa.
Lịch sử nhập NCC phục vụ `CAT_SUPPLIERS`, `INV_HISTORY`, `DEBT_CUSTOMER` hoặc
`REP_VIEW`; báo cáo Top NCC dùng `REP_VIEW`. Các đường gọi này đi qua cùng proxy.

## Ánh xạ thao tác đặc biệt

`IEmployeeMoneyService`: List cần CAT_EMPLOYEES/Xem; Create cần Thêm, thêm
FIN_CASH_OUT/Thêm hoặc FIN_CASH_IN/Thêm khi chi/thu riêng. Cancel cần
CAT_EMPLOYEES/Xóa; proxy đọc bản ghi thật để đòi thêm FIN_REVERSE/Xóa nếu CASH.
PaySalary cần HR_PAYROLL/Sửa + FIN_CASH_OUT/Thêm; CancelSalaryPayment cần
HR_PAYROLL/Sửa + FIN_REVERSE/Xóa. Không thêm grant mặc định. Chi tiết:
`docs/employee-money-payroll.md`.

Danh mục loại thu/chi (`IVoucherCategoryService`) dùng `FIN_CASH_IN`/`FIN_CASH_OUT`
theo hướng của request: List dùng Xem, Save mới dùng Thêm, Save loại đã có (kể cả
ngừng dùng) dùng Sửa. App đăng ký qua AuthorizedServiceProxy. Không cấp chéo
quyền Thu sang Chi và không thêm grant mặc định. Xem `docs/finance-voucher-categories.md`.

| Chức năng | Cột dùng để cho phép thao tác |
|---|---|
| INV_STOCK_VIEW, INV_HISTORY, SALES_HISTORY | Xem |
| INV_SALES_INTAKE | Sửa: duyệt hoặc từ chối |
| INV_REVERSE, PROD_CANCEL, FIN_REVERSE | Xóa: hủy/đảo chứng từ |
| PROD_COMPLETE, PROD_QC | Sửa: hoàn thành/nhận lại/QC |
| SALES_PRICE_OVERRIDE, SALES_DEBT_APPROVE, SALES_CHANGE_EMPLOYEE | Sửa |
| PROD_GOLD_RECONCILE, DEBT_SETTLEMENT | Thêm: ghi đối chiếu/thu chi |
| DEBT_WORKER_GOLD | Sửa: ghi trả vàng |
| REP_EXPORT, DATA_EXPORT | Xem: cho phép xuất/in |
| SYS_DATABASE | Xem thông tin; Sửa sao lưu/đổi DB; Thêm tạo DB trắng |

Tạo DB trắng còn yêu cầu SYS_DATA_DELETE/Xóa. Các grant mới nhạy cảm mặc định tắt
cho nhóm thường. Migration không tự xóa grant Sửa cũ vì không phân biệt được grant
do kế thừa trước đây với grant quản trị viên chủ ý cấp; cần rà lại ma trận nhóm.

## Kiểm thử

- `Kazuko.Modules.UserManagement.Tests`: độc lập CRUD, catalog 41 chức năng.
- `Kazuko.UserManagement.IntegrationChecks --unit`: deny mặc định, khóa, lọc route,
  chặn trước side effect, thu hồi quyền, bảo vệ Admin và chống giả session.
- `--ui`: WPF nút/tab, giữ CanExecute và mở form.
- Database tạm restore từ backup đã có 77: tạo/sửa/đăng nhập tài khoản, thay đổi
  grant trong DB, khóa nhóm, Admin không bị khóa theo nhóm.

Migration 82 thêm HR_PAYROLL và HR_PAYROLL_REOPEN (mặc định từ chối nhóm thường).
HR_PAYROLL: Xem tải bảng; Thêm tính/tính lại; Sửa điều chỉnh và chốt. Mở chốt cần
cả HR_PAYROLL/Xem và HR_PAYROLL_REOPEN/Sửa, phải có lý do. Bảng lương chốt chỉ đọc.
Export Excel bảng lương cần DATA_EXPORT/Xem và HR_PAYROLL/Xem.

## Tra cứu CCCD khách hàng

`FindIdentityMatchesAsync` thuộc nhóm đọc khách hàng trong BackendAccess, cho phép
cùng các chức năng đang dùng lookup khách (CAT_CUSTOMERS, SALES_RETAIL,
SALES_WHOLESALE, OLD_GOLD_PURCHASE, CRAFT_ORDER, DEBT_CUSTOMER, DEBT_SETTLEMENT,
INV_MATERIAL_RECEIPT, PROD_ORDER, REP_VIEW). Proxy kiểm tra session được truyền
khớp tài khoản/chi nhánh hiện tại; query lọc theo công ty/chi nhánh hoạt động.
Chọn khách đã có không ghi hồ sơ. Nút mở hồ sơ từ kết quả quét cần CAT_CUSTOMERS/Sửa;
lưu tạo/sửa vẫn qua policy SaveAsync hiện có và guard backend. Không bổ sung quyền mới.
