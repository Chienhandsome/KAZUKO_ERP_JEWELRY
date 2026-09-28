# UI, theme và khả năng tiếp cận

## Resource bắt buộc

Mọi View dùng các semantic resource từ `Kazuko.Shared.UI`:

- Brush: `PrimaryBrush`, `AppBackgroundBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `BorderBrush`, `SuccessBrush`, `DangerBrush`.
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
- Nút cần chiều cao tối thiểu 40 px tại scale 100%.
- Text quan trọng phải wrap hoặc trim kèm tooltip; không để chồng lấn.
- Kiểm tra bằng keyboard: tab order, default button, focus visible và access key khi phù hợp.

## Application shell và trang chủ

- Shell chuẩn có title bar màu chủ đạo, sidebar 330 px khi mở và 82 px khi thu gọn, content host và status bar.
- Nút mở/thu sidebar nằm giữa theo chiều dọc, bám vào ranh giới bên phải sidebar để luôn truy cập được ở cả hai trạng thái.
- Sidebar lấy dữ liệu từ `IModuleNavigationRegistry`; không hard-code danh sách chức năng trong `MainWindow.xaml`.
- Trang chủ lấy các tile có `ShowOnHome` từ cùng registry. Icon dùng pack URI tới resource do module sở hữu.
- Logo/background thương hiệu là hình minh họa cố định; text, card, border và trạng thái tương tác vẫn dùng semantic `DynamicResource`.
- Khi không đủ chiều cao hoặc font scale lớn, nội dung trang chủ cuộn theo chiều dọc; không giảm cỡ chữ để ép vừa.

## DataGrid (convention module-local + shared column layout)

Style DataGrid (brush/typography) vẫn **local trong module** (`CustomerDataGridStyle`, `WarehouseDataGridStyle`, …) dùng `DynamicResource`:

- `IsReadOnly=True`, không auto-generate cột, hàng xen kẽ `SurfaceBrush` / `SurfaceAltBrush`.
- Cột thao tác dùng `DataGridTemplateColumn` + command trên ViewModel (không SQL trong View).
- Binding tới property read-only phải ghi `Mode=OneWay` khi dependency property mặc định TwoWay.

Quick menu cột (chuột phải header) và lưu layout là **tái sử dụng** qua `Kazuko.Shared.UI.DataGridLayout`:

- Gắn `grid:DataGridColumnLayout.SettingsKey` (file JSON dưới `%LocalAppData%\KazukoERP\`), `EnableHeaderMenu=True`, tùy chọn `AutoFitOnFirstLoad=True`.
- Mỗi cột gắn `grid:DataGridColumnLayout.Key` ổn định (không dùng header tiếng Việt làm khóa).
- Menu: **Cột hiển thị** (ẩn/hiện + đổi thứ tự) và **Dãn cột**; visibility/order lưu trên máy, không ghi Firebird.

Dialog tạo/sửa nghiệp vụ: Window riêng, owner-centered, `ScrollViewer`, Enter lưu / Escape hủy, không hard-code màu/font size.

## Kiểm thử bắt buộc cho View mới

- Light và Dark.
- Font scale 90%, 100%, 125%, 150%.
- DPI Windows 100%, 150%, 200% nếu màn hình có layout phức tạp.
- Cửa sổ ở kích thước tối thiểu và maximized.
- Nội dung tiếng Việt dài, dữ liệu rỗng, lỗi validation và trạng thái loading.
