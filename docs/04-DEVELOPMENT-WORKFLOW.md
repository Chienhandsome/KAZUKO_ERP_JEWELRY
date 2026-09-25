# Quy trình phát triển

## Trước khi code

- Đọc `AGENTS.md` và tài liệu liên quan.
- Xác định module sở hữu use case.
- Kiểm tra working tree để không ghi đè thay đổi của người khác.
- Nếu thay đổi kiến trúc hoặc contract công khai, cập nhật docs cùng code.

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

## Roadmap gần nhất

1. Thay `DevelopmentAuthenticationService` bằng authentication Firebird từ codebase đã duyệt.
2. Bổ sung logging, exception boundary và audit context.
3. Hoàn thiện package signature/rollback và màn hình import update.
4. Thiết kế schema form metadata và renderer allow-list.
5. Port module Kho theo vertical slice: danh sách → chi tiết → ghi sổ → audit.
6. Thêm test projects và CI.
