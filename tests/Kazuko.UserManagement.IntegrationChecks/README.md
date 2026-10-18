# Kiểm tra hồi quy tạo/sửa tài khoản

Chương trình console kiểm tra repository và đăng nhập thực tế trên Firebird đã có
migration 77: chi nhánh phiên, các cột legacy bắt buộc, role thu ngân, hiển thị tài
khoản, giữ/đổi mật khẩu, và từ chối trùng tài khoản.

Chỉ chạy trên database **tạm được restore từ backup**, tên file bắt đầu bằng
`kazuko_user_verify_`. Chương trình có ghi dữ liệu kiểm thử vào bản sao này.
Không dùng database của ứng dụng đang chạy.

Đặt `KAZUKO_FIREBIRD_HOME` tới runtime Firebird bundled và
`KAZUKO_FIREBIRD_PASSWORD` trong môi trường, rồi chạy:

```powershell
dotnet run --project tests/Kazuko.UserManagement.IntegrationChecks -- <đường-dẫn-database-tạm>
```

Xóa bản sao sau kiểm tra; không ghi đè database nguồn.

Kiểm tra không dùng database:

```powershell
dotnet run --project tests/Kazuko.UserManagement.IntegrationChecks -- --unit
dotnet run --project tests/Kazuko.UserManagement.IntegrationChecks -- --ui
```

Ca database kiểm tra thêm quyền bị thu hồi, nhóm ngừng hoạt động, và Admin hệ thống.
