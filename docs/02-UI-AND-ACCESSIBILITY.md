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

## Nút, ô nhập và ô chọn

- Dùng style chung trong Kazuko.Shared.UI. Nút trong form và hộp thoại dùng chiều cao tối thiểu 44 DIP. Form và hộp thoại dùng bo góc 10 DIP và lề ngang trong control 12 DIP. Các ô một dòng (ô nhập, mật khẩu, ô chọn và ô ngày) dùng tài nguyên SingleLineFieldMinHeight mặc định 32 DIP. Riêng màn đăng nhập đặt mức tối thiểu 44 DIP. Ô nhiều dòng giữ chiều cao theo nội dung. Các nút cùng hàng có cùng chiều cao và cách nhau 8 DIP. Cho phép control cao thêm theo cỡ chữ.
- Bảng dữ liệu và thanh công cụ dùng phiên bản gọn với chiều cao tối thiểu 32 DIP và bo góc 6 DIP. Màu, chữ và trạng thái tương tác dùng cùng quy tắc với form.
- Mỗi nhóm thao tác có một nút chính dùng nền PrimaryBrush và chữ OnPrimaryBrush. Nút phụ dùng nền theo theme, viền mảnh và chữ thông thường. Nút nguy hiểm dùng màu cảnh báo và nhãn nêu rõ thao tác, ví dụ **Xóa**.
- Ô nhập có nhãn phía trên, canh trái. Ô chỉ đọc có nền dịu hơn và vẫn cho sao chép khi cần. Khi kiểm tra dữ liệu thất bại, dùng viền cảnh báo và thông báo ngay dưới ô liên quan.
- ComboBox dùng cùng kích thước, viền, bo góc và cỡ chữ với ô nhập. Mũi tên ở mép phải. Mục đang chọn có dấu hiệu riêng với mục đang được trỏ tới hoặc được điều hướng tới bằng bàn phím.
- Nút có trạng thái rê chuột, đang nhấn, focus bằng bàn phím và vô hiệu hóa. Khi đang xử lý, giữ kích thước nút. Kiểm tra theme sáng/tối và phím Tab.
- Các style form hiện dùng ở màn đăng nhập nằm trong Kazuko.Shared.UI/Themes/FormControls.xaml. Dùng FormPrimaryButtonStyle cho **Đăng nhập** và **Tạo mới**, FormSecondaryButtonStyle cho **Chọn DB**. Ô tên file dùng FormTextBoxStyle ở chế độ chỉ đọc.

## Layout

- Ưu tiên `Grid`, `DockPanel`, `WrapPanel`, `ItemsControl` và `ScrollViewer`.
- Không đặt vị trí tuyệt đối bằng Canvas cho form nghiệp vụ.
- Khai báo `MinWidth` hợp lý; khi nội dung không đủ chỗ, cho cuộn thay vì thu nhỏ chữ.
- Text quan trọng phải wrap hoặc trim kèm tooltip; không để chồng lấn.
- Ô ngày dùng `RoundedDatePickerStyle` trong `Shared.UI/Themes/DatePicker.xaml`;
  style `DatePicker` mặc định kế thừa mẫu này. Style riêng phải `BasedOn` cùng mẫu,
  giữ các binding và trạng thái khóa form. Định dạng ngày ngắn theo `vi-VN`,
  chiều cao tối thiểu 32 DIP; không dùng template ngày mặc định của Windows.
