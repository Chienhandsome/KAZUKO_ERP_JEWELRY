# KAZUKO ERP JEWELRY

Nền tảng ERP xưởng kim hoàn viết bằng WPF trên .NET 8. Kiến trúc ưu tiên khả năng đổi theme/cỡ chữ, mở rộng bằng module và phát hành thay đổi nghiệp vụ qua một gói cập nhật duy nhất.

## Chạy ứng dụng

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln
dotnet run --project src/Kazuko.App/Kazuko.App.csproj
```

Tài khoản nền tảng hiện tại: `admin`, mật khẩu để trống. Đây là authentication tạm thời; kết nối Firebird sẽ thay thế nó ở milestone tiếp theo.

## Tài liệu

Đọc [yêu cầu cốt lõi](docs/00-PROJECT-REQUIREMENTS.md) trước khi phát triển. Mọi coding agent cũng phải tuân theo [AGENTS.md](AGENTS.md).

## Trạng thái hiện tại

- WPF .NET 8 và cấu trúc nhiều project.
- MVVM primitives và constructor injection qua service registry.
- Theme sáng/tối bằng `DynamicResource`.
- Cỡ chữ 90%, 100%, 110%, 125%, 150%, lưu vào LocalAppData.
- Login → AppShell → Dashboard chạy theo một luồng hoàn chỉnh.
- Menu do module đăng ký.
- Trình kiểm tra/staging gói `.kazuko-module` với manifest và SHA-256.
- Chữ ký số, activation/rollback module, Firebird và phân quyền thật nằm trong roadmap tiếp theo.
