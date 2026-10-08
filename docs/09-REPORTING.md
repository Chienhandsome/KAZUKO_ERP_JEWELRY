# Báo cáo và in ấn

## Quyết định

Module Báo cáo dùng **FastReport Open Source** để dựng mẫu in A4 (file `.frx`).

- Dữ liệu vẫn lấy qua Application/Infrastructure service (không SQL trong View/ViewModel).
- Mẫu báo cáo A4 nằm ở `Templates/*.frx` (copy ra output). Có thể chỉnh kéo-thả bằng **FastReport Designer Community** (miễn phí).
- Runtime: `FastReport.OpenSource` + `FastReport.OpenSource.Export.PdfSimple`.
- Xem trước trong app: xuất PDF và hiển thị bằng WebView2 (thay DataGrid).
- Mở PDF ngoài / In: xuất PDF tạm rồi mở shell / verb `print`.
- Open Source không có `Report.Print()` hay WPF PreviewControl thương mại.
- Không dùng bản Demo FastReport.WPF có watermark trong production.

## Bill thanh toán (A5 ngang / dọc) — mẫu trong database

Bill sau thanh toán bán hàng dùng mẫu FastReport A5 lưu trong Firebird:

| Mục | Chi tiết |
|---|---|
| Bảng | `PRINT_TEMPLATES` (migration 57) |
| Mã mẫu | `SALES_RECEIPT_A5` (ngang), `SALES_RECEIPT_A5_PORTRAIT` (dọc) |
| Seed mặc định | Embedded `phoi_a5_ngang.frx` / `phoi_a5_doc.frx` |
| Contract | `IPrintTemplateStore` / `FirebirdPrintTemplateStore` |
| Runtime in | `SalesReceiptPrintService` — chọn orientation → load BLOB → bind → PDF |
| Xem trước | WebView2 PDF; ComboBox **A5 ngang / A5 dọc** (lưu `%LocalAppData%\KazukoERP\sales.receipt.orientation.json`) |
| Chỉnh sửa | Cài đặt → **Mẫu in bill** (chọn khổ) → Xuất/Import `.frx` |

**Máy in (vd. HP M1212nf):** orientation Portrait/Landscape do **driver** xử lý — không cần xoay giấy tay trong khay. Trong hộp thoại in chọn đúng A5 + Portrait (mẫu dọc) hoặc Landscape (mẫu ngang), hoặc Fit to page.

### Bind dữ liệu bill

- Parameters: `Tên khách`, `Số phiếu`, `Ngày`, `Tiền vàng mới`, `Tiền vàng cũ`, `Tiền bớt`, `Tổng cộng`, `Tổng cộng bằng chữ`, `Cộng tiền`, …
- DataSource `DataSet`: dòng bán (cột `LOAIVANG` = tuổi vàng thành phẩm).
- DataSource `OldGold`: **bảng thành phần** (Subreport trái dưới — `Loại`, `TL vàng`, `TL hột`, `Đ.giá`, `Thành tiền`). Ưu tiên dòng vàng cũ; nếu không có thì lấy thành phần vàng từ dòng bán.
- Lề mẫu ≥ 10mm; nút In dùng hộp thoại WebView2 (chọn A5 ngang / Fit) — tránh `Verb=print` cắt lề.
- Runtime Open Source: clear `Filter` thương mại, tách 2 bảng, `PrintOnParent` cho Subreport.

### Không làm (giữ free)

- Không mua FastReport WPF commercial / Online Designer.
- Không nhúng Designer trong app.
- Không dùng FlowDocument cho bill nghiệp vụ mới.

## Tái sử dụng (Shared — module Báo cáo)

Mỗi báo cáo A4 mới chỉ viết phần riêng; phần khung dùng chung trong `Kazuko.Modules.Reports/Shared/`:

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

## Designer (Community, free)

1. Tải FastReport Designer Community từ [GitHub FastReport releases](https://github.com/FastReports/FastReport/releases).
2. **Bill:** Cài đặt → Xuất `.frx` → mở Designer → lưu → Import lại vào DB.
3. **Báo cáo A4:** mở file trong `Templates/` → kéo thả → build lại app (copy ra output).

Nếu thiếu `.frx` báo cáo A4 lúc chạy, app tự tạo mẫu mặc định và ghi ra thư mục templates (lần đầu).

## Báo cáo đã có

| Key | Mẫu |
|-----|-----|
| `reports.warehouse.jewelry_on_hand` | `jewelry_on_hand_v3.frx` |
| `reports.warehouse.jewelry_on_hand_summary` | `jewelry_on_hand_summary_v1.frx` |
| Bill bán hàng (Sales) | `SALES_RECEIPT_A5` trong `PRINT_TEMPLATES` |
