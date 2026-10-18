# Phân quyền theo nhóm

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

## Ánh xạ thao tác đặc biệt

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
