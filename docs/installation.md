# Bộ cài Windows x64 và dữ liệu mới tự động

## Cài trên máy mới

1. Chạy KazukoERP-Setup-1.0.3-win-x64.exe.
2. Mở KAZUKO ERP từ shortcut hoặc file exe.
3. Lần đầu app tự tạo DB mới, danh mục mặc định và kết nối. Không có màn hình
   chọn server, khôi phục hoặc nhập cấu hình Firebird.
4. Đăng nhập bằng admin, để trống ô mật khẩu. Thông báo mặc định giữ đến khi
   admin đăng nhập thành công.
5. Khai báo thông tin cửa hàng, giá và dữ liệu nghiệp vụ theo nhu cầu.

Dữ liệu mới có cửa hàng/chi nhánh chính, ba kho, quỹ tiền mặt, nhóm hàng/khách/NCC,
danh mục loại vàng, ca làm việc, khách lẻ và quyền quản trị. Không có giao dịch,
tồn kho, công nợ, nhân viên, khách hàng thực tế hoặc tài khoản từ máy phát triển.
Giá và số dư không được lấy từ máy build.

Máy đích cần Windows 10 từ 1903 trở lên hoặc Windows 11 x64. Không cần cài
.NET SDK, .NET Runtime hoặc Firebird riêng. Setup chứa
.NET/WindowsDesktop self-contained và toàn bộ Firebird Embedded x64. Mặc định
WebView2 được tải khi cài nếu máy chưa có; phần dưới hướng dẫn đóng gói offline.
Không cài Firebird Server/service. Mỗi máy dùng DB cục bộ riêng, không tự đồng bộ.

## Nơi lưu và bảo vệ dữ liệu

- Chương trình: %LOCALAPPDATA%\Programs\Kazuko ERP.
- DB mặc định: %LOCALAPPDATA%\KazukoERPJewelry\data\DATA.FDB.
- Profile không chứa mật khẩu: %LOCALAPPDATA%\KazukoERPJewelry\connection.json.
- Khóa mở DB và thông báo mật khẩu admin: file .key bảo vệ bằng Windows DPAPI
  theo tài khoản hiện tại, không ghi vào biến môi trường người dùng.

Lần mở sau dùng lại DB, không tạo lại hoặc reset dữ liệu. Hai lần mở đồng thời
được khóa theo thư mục profile. DB chỉ được công bố sau khi tạo schema/seed
thành công; lỗi giữa chừng không ghi đè DB có sẵn. Profile hoặc DB cũ được giữ,
kể cả khi DB cũ mất kết nối; app không âm thầm thay bằng DB trống.

Cài nâng cấp bằng cùng tài khoản/thư mục. Lưu các màn hình đang nhập, đóng ERP
và dừng host TV trước khi nâng cấp/gỡ cài. Bộ cài không ép kill app. DB, profile,
khóa và nháp ngoài thư mục chương trình không bị gỡ. Không đặt DB trong thư mục
chương trình; không dùng chung file Embedded qua SMB/OneDrive.

Khóa DPAPI chỉ mở được dưới tài khoản Windows đã tạo. Tạo sao lưu từ app để bảo
toàn dữ liệu; sao chép profile/.key sang tài khoản Windows khác không đủ để chuyển.

## Tạo setup

Máy build cần .NET SDK 8 và bundle đầy đủ tại
firebird/Firebird-5.0.4.1812-0-windows-x64.

Chạy tại thư mục repository:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Setup.ps1 -Version 1.0.3 -BootstrapCompiler

- -BootstrapCompiler dùng Inno Setup 6.7.4 từ release chính thức, xác minh chữ ký
  và SHA256 rồi cài cho tài khoản hiện tại vào artifacts/setup/tools/InnoSetup.
  Nếu đã có compiler thì bỏ cờ này hoặc dùng -IsccPath "C:\...\ISCC.exe".
- Runtime .NET 8 mặc định lấy bản vá mới nhất từ metadata Microsoft; dùng
  -RuntimeVersion 8.0.33 để ghim bản vá.