- Kiểm tra bằng keyboard: tab order, default button, focus visible và access key khi phù hợp.
- Ô nhập một dòng (`TextBox`, `PasswordBox`) bôi đen toàn bộ nội dung khi nhận focus bằng click hoặc Tab. Ô chỉ đọc và ô nhiều dòng (`AcceptsReturn`) không bôi đen. Hành vi gắn ở `TextBoxSelectAllBehavior.Register` lúc khởi động, vì style có key riêng không kế thừa setter của style `TextBox` mặc định.
- Ô tiền và số lượng ngắt hàng nghìn theo vi-VN (`1.000.000`, số lượng lẻ `1.250,5`) bằng `NumericThousandSeparatorBehavior`. Sau khi sửa chữ, behavior phải đẩy binding lên source (kể cả lúc rời ô): gán `Text` trong `TextChanged` nằm trong lượt cập nhật của WPF nên nếu không đẩy lại thì ô hiện số nhưng thuộc tính vẫn bằng 0. Ô binding vào `decimal` dùng `ViMoneyConverter` hoặc `ViQuantityConverter`, không dùng `StringFormat=N0` trên ô đang gõ — binding WPF mặc định đọc dấu chấm theo en-US.
- Riêng các ô trọng lượng tổng/đá/vàng và hao hụt của nhập kho thành phẩm dùng `WeightDecimalInput`: cho phép một dấu `.` hoặc `,` làm dấu thập phân, không có dấu phân nhóm hàng nghìn. Đọc độc lập CurrentCulture/thiết lập Windows, hiển thị kết quả theo vi-VN và giữ tối đa 4 số lẻ. Chuỗi có nhiều dấu hoặc cả hai loại dấu bị từ chối, không suy đoán thành số khác. Quy tắc này không thay định dạng tiền/số lượng ở các ô khác.
- Rà soát 28/10/2026: mở rộng quy tắc số thập phân thuần sang TL ước tính LSX, số lượng giao NVL (kể cả sửa dòng), nhập/xuất NVL, số chỉ trả nợ thợ và các ô mm/cỡ chữ/góc của designer tem/bill. Binding số dùng `PlainDecimalConverter`, không gắn formatter ngăn nghìn lên các ô này. Ô số lượng bắt buộc dương dùng `ConverterParameter=PositiveQuantity`: đầu vào hỏng đưa source về 0 để validation chặn thao tác, tránh ghi lại giá trị hợp lệ cũ. Ô designer báo lỗi binding khi đầu vào hỏng.
- Kiểm kê vẫn hỗ trợ số lượng phân nhóm Việt Nam (`1.250,5`), dùng `QuantityDecimalInput` để kiểm tra nhóm đúng cấu trúc; `0.5`/`0.023` là số thập phân, không được đọc thành 5/23. Riêng `1.250` tại ô số lượng có phân nhóm vẫn là 1250, còn ở ô trọng lượng thuần là 1,25. Không fallback bằng cách bỏ mọi dấu/ký tự. Tiền VND giữ formatter riêng. Danh sách kiểm tra và test ở `docs/manual-tests/decimal-input-review.md`.

## Application shell và trang chủ

- Màn hình đăng nhập có lớp bong bóng viền mờ nổi từ dưới lên trong khung thương hiệu,
  nằm sau nội dung và không nhận chuột/focus. Màu dùng `OnPrimaryBrush`; lớp trang trí
  được cắt theo góc bo. Animation chỉ chạy khi view đang hiện, gỡ clock khi ẩn/đóng;
  bỏ chuyển động khi Windows tắt client-area animation hoặc bật High Contrast.
- Màn hình đăng nhập hiển thị tên file DB, đường dẫn đầy đủ khi rê chuột, và hai nút **Chọn DB** / **Tạo mới** dưới ô tên file.
  **Tạo mới** khởi tạo một file mới với dữ liệu mặc định rồi mở lại ứng dụng để dùng file đó.
  **Chọn DB** mở file `.fdb`
  hiện có trên máy, kiểm tra schema đúng phiên bản và chi nhánh hoạt động trước khi
  lưu profile rồi mở lại ứng dụng. Không tự nâng cấp, tạo mới hoặc xóa dữ liệu khi
  chọn file. Lỗi/hủy chọn giữ DB cũ; không cho chọn DB đồng thời với đăng nhập.

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

- Gắn `grid:DataGridColumnLayout.SettingsKey` ổn định, `EnableHeaderMenu=True`, tùy chọn `AutoFitOnFirstLoad=True`.
- Mỗi cột gắn `grid:DataGridColumnLayout.Key` ổn định (không dùng header tiếng Việt làm khóa).
- Menu: **Cột hiển thị** (ẩn/hiện + đổi thứ tự) và **Dãn cột**; visibility/order/độ rộng Absolute lưu Firebird theo công ty/tài khoản, có cache/outbox trên máy khi mất kết nối.

