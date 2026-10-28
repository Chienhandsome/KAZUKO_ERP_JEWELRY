# Hướng dẫn bắt buộc cho mọi coding agent

Trước khi thay đổi code, đọc theo thứ tự:

1. `docs/00-PROJECT-REQUIREMENTS.md`
2. `docs/01-ARCHITECTURE.md`
3. `docs/02-UI-AND-ACCESSIBILITY.md`
4. `docs/03-MODULE-UPDATE-SYSTEM.md`
5. `docs/04-DEVELOPMENT-WORKFLOW.md`
6. `docs/09-REPORTING.md` khi làm báo cáo / in A4
7. `docs/10-CRAFT-CUSTOM-ORDER.md` khi làm đơn đặt chế tác / cọc / gắn lệnh SX

Các quyết định trong `docs/` là chuẩn của dự án. Không đưa SQL vào View/ViewModel, không hard-code màu hoặc cỡ chữ trong màn hình nghiệp vụ, không chạy C#/XAML tùy ý lấy từ database và không tạo dependency ngược từ Core tới WPF/Infrastructure.

Nếu yêu cầu mới mâu thuẫn với tài liệu, cập nhật tài liệu quyết định trước hoặc cùng commit với code. Mọi module mới phải vượt qua checklist trong `docs/04-DEVELOPMENT-WORKFLOW.md`.

## App đang chạy/debug chặn công việc

Theo yêu cầu của người dùng: khi tiến trình ERP đang chạy hoặc debug giữ file/database khiến build, kiểm thử hoặc bước triển khai cần thiết bị chặn, tự dừng đúng tiến trình ERP của workspace này rồi thực hiện lại bước bị chặn; không cần hỏi xác nhận lại. Kiểm tra đường dẫn executable và tiến trình giữ tài nguyên trước khi dừng. Không tắt hàng loạt `dotnet`, Visual Studio hoặc app/dịch vụ không liên quan. Thông báo ngắn cho người dùng khi đã phải dừng app; lưu ý việc dừng có thể làm mất dữ liệu đang nhập chưa lưu. Không tự khởi động lại app trừ khi cần kiểm thử hoặc người dùng yêu cầu.
