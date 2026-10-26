# UI, theme và khả năng tiếp cận

## Resource bắt buộc

Mọi View dùng các semantic resource từ `Kazuko.Shared.UI`:

- Brush: `PrimaryBrush`, `AppBackgroundBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `BorderBrush`, `SuccessBrush`, `DangerBrush`, `InfoBrush`.
- Typography: `FontSize.Caption`, `FontSize.Body`, `FontSize.Subtitle`, `FontSize.Heading`, `FontSize.Display`.
- Style: `PrimaryButtonStyle`, `NavButtonStyle`, `CardBorderStyle` và các style dùng chung được bổ sung sau này.

Ví dụ đúng:

```xml
<TextBlock FontSize="{DynamicResource FontSize.Heading}"
           Foreground="{DynamicResource TextPrimaryBrush}" />
```

Không dùng mã màu trực tiếp hoặc `FontSize="14"` trong View nghiệp vụ.

## Layout

- Ưu tiên `Grid`, `DockPanel`, `WrapPanel`, `ItemsControl` và `ScrollViewer`.
- Không đặt vị trí tuyệt đối bằng Canvas cho form nghiệp vụ.
- Khai báo `MinWidth` hợp lý; khi nội dung không đủ chỗ, cho cuộn thay vì thu nhỏ chữ.
- Chiều cao nút tùy ngữ cảnh: ưu tiên tối thiểu 40 px cho thao tác chính; nút trong bảng, toolbar hoặc pane gọn có thể dùng 28–36 px. Phải đọc được, có focus và không bị cắt ở các mức font/DPI được hỗ trợ; không ép mọi nút cùng một chiều cao.
- Text quan trọng phải wrap hoặc trim kèm tooltip; không để chồng lấn.
- Ô ngày dùng `RoundedDatePickerStyle` trong `Shared.UI/Themes/DatePicker.xaml`;
  style `DatePicker` mặc định kế thừa mẫu này. Style riêng phải `BasedOn` cùng mẫu,
  giữ các binding và trạng thái khóa form. Định dạng ngày ngắn theo `vi-VN`,
  chiều cao tối thiểu 40 px; không dùng template ngày mặc định của Windows.
- Kiểm tra bằng keyboard: tab order, default button, focus visible và access key khi phù hợp.
- Ô nhập một dòng (`TextBox`, `PasswordBox`) bôi đen toàn bộ nội dung khi nhận focus bằng click hoặc Tab. Ô chỉ đọc và ô nhiều dòng (`AcceptsReturn`) không bôi đen. Hành vi gắn ở `TextBoxSelectAllBehavior.Register` lúc khởi động, vì style có key riêng không kế thừa setter của style `TextBox` mặc định.
- Ô tiền và số lượng ngắt hàng nghìn theo vi-VN (`1.000.000`, số lượng lẻ `1.250,5`) bằng `NumericThousandSeparatorBehavior`. Sau khi sửa chữ, behavior phải đẩy binding lên source (kể cả lúc rời ô): gán `Text` trong `TextChanged` nằm trong lượt cập nhật của WPF nên nếu không đẩy lại thì ô hiện số nhưng thuộc tính vẫn bằng 0. Ô binding vào `decimal` dùng `ViMoneyConverter` hoặc `ViQuantityConverter`, không dùng `StringFormat=N0` trên ô đang gõ — binding WPF mặc định đọc dấu chấm theo en-US.
- Riêng các ô trọng lượng tổng/đá/vàng và hao hụt của nhập kho thành phẩm dùng `WeightDecimalInput`: cho phép một dấu `.` hoặc `,` làm dấu thập phân, không có dấu phân nhóm hàng nghìn. Đọc độc lập CurrentCulture/thiết lập Windows, hiển thị kết quả theo vi-VN và giữ tối đa 4 số lẻ. Chuỗi có nhiều dấu hoặc cả hai loại dấu bị từ chối, không suy đoán thành số khác. Quy tắc này không thay định dạng tiền/số lượng ở các ô khác.

## Application shell và trang chủ

- Shell chuẩn có title bar màu chủ đạo, sidebar 330 px khi mở và 88 px khi thu gọn, content host và status bar.
- Nút mở/thu sidebar nằm giữa theo chiều dọc, bám vào ranh giới bên phải sidebar để luôn truy cập được ở cả hai trạng thái.
- Sidebar lấy dữ liệu từ `IModuleNavigationRegistry`; không hard-code danh sách chức năng trong `MainWindow.xaml`.
- Trang chủ lấy các tile có `ShowOnHome` từ cùng registry. Icon dùng pack URI tới resource do module sở hữu.
- Logo/background thương hiệu là hình minh họa cố định; text, card, border và trạng thái tương tác vẫn dùng semantic `DynamicResource`.
- Khi không đủ chiều cao hoặc font scale lớn, nội dung trang chủ cuộn theo chiều dọc; không giảm cỡ chữ để ép vừa.
- Góc trên phải trang chủ có Thiết lập shortcut: chọn tối đa 10 chức năng từ registry
  (`ShowOnHome` hoặc `ShowInSidebar`, trừ trang chủ), chỉ gồm chức năng được phép truy cập.
  Mặc định lấy tối đa 10 mục `ShowOnHome` theo thứ tự registry. Cho phép ẩn toàn bộ;
  lựa chọn tự lưu riêng theo UserId trên máy trong `%LocalAppData%\KazukoERP\home-shortcuts\`.
  Mất quyền thì ẩn shortcut ngay, không xóa lựa chọn đã lưu; khi được cấp lại quyền
  vẫn giữ giới hạn 10 mục hiển thị. Tùy chọn trang chủ không thay đổi sidebar hay phân quyền.

## DataGrid (convention module-local + shared column layout)

Style DataGrid (brush/typography) vẫn **local trong module** (`CustomerDataGridStyle`, `WarehouseDataGridStyle`, …) dùng `DynamicResource`:

- `IsReadOnly=True`, không auto-generate cột, hàng xen kẽ `SurfaceBrush` / `SurfaceAltBrush`.
- Cột thao tác dùng `DataGridTemplateColumn` + command trên ViewModel (không SQL trong View).
- Binding tới property read-only phải ghi `Mode=OneWay` khi dependency property mặc định TwoWay.

Quick menu cột (chuột phải header) và lưu layout là **tái sử dụng** qua `Kazuko.Shared.UI.DataGridLayout`:

- Gắn `grid:DataGridColumnLayout.SettingsKey` (file JSON dưới `%LocalAppData%\KazukoERP\`), `EnableHeaderMenu=True`, tùy chọn `AutoFitOnFirstLoad=True`.
- Mỗi cột gắn `grid:DataGridColumnLayout.Key` ổn định (không dùng header tiếng Việt làm khóa).
- Menu: **Cột hiển thị** (ẩn/hiện + đổi thứ tự) và **Dãn cột**; visibility/order/độ rộng Absolute lưu trên máy, không ghi Firebird.

Dialog tạo/sửa nghiệp vụ: Window riêng, owner-centered, `ScrollViewer`, Enter lưu / Escape hủy, không hard-code màu/font size.

## Kiểm thử bắt buộc cho View mới

- Light và Dark.
- Font scale 90%, 100%, 125%, 150%.
- DPI Windows 100%, 150%, 200% nếu màn hình có layout phức tạp.
- Cửa sổ ở kích thước tối thiểu và maximized.
- Nội dung tiếng Việt dài, dữ liệu rỗng, lỗi validation và trạng thái loading.
