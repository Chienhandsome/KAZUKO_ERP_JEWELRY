# Báo cáo và in ấn

## Quyết định

Module Báo cáo dùng **FastReport Open Source** để dựng báo cáo A4 bằng code theo bộ lọc và cấu hình cột. Bill A5 và tem dùng mẫu `.frx` lưu trong database.

- Dữ liệu vẫn lấy qua Application/Infrastructure service (không SQL trong View/ViewModel).
- Các `*PrintService` của module Reports dựng FastReport bands khi chạy. Muốn đổi bố cục A4 phải sửa PrintService hoặc cấu hình cột đã hỗ trợ; sửa file trong `Templates/` không đổi các báo cáo dựng động. Không hứa hẹn designer kéo-thả cho báo cáo A4.
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
| Cài đặt mẫu | Cài đặt → **Mẫu in → Mẫu bill**, chọn khổ A5 ngang/dọc, xem ảnh mẫu đã lưu và bấm **Sửa mẫu bill** để kéo-thả; không có Xuất/Import/Tải lại. |

**Máy in (vd. HP M1212nf):** nút **In** dùng `PrintDocument` thẳng máy mặc định (không hộp thoại), giống setting WPS: khổ **A5**, lề ~4mm, **fit + auto-center + auto-rotate**. Mẫu dọc: lề trái ≥ 14mm / phải ≥ 16mm; khối **Vàng mới / Vàng cũ** trong `ReportSummary`.

### Bind dữ liệu bill

- Parameters: `Tên khách`, `Số phiếu`, `Ngày`, `Tiền vàng mới`, `Tiền vàng cũ`, `Tiền bớt`, `Tổng cộng`, `Tổng cộng bằng chữ`, `Cộng tiền`, …
- DataSource `DataSet`: dòng bán (cột `LOAIVANG` = tuổi vàng thành phẩm).
- DataSource `OldGold`: **bảng thành phần** (Subreport trái dưới — `Loại`, `TL vàng`, `TL hột`, `Đ.giá`, `Thành tiền`). Ưu tiên dòng vàng cũ; nếu không có thì lấy thành phần vàng từ dòng bán.
- Lề mẫu ≥ 10mm; nút **In** = `PrintDocument` A5 Portrait + fit margins + auto-center + auto-rotate (giống WPS).
- Runtime Open Source: clear `Filter` thương mại, tách 2 bảng, `PrintOnParent` cho Subreport.

### Không làm (giữ free)

- Không mua FastReport WPF commercial / Online Designer.
- Không nhúng FastReport Designer thương mại trong app. Designer bill/tem là UI WPF do dự án tự xây dựng trên mẫu `.frx`.
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

- Nút **Cột báo cáo** dùng chung cho các báo cáo bảng, gồm bảng lương tháng.

## Báo cáo lương nhân viên

Tải lại danh sách nhân viên phải bỏ qua thay đổi lựa chọn tạm thời do ComboBox gửi null trong lúc Clear/Add. Chỉ thao tác đổi bộ lọc của người dùng mới tăng revision và hủy dữ liệu cũ; refresh nội bộ giữ ID nhân viên đang chọn và tiếp tục dựng PDF. Kiểm thử binding ComboBox TwoWay thực tế ở `Kazuko.Modules.Reports.Tests/PayrollEmployeeSelectorTests` cho cả bảng tháng và phiếu cá nhân.

