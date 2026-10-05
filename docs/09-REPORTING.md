# Báo cáo và in ấn

## Quyết định

Module Báo cáo dùng **FastReport Open Source** để dựng mẫu in A4 (file `.frx`).

- Dữ liệu vẫn lấy qua Application/Infrastructure service (không SQL trong View/ViewModel).
- Mẫu in nằm ở `Templates/*.frx` (copy ra output). Có thể chỉnh kéo-thả bằng **FastReport Designer**.
- Runtime: `FastReport.OpenSource` + `FastReport.OpenSource.Export.PdfSimple`.
- Xem trước trong app: xuất PDF và hiển thị bằng WebView2 (thay DataGrid).
- Mở PDF ngoài / In: xuất PDF tạm rồi mở shell / verb `print`.
- Open Source không có `Report.Print()` hay WPF PreviewControl thương mại.
## Không dùng

- Không hard-code layout phức tạp bằng `FlowDocument` cho báo cáo nghiệp vụ mới.
- Không dùng bản Demo FastReport.WPF có watermark trong production.
## Designer

1. Cài FastReport Designer (Community/Commercial tùy license).
2. Mở file `.frx` trong `Templates/`.
3. Kéo thả TextObject / Table / cột DataBand; lưu file.
4. Build lại app — mẫu mới được copy ra thư mục chạy.

Nếu thiếu `.frx` lúc chạy, app tự tạo mẫu mặc định và ghi ra thư mục templates (lần đầu).

## Báo cáo đã có

| Key | Mẫu |
|-----|-----|
| `reports.warehouse.jewelry_on_hand` | `jewelry_on_hand_v2.frx` |
