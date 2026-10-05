# Hướng dẫn bắt buộc cho mọi coding agent

Trước khi thay đổi code, đọc theo thứ tự:

1. `docs/00-PROJECT-REQUIREMENTS.md`
2. `docs/01-ARCHITECTURE.md`
3. `docs/02-UI-AND-ACCESSIBILITY.md`
4. `docs/03-MODULE-UPDATE-SYSTEM.md`
5. `docs/04-DEVELOPMENT-WORKFLOW.md`
6. `docs/09-REPORTING.md` khi làm báo cáo / in A4

Các quyết định trong `docs/` là chuẩn của dự án. Không đưa SQL vào View/ViewModel, không hard-code màu hoặc cỡ chữ trong màn hình nghiệp vụ, không chạy C#/XAML tùy ý lấy từ database và không tạo dependency ngược từ Core tới WPF/Infrastructure.

Nếu yêu cầu mới mâu thuẫn với tài liệu, cập nhật tài liệu quyết định trước hoặc cùng commit với code. Mọi module mới phải vượt qua checklist trong `docs/04-DEVELOPMENT-WORKFLOW.md`.