- Output: artifacts/setup/output/KazukoERP-Setup-1.0.3-win-x64.exe và .sha256.
- Mỗi lần publish có staging mới và payload-manifest.json riêng.
- Không đóng gói DATA.FDB của cửa hàng, bản sao lưu, profile hoặc mật khẩu. Schema
  và seed sạch nằm trong assembly; app tạo DB thật trên máy đích. SECURITY5.FDB
  và file mẫu vendor vẫn thuộc bundle Firebird.
- Schema khởi tạo hiện v93. Migrations 1–29 thiếu trong source cũ nên có baseline
  schema riêng, không chạy replay mù thư mục migrations. Chi tiết:
  [bootstrap/README.md](../database/bootstrap/README.md).
- Bản setup 1.0.0/1.0.1 cũ vẫn yêu cầu cấu hình DB; cần build lại bản mới để có luồng tự tạo.
- Bản 1.0.3 đổi mật khẩu admin mặc định thành trống. Với DB tự tạo bởi 1.0.2,
  chỉ đổi mật khẩu sinh ban đầu còn nguyên và chưa từng đăng nhập. Mật khẩu của
  tài khoản đã dùng hoặc tự đổi được giữ nguyên.

## Cài hoàn toàn offline

Tải Microsoft WebView2 Evergreen Standalone Installer x64 chính thức, sau đó:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Setup.ps1 -Version 1.0.3 -WebView2Installer "D:\Downloads\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"

Script xác minh chữ ký Microsoft. Bản mặc định dùng Bootstrapper nên cần mạng
trên máy cài nếu thiếu WebView2. Máy build cần mạng để restore NuGet/tải runtime.

## Kiểm tra

    dotnet run --project tests/Kazuko.LocalDatabase.IntegrationChecks
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Verify-Setup.ps1 -SetupPath ".\artifacts\setup\output\KazukoERP-Setup-1.0.3-win-x64.exe"

Test local DB dùng test89 riêng: không có dữ liệu nghiệp vụ cũ, admin đăng nhập
được, khóa DPAPI đọc lại được, mở đồng thời không tạo trùng, mở lại giữ thay đổi,
không thay DB/profile có sẵn. Test setup cài vào artifacts, kiểm tra runtime,
chạy nhánh host TV không gửi lệnh DB rồi gỡ; từ chối chạy nếu đã cài KAZUKO.
Không tự mở DB thật để kiểm thử.

App có manifest UTF-8 theo process, không đổi locale/code page của Windows.
Schema được thực thi qua parser của provider trong process, không truyền đường
dẫn tiếng Việt vào argv ANSI của isql. Khôi phục kiểm tra backup dùng service API
cục bộ trong process. Đã kiểm tra tạo/đăng nhập/mở lại và backup/restore trong
đường dẫn có dấu tiếng Việt/khoảng trắng; không cần bật UTF-8 hệ thống.

Setup chưa có chữ ký số của đơn vị phát hành. Có thể ký bằng chứng thư của đơn vị
trước khi phân phối và tạo lại SHA256. Bộ cài này không thay thế quy trình cập nhật
module có chữ ký/backup/rollback.

Nguồn: [Inno Setup](https://jrsoftware.org/isinfo.php),
[.NET self-contained](https://learn.microsoft.com/en-us/dotnet/core/deploying/),
[WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
## Chọn dữ liệu từ màn hình đăng nhập

Nút **Chọn DB** cho chọn file `.fdb` KAZUKO hiện có trên máy. Ứng dụng kiểm tra
kết nối, phiên bản schema và chi nhánh hoạt động, lưu lựa chọn theo tài khoản
Windows rồi tự mở lại màn hình đăng nhập. Đăng nhập bằng tài khoản của DB vừa
chọn; thao tác này không tạo lại tài khoản hoặc thay đổi mật khẩu.

Đường dẫn DB đang dùng hiện ngay trên màn hình; rê chuột để xem đường dẫn đầy đủ.
Hủy chọn hoặc file không hợp lệ giữ DB cũ. File khác phiên bản schema phải được
nâng cấp bằng quy trình bảo trì trước; chọn DB không tự chạy migration.
Khóa kết nối được bảo vệ bằng DPAPI, không ghi mật khẩu rõ trong `connection.json`.

