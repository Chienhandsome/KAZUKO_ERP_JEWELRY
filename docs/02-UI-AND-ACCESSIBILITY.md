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

## Kiểm thử bắt buộc cho View mới

- Light và Dark.
- Font scale 90%, 100%, 125%, 150%.
- DPI Windows 100%, 150%, 200% nếu màn hình có layout phức tạp.
- Cửa sổ ở kích thước tối thiểu và maximized.
- Nội dung tiếng Việt dài, dữ liệu rỗng, lỗi validation và trạng thái loading.
