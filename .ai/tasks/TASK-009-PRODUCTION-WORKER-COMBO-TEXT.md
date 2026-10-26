# TASK-009 - Chọn thợ sản xuất bằng phần chữ trong tạo LSX

## Status

DONE — 26/10/2026. Đã xử lý sau lỗi đọc dấu thập phân nhập kho thành phẩm.

## Goal

Trong form tạo lệnh sản xuất, bấm tên thợ hoặc đúng checkbox phải cùng chọn/bỏ chọn người đó và hiển thị đúng tên ở ô Thợ SX.

## Reproduction

1. Mở tạo LSX và danh sách Thợ SX.
2. Bấm phần chữ tên nhân viên: ô đóng hiện `Kazuko.Modules.Production.SelectableEmployeeViewModel` (sai).
3. Bấm đúng checkbox: ô hiện tên như `Lê Thị Hương` (đúng).

## Allowed Files

- Form/ViewModel và resources chọn nhân viên thuộc src/Modules/Kazuko.Modules.Production.
- Tests module Production tương ứng, tài liệu liên quan.
- KAZUKO_ERP_JEWELRY.sln: đăng ký project kiểm thử Production.

## Acceptance Criteria

- Bấm chữ và bấm checkbox có cùng hành vi, không hiện tên kiểu .NET.
- Giữ chọn nhiều thợ, bỏ chọn, bàn phím và mở lại lệnh đã lưu.
- Không thay dữ liệu nhân viên, schema, danh sách người được chọn ngoài ý định người dùng.

## Notes

Nguyên nhân: ComboBox editable hiển thị SelectedItem khi người dùng chọn dòng, ghi đè chuỗi tổng hợp tên thợ bằng tên kiểu SelectableEmployeeViewModel. Header mới bind trực tiếp SelectedWorkerDisplayText, không dùng SelectionBoxItem hoặc textbox editable. Click toàn bộ dòng (chữ/checkbox/khoảng trống) bật/tắt IsSelected đúng một lần và giữ popup mở. Dòng ComboBox được chọn chỉ phục vụ highlight/bàn phím; Space/Enter bật/tắt thợ đang trỏ tới, Escape đóng danh sách.

Kiểm thử WorkerPickerTests dùng cửa sổ WPF thực: sáng/tối × font 90/100/125/150%, click chữ/checkbox, chọn nhiều/bỏ chọn, highlight không đổi tên tổng hợp, Space/Enter/Escape, khôi phục checkbox của thợ đã nghỉ được chọn và trạng thái không cho sửa. ViewModel nghiệp vụ, ID nhân viên, logic lưu lệnh và database không thay đổi.

Lệnh: `dotnet test tests/Kazuko.Modules.Production.Tests/Kazuko.Modules.Production.Tests.csproj`.