- Nhân sự → Báo cáo hoặc Báo cáo → BÁO CÁO NHÂN SỰ: **Bảng lương tháng** (A4 ngang, cột tùy chọn) và **Phiếu lương cá nhân** (A4 dọc, bắt buộc chọn một nhân viên).
- Lọc tháng/năm, nhân viên. Mặc định chỉ kỳ CLOSED; bật **Cho phép bản nháp** để xem DRAFT. Bản nháp có nhãn trên mỗi trang PDF và trong Excel.
- `IPayrollReportService` / `FirebirdPayrollReportService`: phạm vi toàn doanh nghiệp theo chi nhánh của phiên đăng nhập. Tên/mã, công, lương, thưởng và khấu trừ lấy từ snapshot `HR_PAYROLL_LINES`, không tính lại theo hồ sơ/chấm công hiện tại. Danh sách chọn nhân viên cũng đọc snapshot, kể cả người đã nghỉ.
- Thực nhận = lương + thưởng ghi nhận + thưởng điều chỉnh - khấu trừ ghi nhận - khấu trừ điều chỉnh. Đã chi lấy số tiền từ phiếu chi POSTED của `HR_PAYROLL_PAYMENTS` POSTED; phiếu hủy không tính. Các khoản thu/chi riêng không cộng lại vào lương.
- Phiếu cá nhân tách khoản ghi nhận/điều chỉnh, diễn giải, phiếu/ngày/phương thức chi và phần ký nhận. Bảng tháng có tổng cộng thực nhận, đã chi, còn phải chi.
- Dùng `ReportWorkspaceView` với WebView2, FastReport bands dựng động như các báo cáo cấu hình cột; không thêm mẫu FRX hoặc migration. Có lưu PDF, mở PDF, in shell và Excel số/ngày đúng kiểu. Đổi bộ lọc xóa ngay bản xem trước và chặn xuất dữ liệu cũ.
- Xem cần đồng thời REP_VIEW và HR_PAYROLL; in/lưu PDF thêm REP_EXPORT, Excel thêm DATA_EXPORT. Thu hồi quyền xóa dữ liệu/PDF đang giữ.
- Kiểm tra: `tests/Kazuko.PayrollReports.IntegrationChecks`, chỉ chạy trên bản restore `test89_*.fdb`; kiểm chứng kỳ nháp/chốt, chi/hủy chi, scope công ty, quyền, UI Light/Dark và PDF nhiều trang. Không ghi dữ liệu mẫu vào DATA.FDB.

## Cấu hình cột

- Nút **Cột báo cáo** mở dialog giống DataGrid: tick ẩn/hiện, ▲▼ đổi thứ tự.
- Layout lưu theo máy (`reports.*.columns.json`), không ghi Firebird.
- Áp dụng xong dựng lại PDF preview theo cột đã chọn.

## Designer (Community, free)

