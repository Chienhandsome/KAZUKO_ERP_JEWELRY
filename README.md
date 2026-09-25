# KAZUKO ERP JEWELRY

Nền tảng ERP xưởng kim hoàn viết bằng WPF trên .NET 8. Kiến trúc ưu tiên khả năng đổi theme/cỡ chữ, mở rộng bằng module và phát hành thay đổi nghiệp vụ qua một gói cập nhật duy nhất.

## Chạy ứng dụng

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln
dotnet run --project src/Kazuko.App/Kazuko.App.csproj
```

Yêu cầu trước khi chạy:

1. Firebird Embedded runtime tại `firebird\Firebird-5.0.4.1812-0-windows-x64\` (được copy vào output khi build; không commit vào Git).
2. Database development tại `data\data.fdb` (gitignored). Sao chép một lần từ bản WinForms tham chiếu `...\data\DATA.FDB` nếu chưa có.

Đăng nhập dùng tài khoản trong Firebird (bảng `USERS`), kèm chọn chi nhánh. Không còn tài khoản demo cứng trong composition root.

Tuỳ chọn cấu hình qua biến môi trường: `KAZUKO_FIREBIRD_HOME`, `KAZUKO_FIREBIRD_DATABASE`, `KAZUKO_FIREBIRD_USER`, `KAZUKO_FIREBIRD_PASSWORD`, `KAZUKO_FIREBIRD_EMBEDDED`, `KAZUKO_FIREBIRD_HOST`, `KAZUKO_FIREBIRD_PORT`.

## Tài liệu

Đọc [yêu cầu cốt lõi](docs/00-PROJECT-REQUIREMENTS.md) trước khi phát triển. Mọi coding agent cũng phải tuân theo [AGENTS.md](AGENTS.md).

## Trạng thái hiện tại

- WPF .NET 8 và cấu trúc nhiều project.
- MVVM primitives và constructor injection qua service registry.
- Theme sáng/tối bằng `DynamicResource`.
- Cỡ chữ 90%, 100%, 110%, 125%, 150%, lưu vào LocalAppData.
- Login → AppShell → Dashboard chạy theo một luồng hoàn chỉnh với xác thực Firebird Embedded.
- Menu do module đăng ký.
- Trình kiểm tra/staging gói `.kazuko-module` với manifest và SHA-256.
- Chữ ký số, activation/rollback module và các module nghiệp vụ CRUD nằm trong roadmap tiếp theo.
