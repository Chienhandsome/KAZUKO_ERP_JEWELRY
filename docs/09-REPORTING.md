# Báo cáo và in ấn

## Quyết định

Module Báo cáo dùng **FastReport Open Source** để dựng mẫu in A4 (file `.frx`).

- Dữ liệu vẫn lấy qua Application/Infrastructure service (không SQL trong View/ViewModel).
- Mẫu in nằm ở `Templates/*.frx` (copy ra output). Có thể chỉnh kéo-thả bằng **FastReport Designer**.
- Runtime: `FastReport.OpenSource` + `FastReport.OpenSource.Export.PdfSimple`.
- Xem trước trong app: xuất PDF và hiển thị bằng WebView2 (thay DataGrid).
- Mở PDF ngoài / In: xuất PDF tạm rồi mở shell / verb `print`.
- Open Source không có `Report.Print()` hay WPF PreviewControl thương mại.

## Tái sử dụng (Shared)

Mỗi báo cáo mới chỉ viết phần riêng; phần khung dùng chung trong `Kazuko.Modules.Reports/Shared/`:

| Thành phần | Vai trò |
|---|---|
| `ReportWorkspaceViewModel` | Base: tải / debounce lọc / trạng thái / PDF preview / In / Mở PDF |
| `ReportWorkspaceView` | Layout: pane PDF trái + panel lọc phải (WebView2) |
| `FastReportPdfExporter` | Export PDF tạm, mở ngoài, in shell |
| `FastReportTemplateHelper` | Đường dẫn `.frx`, `Mm()`, text, bind DataBand, filter text |
| `ReportColumnLayout` / dialog | Ẩn-hiện + thứ tự cột, lưu `%LocalAppData%\KazukoERP` |
| `ReportSharedResources.xaml` | Style TextBox / ComboBox / nút bo góc |

Checklist báo cáo mới:

1. Contract + Infra query service (Core / Infrastructure).
2. `*PrintService` dựng bảng theo `ActiveColumns` (dynamic FastReport bands).
3. `*ReportViewModel : ReportWorkspaceViewModel` — filter + columns + `LoadDataAsync` + `CreatePreparedReport`.
4. Đăng ký key trong `ReportCatalog` / `ReportsViewModel`.
5. DataTemplate trong `ReportsView` + nút **Cột báo cáo**.

## Cột báo cáo

- Nút **Cột báo cáo** mở dialog giống DataGrid: tick ẩn/hiện, ▲▼ đổi thứ tự.
- Layout lưu theo máy (`reports.*.columns.json`), không ghi Firebird.
- Áp dụng xong dựng lại PDF preview theo cột đã chọn.

## Không dùng

- Không hard-code layout phức tạp bằng `FlowDocument` cho báo cáo nghiệp vụ mới.
- Không dùng bản Demo FastReport.WPF có watermark trong production.
- Không nhét logic SQL hoặc mọi bộ lọc vào một siêu ViewModel.

## Designer

1. Cài FastReport Designer (Community/Commercial tùy license).
2. Mở file `.frx` trong `Templates/`.
3. Kéo thả TextObject / Table / cột DataBand; lưu file.
4. Build lại app — mẫu mới được copy ra thư mục chạy.

Nếu thiếu `.frx` lúc chạy, app tự tạo mẫu mặc định và ghi ra thư mục templates (lần đầu).

## Báo cáo đã có

| Key | Mẫu |
|-----|-----|
| `reports.warehouse.jewelry_on_hand` | `jewelry_on_hand_v3.frx` |
| `reports.warehouse.jewelry_on_hand_summary` | `jewelry_on_hand_summary_v1.frx` |