1. Tải FastReport Designer Community từ [GitHub FastReport releases](https://github.com/FastReports/FastReport/releases).
2. **Bill:** Cài đặt → Mẫu in → chọn A5 ngang/dọc → **Sửa mẫu bill** → sửa, xem trước và lưu trong app.
3. **Báo cáo A4:** dùng **Cột báo cáo** để đổi ẩn/hiện/thứ tự; thay bố cục khác phải sửa PrintService rồi build lại. Designer FRX chỉ áp dụng cho mẫu được pipeline thực tế nạp từ file/database, không áp dụng cho báo cáo dựng động.

Các file FRX/helper legacy còn trong source không phải nguồn bố cục của báo cáo dựng động.

### Designer bill (WPF)

- `IReceiptDesignerService` (Core), implementation trong Sales; Dashboard không tham chiếu Sales.
- Nạp đúng mã khổ đang chọn; không đổi khổ giữa phiên sửa để tránh lưu nhầm mẫu. Quyền `SYS_SETTINGS/Sửa` được kiểm tra khi mở, nạp và trước lưu.
- Toolbar, ô nhập, ComboBox, ListBox và scrollbar dùng chung `Shared.UI/Themes/PrintDesignerResources.xaml` với designer tem. Mặt giấy/mực giữ màu in; chrome dùng theme/font động.
- Chọn **Vùng nội dung** (trang + band), canvas có lưới 1 mm, bật/tắt bám lưới, zoom 50–200%, kéo và resize tám điểm; X/Y/Rộng/Cao nhập mm. Bảng kéo cả khối, resize phân bổ theo cột/hàng; chọn từng ô từ danh sách hoặc nhấp đúp để sửa chữ/font/căn lề. Có thêm chữ, xóa đối tượng, thay chiều cao vùng và di chuyển bằng phím mũi tên (Shift: 10 px).
- Band chứa các dòng dữ liệu được lặp theo dữ liệu thật khi in. Trang phụ của Subreport được chọn và sửa riêng. Canvas trình bày bố cục vùng và biểu thức, **Xem trước** render trang đầu bill bằng dữ liệu minh họa với bản nháp chưa lưu.
- Model chỉnh các thuộc tính layout/text trên XML `.frx`, bảo toàn DataSource, biểu thức, bảng và Subreport; không có trường sửa ScriptText hay mã ứng dụng. Các phần tử không có panel thuộc tính chuyên biệt vẫn được giữ nguyên và có khung để di chuyển.
- Khi nạp mẫu legacy, áp dụng bố cục runtime một lần rồi đánh dấu `ReportInfo.Description=KAZUKO_RECEIPT_DESIGNER_V1`. Runtime tiếp tục chuẩn bị dữ liệu nhưng không tự căn lại summary/di chuyển footer của mẫu đã đánh dấu, nên giữ vị trí đã kéo.
- **Lưu mẫu** phải render thành công bản nháp trước khi ghi `IPrintTemplateStore.SaveAsync`; lỗi biểu thức hoặc mất quyền không thay mẫu đã lưu. Notes `Sửa bằng Bill Designer` ngăn auto-refresh seed ghi đè mẫu sửa. Lưu thành công đóng designer và tải lại preview ở Cài đặt. Hủy/đóng có xác nhận bỏ chỉnh sửa nếu còn bản nháp.
- Không migration; bill vẫn in qua pipeline FastReport/PDF hiện có. Kiểm tra thao tác và theme/DPI: `docs/manual-tests/receipt-designer.md`.

## Báo cáo đã có

| Key | Mẫu |
|-----|-----|
| `reports.warehouse.jewelry_on_hand` | `JewelryOnHandPrintService` dựng động |
| `reports.warehouse.jewelry_on_hand_summary` | `JewelryOnHandSummaryPrintService` dựng động |
| Bill bán hàng (Sales) | `SALES_RECEIPT_A5` trong `PRINT_TEMPLATES` |
| Tem nữ trang 28x12 (Warehouse) | `TEM_NU_TRANG_28x12` trong `PRINT_TEMPLATES` |

## Tem nữ trang 28x12

### Bố cục L.btw (86 × 12 mm)

Mẫu seed riêng `tem_nu_trang_l_28x12.frx` theo ảnh và preview trích từ `L.btw`: ô trái có
tên SP, TTL/TLĐ/TLV, gram, công bán, QR chứa mã hàng, mã hàng dưới QR và HLV dọc;
ô phải có NPP, địa chỉ NPP, NSX, địa chỉ NSX, xuất xứ, ký hiệu và TCCS; MTH trên đuôi.
File `.btw` gốc không bị sửa. Mẫu 1 giữ bộ vẽ cũ. Mẫu L.btw V2 có xử lý riêng cho đầu in nhiệt (bên dưới);
không đổi driver, tốc độ hoặc độ đậm của máy in.

Mẫu L.btw **V2** (`ReportInfo.Description=KAZUKO_LABEL_L_BTW_V2`) dùng
`FontWidth` để giữ chiều cao font, bộ vẽ áp dụng `FontWidthRatio` đã Prepare,
không co cỡ font thêm. Chữ vẽ đơn sắc `SingleBitPerPixelGridFit`, tránh viền xám/
ClearType. QR dùng bộ mã hóa FastReport và API công khai UpdateAutoSize; lấy
kích thước module từ hiệu quiet zone 4 ô mỗi phía. Căn module theo số chấm nguyên
trong tọa độ thiết bị (kể cả lệch dọc/hard margin), không nội suy, giữ vùng trắng
4 ô, tối thiểu 2 chấm/module. QR quá nhỏ cho mã dài báo kích thước tối thiểu cần
thiết thay vì in mã mất nét. Khung seed tăng từ ~7.01 lên ~7.41 mm, đủ QR version 1
ở 203 DPI. Trạng thái Graphics được khôi phục sau mỗi lần vẽ. Mẫu 1/V1 không dùng
nhánh xử lý mới. Migration 87 chỉ cập nhật seed L.btw V1 có hash gốc và ghi chú
seed đúng; bản người dùng tự sửa/import giữ nguyên.

Trọng lượng lưu bằng **chỉ**. Checkbox **Tự đổi đơn vị cho gọn (chỉ / phân / ly)**
ở cả In tem và In thử: mặc định tắt, nhớ trên máy khi chấp nhận hộp in. Tắt luôn
hiện chỉ; bật thì |w| ≥ 1 giữ chỉ, 0.1 ≤ |w| < 1 đổi phân, 0 < |w| < 0.1 đổi ly;
0 vẫn ghi `0 chỉ`. Gram = chỉ × 3.75, hiển thị tối đa 6 chữ số thập phân, dấu phẩy
vi-VN. Không làm tròn hoặc đổi dữ liệu nghiệp vụ gốc. In thử và in hàng thật dùng
chung `JewelryLabelData`; Xem trước dùng lựa chọn đơn vị đã nhớ gần nhất.

NPP/địa chỉ NPP/NSX/địa chỉ NSX/xuất xứ lấy từ các trường riêng trong
**Cài đặt → Thông tin chung**, TCCS lấy ô TCCS hiện có, ký hiệu lấy từ hàng.
Không lấy tên nhà cung cấp của hàng làm NPP/NSX và không hard-code tên/địa chỉ trong mẫu.
Migration 85 thêm 5 cột COMPANIES; lần đầu điền NPP/NSX theo tên/địa chỉ doanh nghiệp
hiện có và xuất xứ `Vietnam`, sau đó người dùng chỉnh độc lập. Bill giữ NAME/ADDRESS.
Chạy migration 85 trước khi dùng build này; sao lưu/khôi phục theo hướng dẫn migration.

Hai bản ghi độc lập trong PRINT_TEMPLATES: `TEM_NU_TRANG_28X12` (mẫu 1) và
`TEM_NU_TRANG_L_28X12` (mẫu 2, L.btw). Mẫu 1 giữ seed cũ
`tem_nu_trang_28x12.frx`, mẫu 2 dùng seed `tem_nu_trang_l_28x12.frx`.
Migration 86 thêm mẫu 2 cho mỗi doanh nghiệp hoạt động nếu chưa có; không cập nhật
nội dung, tên hoặc thời gian của mẫu 1. Backup/restore thử trước migration; ledger
lưu SHA256 toàn bộ artifact. Không chạy lại để ghi đè mẫu 2 đã sửa.

Hộp In tem/In thử có ComboBox **Mẫu tem** với hai lựa chọn, nhớ lựa chọn sau khi
chấp nhận hộp in trong `jewelry.label.printer.json`. File cấu hình cũ/giá trị lạ
quay về mẫu 1. In hàng thật dùng đúng code được chọn, không phụ thuộc mẫu đang sửa.
Trong designer, chọn mẫu rồi **Nạp mẫu** để mở bản trong DB. Nạp sẽ bỏ chỉnh sửa
chưa lưu của bản đang mở (tooltip ghi rõ). **Lưu mẫu** ghi theo code đã nạp, không
phải lựa chọn chưa nạp trong ComboBox, nên không ghi đè nhầm mẫu còn lại.
In thử mẫu đang sửa dùng bản nháp hiện tại; chọn mẫu khác trong hộp in dùng bản
đã lưu của mẫu đó. Hủy hộp in không gửi lệnh in. Mẫu người dùng đã sửa không tự
bị seed thay thế.
Kiểm tra thủ công: `docs/manual-tests/label-template-l.md`.

Trong trình thiết kế, chọn đối tượng mã để đổi giữa **Barcode (Code 128)** và **QR**.
QR mặc định vuông 8 mm, giữ tỉ lệ vuông khi đổi kích thước; nếu vị trí cũ chồng nội dung,
ưu tiên tìm khoảng trống trong tem. Đổi lại barcode khôi phục vị trí/kích thước thanh
trước đó. Loại mã được lưu trong `.frx` và dùng cho canvas, xem trước,
in thử, in hàng thật. Nội dung quét của cả hai là **mã hàng (`ProductCode`)** qua
`DataSet.MAVACH`; không lấy mã barcode riêng nếu khác mã hàng. Dữ liệu xem trước/in thử
dùng mã hàng mẫu `B600022` và số liệu hàng mẫu. Tên tiệm, địa chỉ, NCC, địa chỉ NCC và TCCS
lấy từ **Thông tin chung**: NCC là tên doanh nghiệp.

Trình sửa mẫu tem có **In thử**: chọn máy in và số bản, dùng dữ liệu hàng mẫu với
các chỉnh sửa hiện tại (kể cả chưa lưu). Tên doanh nghiệp, địa chỉ và TCCS lấy từ Thông tin chung.
In vector từ trang đã chuẩn bị của FastReport,
khổ 86 × 12 mm, lề 0. Hộp **Lệch dọc (mm)** cộng thêm vào trục dọc (dương đẩy xuống, âm kéo lên), nhớ riêng theo tên máy in. Hủy chọn máy in thì không gửi lệnh in.

In tem cho sản phẩm nhập kho thành phẩm (dialog Nhập kho TP):

| Mục | Chi tiết |
|---|---|
| Mã mẫu | `TEM_NU_TRANG_28x12` trong `PRINT_TEMPLATES` |
| Seed mặc định | Embedded `tem_nu_trang_28x12.frx` |
| Khổ giấy in | Ngang **86 mm**, cao **12 mm**, lề 0; áp dụng khi chuẩn bị FastReport và cho `PrintDocument`. Khi vẽ vector: bù hard margin của driver, rồi cộng **lệch dọc** (mm) của máy in đang chọn |
| Runtime in | `FinishedGoodsLabelPrintService` — load BLOB → bind DataTable `DataSet` → `Prepare()` → `VectorLabelRenderer` vẽ vector trực tiếp (text/mã vạch) lên `PrintDocument` ở đơn vị mm |
| Chọn tem | Checkbox từng dòng trong bảng SP ("In tem đang chọn") hoặc tất cả ("In tem tất cả") |
| Máy in | Dialog chọn máy in hệ thống, số bản và lệch dọc (từ -20 đến 20 mm). Nhớ máy in lần trước và lệch dọc theo từng tên máy (`%LocalAppData%\KazukoERP\jewelry.label.printer.json`) |
| Dữ liệu | Hàng từ `ManualFinishedProductItem` (mã vạch, tên, loại vàng, TL, công, KH). Tên tiệm, địa chỉ doanh nghiệp, NCC, địa chỉ NCC và TCCS từ `ICompanyProfileService`: NCC là tên doanh nghiệp, TCCS là ô **TCCS (tiêu chuẩn cơ sở)** trong Cài đặt → Thông tin chung. `DataSet.KYHIEU` là KH đã chọn lúc nhập kho |
| Sửa mẫu | Tab **Mẫu in** trong Cài đặt → mục "Mẫu tem nữ trang 28x12" → nút **Sửa mẫu tem** mở designer kéo-thả |

### Designer sửa mẫu tem

Tab **Mẫu tem** hiển thị ảnh PNG của mẫu đã lưu phía trên nút **Sửa mẫu tem**,
theo mẫu được nhớ trong cấu hình In tem. Ảnh dùng chung bộ chuẩn bị dữ liệu minh họa
với designer và lấy thông tin doanh nghiệp từ Thông tin chung. Vào mục Mẫu in hoặc
lưu trong designer sẽ tải lại ảnh; lỗi render hiển thị tại vùng xem trước và không
khóa nút sửa. Hai loại mẫu bill/tem đều là `.frx`, lưu thành các bản ghi riêng trong
`PRINT_TEMPLATES`. Dashboard gọi service qua contract Core, không tham chiếu module khác.

Trình thiết kế trực quan cho mẫu tem nữ trang (tab **Mẫu in** trong Cài đặt):

| Mục | Chi tiết |
|---|---|
| Backing store | FastReport object model: `Report.Load()` từ DB → sửa `TextObject`/`BarcodeObject` → `Report.Save()` → `IPrintTemplateStore.SaveAsync()` |
| Canvas | Tỷ lệ 3x so với kích thước thật (86×12mm, 96dpi); lưới 1 mm (nét đậm mỗi 5 mm) chỉ trên mặt thiết kế; click chọn, kéo di chuyển, kéo 8 góc/cạnh để resize. Mặc định bám lưới 1 mm khi kéo; tắt bằng ô **Bám lưới 1 mm**. Ô X/Y/Rộng/Cao vẫn nhập số lẻ |
| Đối tượng | Chỉ text + barcode (theo phạm vi pipeline in vector) |
| Panel thuộc tính | Text: nội dung, font, cỡ, đậm/nghiêng, căn lề, góc xoay; Barcode: cột dữ liệu; chung: X/Y/Rộng/Cao (mm) |
| Xem trước | Render PNG bằng `ImageExport` với dữ liệu hàng mẫu; tên, địa chỉ, NCC và TCCS lấy từ Thông tin chung |
| Mở designer | Qua `ILabelDesignerService` (Core) do module Kho implement — tránh dependency Dashboard → Warehouse |