Rà soát 29/10/2026: cả 78 DataGrid khai báo khóa bảng/cột và menu tường minh;
layout tự lưu khi thay đổi, giữ độ rộng khi áp dụng dialog. DTO/contract ở Core,
Firebird repository/cache ở Infrastructure, App cấu hình facade dùng chung với
cột báo cáo sau đăng nhập. Migration 93 và checklist: [table-column-layout.md](table-column-layout.md).

Dialog tạo/sửa nghiệp vụ: Window riêng, owner-centered, `ScrollViewer`, Enter lưu / Escape hủy, không hard-code màu/font size.

Template ComboBox tùy chỉnh phải truyền cả `SelectionBoxItemTemplate` và
`ItemTemplateSelector` vào ContentPresenter để `DisplayMemberPath` hoạt động khi
đóng dropdown. Module kho đã bổ sung binding selector ngày 30/10/2026, sửa ô dòng
mẫu LSX hiển thị toàn bộ record thay vì tên mẫu. Đã kiểm tra tên mẫu/mã LSX ở ô
đã chọn và tên trong dropdown trên Light/Dark bằng DTO giả, không thay đổi DB.

Phiếu nhập kho thành phẩm: Hủy, Escape và nút X cùng kiểm tra điều kiện đóng.
Tên/mã mẫu và loại vàng tự điền chưa được coi là món đang sửa. Nếu có thay đổi,
cho xác nhận bỏ món đang nhập; từ chối hoặc hủy bước lưu nháp phải giữ nguyên dữ
liệu. Các món đã thêm vẫn được hỏi lưu nháp; không cho bỏ yêu cầu ghi sổ chưa rõ
kết quả. Nút Hủy dùng command và Escape binding, không dùng `IsCancel` để tránh
WPF tự thử đóng lần nữa sau khi người dùng từ chối xác nhận.

Thu gọn phiếu nhập thành phẩm (30/10/2026): bảng tiến độ mặc định thu lại; LSX
chỉ có một mẫu tự chọn mẫu đó khi tải phiếu và sau khi thêm/bỏ món đang nhập,
ẩn ô chọn mẫu. LSX nhiều mẫu vẫn chọn tường minh. Chỉ giữ nút Ghi sổ nhập kho
ở cuối màn hình. Bỏ món đang nhập chỉ hiện khi có thay đổi; Kiểm tra kết quả
chỉ hiện khi lần ghi sổ lỗi/chưa rõ kết quả; Mở đơn / Giao hàng chỉ hiện khi đã
ghi sổ và LSX có đơn liên quan. Các cơ chế đối chiếu, ghi sổ và lưu nháp giữ nguyên.

## Kiểm thử bắt buộc cho View mới

Kiểm kê (30/10/2026): danh sách phiếu dạng thẻ có trạng thái; khu thông tin,
bảng chi tiết và khu ghi nhận dòng tách riêng. Dùng style kho cho ComboBox/TextBox,
ghim thao tác lưu/đối chiếu/chốt ngoài phần cuộn. Quét serial và nút xác nhận
đã thấy/không thấy chỉ hiện cho phiếu serial; nhập hàng loạt số 0 chỉ hiện cho
NVL. Số lượng căn phải; kết quả đối chiếu có chữ và màu theo theme. Phiếu mới,
danh sách rỗng và tìm kiếm không có kết quả có hướng dẫn riêng. Giữ SettingsKey
và khóa các cột hiện có để tiếp tục dùng layout đã lưu.

Đã render UserControl thật với dịch vụ giả ở Light/Dark, font 90/100/125/150%,
viewport 1250×680 và 1700×800; gồm phiếu mới, NVL, serial, đã chốt, đang tải và lỗi.
22 kiểm thử kiểm kê hiện có đạt. Không ghi DB; chưa kiểm tra DPI Windows thật.

- Light và Dark.
- Font scale 90%, 100%, 125%, 150%.
- DPI Windows 100%, 150%, 200% nếu màn hình có layout phức tạp.
- Cửa sổ ở kích thước tối thiểu và maximized.
- Nội dung tiếng Việt dài, dữ liệu rỗng, lỗi validation và trạng thái loading.
