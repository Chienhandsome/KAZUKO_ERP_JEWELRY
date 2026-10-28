# Quy trình phát triển

## Trước khi code

- Đọc `AGENTS.md` và tài liệu liên quan.
- Xác định module sở hữu use case.
- Kiểm tra working tree để không ghi đè thay đổi của người khác.
- Nếu thay đổi kiến trúc hoặc contract công khai, cập nhật docs cùng code.
- Nếu ERP đang chạy/debug khóa file hoặc database và chặn công việc, được người dùng cho phép tự dừng đúng tiến trình ERP của workspace rồi thử lại. Xác minh đường dẫn/tiến trình giữ tài nguyên, không dừng hàng loạt host `dotnet`, IDE hoặc dịch vụ khác; báo ngắn sau khi dừng. Xem quy tắc chung trong `AGENTS.md`.

## Definition of Done

- Build solution không có warning/error.
- Logic mới có test unit khi có quy tắc tính toán, validation hoặc transaction.
- View mới vượt qua ma trận theme/font/DPI trong tài liệu UI.
- Không có secret hoặc connection string thật trong source.
- Không có SQL trong View/ViewModel.
- Migration mới có version, checksum và tài liệu rollback.
- Module mới đăng ký navigation/permission qua contract, không sửa shell cho từng module.
- Tính năng cập nhật không bỏ qua signature/backup/rollback trong production.

## Lệnh kiểm tra cơ bản

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln
dotnet test KAZUKO_ERP_JEWELRY.sln --no-build
```

Kiểm thử lỗi audit: `tests/Kazuko.Audit.IntegrationChecks` tạo trigger gây lỗi ghi nhật ký và xác nhận dữ liệu nghiệp vụ rollback, sau đó gỡ trigger. Chỉ chạy trên bản database khôi phục riêng có tên `test89_*.fdb` (truyền đường dẫn làm đối số); chương trình từ chối `DATA.FDB`. Không dùng database đang vận hành. Bộ kiểm thử Infrastructure cũ còn phụ thuộc fixture schema v51 riêng; thiếu fixture phải được ghi nhận là kiểm thử chưa qua, không coi là bằng chứng xác nhận nghiệp vụ.

## Roadmap gần nhất

1. Xác thực Firebird đã tích hợp tại composition root; không còn xem việc thay DevelopmentAuthenticationService là hạng mục chưa làm.
2. Audit nghiệp vụ bắt buộc cùng transaction, lỗi phải rollback. Mở rộng kiểm thử lỗi và logging vận hành khi bổ sung use case.
3. Hoàn thiện package signature/rollback và màn hình import update.
4. Thiết kế schema form metadata và renderer allow-list.
5. Module Kho đã có danh sách, ghi sổ, chuyển kho và các luồng liên quan; các thay đổi tiếp theo là mở rộng/hoàn thiện nghiệp vụ, không phải bắt đầu port.
6. Các test project đã có; duy trì và mở rộng coverage/gate CI theo thay đổi thực tế.
