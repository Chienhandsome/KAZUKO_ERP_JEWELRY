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

**Máy in (vd. HP M1212nf):** nút **In** dùng `PrintDocument` thẳng máy mặc định (không hộp thoại), giống setting WPS: khổ **A5**, lề ~4mm, **fit + auto-center + auto-rotate**. Mẫu dọc: lề trái ≥ 14mm / phải ≥ 16mm; khối **Vàng mới / Vàng cũ** trong `ReportSummary`.

### Bind dữ liệu bill

- Parameters: `Tên khách`, `Số phiếu`, `Ngày`, `Tiền vàng mới`, `Tiền vàng cũ`, `Tiền bớt`, `Tổng cộng`, `Tổng cộng bằng chữ`, `Cộng tiền`, …
- DataSource `DataSet`: dòng bán (cột `LOAIVANG` = tuổi vàng thành phẩm).
- DataSource `OldGold`: **bảng thành phần** (Subreport trái dưới — `Loại`, `TL vàng`, `TL hột`, `Đ.giá`, `Thành tiền`). Ưu tiên dòng vàng cũ; nếu không có thì lấy thành phần vàng từ dòng bán.
- Lề mẫu ≥ 10mm; nút **In** = `PrintDocument` A5 Portrait + fit margins + auto-center + auto-rotate (giống WPS).
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
| Tem nữ trang 28x12 (Warehouse) | `TEM_NU_TRANG_28x12` trong `PRINT_TEMPLATES` |

## Tem nữ trang 28x12

Trong trình thiết kế, chọn đối tượng mã để đổi giữa **Barcode (Code 128)** và **QR**.
QR mặc định vuông 8 mm, giữ tỉ lệ vuông khi đổi kích thước; nếu vị trí cũ chồng nội dung,
ưu tiên tìm khoảng trống trong tem. Đổi lại barcode khôi phục vị trí/kích thước thanh
trước đó. Loại mã được lưu trong `.frx` và dùng cho canvas, xem trước,
in thử, in hàng thật. Nội dung quét của cả hai là **mã hàng (`ProductCode`)** qua
`DataSet.MAVACH`; không lấy mã barcode riêng nếu khác mã hàng. Dữ liệu xem trước/in thử
dùng mã mẫu `B600022`.

Trình sửa mẫu tem có **In thử**: chọn máy in và số bản, dùng dữ liệu mẫu với
các chỉnh sửa hiện tại (kể cả chưa lưu). In vector từ trang đã chuẩn bị của FastReport,
khổ 86 × 12 mm, lề 0. Hủy chọn máy in thì không gửi lệnh in.

In tem cho sản phẩm nhập kho thành phẩm (dialog Nhập kho TP):

| Mục | Chi tiết |
|---|---|
| Mã mẫu | `TEM_NU_TRANG_28x12` trong `PRINT_TEMPLATES` |
| Seed mặc định | Embedded `tem_nu_trang_28x12.frx` |
| Khổ giấy in | Ngang **86 mm**, cao **12 mm**, lề 0; áp dụng khi chuẩn bị FastReport và cho `PrintDocument`, bù hard margin của driver khi vẽ vector |
| Runtime in | `FinishedGoodsLabelPrintService` — load BLOB → bind DataTable `DataSet` → `Prepare()` → `VectorLabelRenderer` vẽ vector trực tiếp (text/mã vạch) lên `PrintDocument` ở đơn vị mm |
| Chọn tem | Checkbox từng dòng trong bảng SP ("In tem đang chọn") hoặc tất cả ("In tem tất cả") |
| Máy in | Dialog chọn máy in hệ thống + số bản; nhớ máy in lần trước (`%LocalAppData%\KazukoERP\jewelry.label.printer.json`) |
| Dữ liệu | Tự động từ `ManualFinishedProductItem` (mã vạch, tên, loại vàng, TL, công, NCC...); tên/địa chỉ tiệm từ `ICompanyProfileService` |
| Sửa mẫu | Tab **Mẫu in** trong Cài đặt → mục "Mẫu tem nữ trang 28x12" → nút **Sửa mẫu tem** mở designer kéo-thả |

### Designer sửa mẫu tem

Trình thiết kế trực quan cho mẫu tem nữ trang (tab **Mẫu in** trong Cài đặt):

| Mục | Chi tiết |
|---|---|
| Backing store | FastReport object model: `Report.Load()` từ DB → sửa `TextObject`/`BarcodeObject` → `Report.Save()` → `IPrintTemplateStore.SaveAsync()` |
| Canvas | Tỷ lệ 3x so với kích thước thật (86×12mm, 96dpi); lưới 1 mm (nét đậm mỗi 5 mm) chỉ trên mặt thiết kế; click chọn, kéo di chuyển, kéo 8 góc/cạnh để resize. Mặc định bám lưới 1 mm khi kéo; tắt bằng ô **Bám lưới 1 mm**. Ô X/Y/Rộng/Cao vẫn nhập số lẻ |
| Đối tượng | Chỉ text + barcode (theo phạm vi pipeline in vector) |
| Panel thuộc tính | Text: nội dung, font, cỡ, đậm/nghiêng, căn lề, góc xoay; Barcode: cột dữ liệu; chung: X/Y/Rộng/Cao (mm) |
| Xem trước | Render PNG bằng `ImageExport` với dữ liệu mẫu |
| Mở designer | Qua `ILabelDesignerService` (Core) do module Kho implement — tránh dependency Dashboard → Warehouse |
