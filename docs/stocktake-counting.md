# Kiểm kê — giai đoạn 2

## Phạm vi

Kho → Kiểm kê cho phép lập phiếu, chụp tồn hệ thống, nhập số thực tế/quét serial, lưu nháp, mở lại, đối chiếu biến động và chốt kết quả. Không sinh STOCK_DOCUMENTS, movement, thu/chi, công nợ hoặc tự điều chỉnh tồn. In phiếu và điều chỉnh tồn thuộc giai đoạn sau. Không sửa/xóa dữ liệu kiểm kê legacy trong STOCKTAKES/STOCKTAKE_LINES.

## Thao tác

- Lập phiếu → chọn một kho hoạt động thuộc chi nhánh phiên đăng nhập và ngày kiểm kê → Bắt đầu kiểm kê. Người lập được lấy từ session, mã KK sinh tự động. Mỗi lần tạo dùng request ID để chống tạo lặp khi gửi lại.
- Kho NVL: chụp danh mục NVL hoạt động và cả NVL ngừng hoạt động còn dòng tồn trong kho; số chưa nhập là NULL, khác với 0. Chọn dòng, nhập số thực tế và ghi chú rồi Ghi nhận dòng. Số lượng tối đa 4 chữ số thập phân, không âm. Có lệnh xác nhận toàn bộ ô NVL chưa nhập bằng 0. Nhập liệu đang gõ được ghi nhận khi chuyển dòng hoặc lưu, số sai không được âm thầm bỏ qua.
- Kho thành phẩm/kho bán hàng: chụp từng sản phẩm có location tương ứng và trạng thái IN_STOCK/DISPLAYED/RESERVED. Quét mã serial, barcode hoặc QR (khớp chính xác), hoặc chọn Đã thấy/Không thấy. Một serial chỉ có 0/1 món; quét lặp không cộng thêm.
- Serial cùng công ty/chi nhánh nhưng ngoài danh sách dự kiến được ghi thành dòng có tồn hệ thống 0, kèm trạng thái hàng tại lúc đọc. Serial không xác định lưu nguyên mã để xử lý sau; không tạo FINISHED_PRODUCTS hoặc tiết lộ hồ sơ ở chi nhánh khác. Mã khớp nhiều món bị từ chối.
- Lưu nháp lưu toàn bộ số đếm/ghi chú. Tải lại hoặc chọn phiếu khác cần xác nhận bỏ thay đổi chưa lưu. Tìm trong bảng theo mã/tên/kết quả đối chiếu; tìm kiếm không làm bỏ các dòng ẩn khi lưu/chốt.
- Chốt kết quả yêu cầu mọi NVL đã có số thực tế, không còn dòng Cần kiểm lại và tồn kho chưa thay đổi kể từ mốc đối chiếu. Serial chưa thấy được coi là thiếu sau xác nhận chốt. Dòng chưa rõ nguồn vẫn được giữ làm kết quả điều tra, chưa tự nhập kho. COUNTED là chốt kết quả, không phải ghi sổ tồn. Không sửa/mở chốt trong giai đoạn này.

## Biến động và đồng thời

Migration 90 thêm LOCATIONS.COUNT_REV và STOCK_BALANCES/FINISHED_PRODUCTS.COUNT_TOKEN. Trigger cập nhật token từng dòng và revision mỗi kho trong cùng transaction với mọi insert/update/delete tồn. Chuyển serial cập nhật cả kho cũ và mới; cả tăng rồi giảm về đúng số cũ vẫn phát hiện được. Không sửa ROW_VERSION hiện có hoặc các giá trị tồn/giá vốn.

Tạo, đối chiếu và chốt khóa hàng location trước khi đọc mốc và dữ liệu tồn. Trigger khiến giao dịch tồn không commit thay đổi cùng kho trong khoảng đọc/chốt; tránh bỏ sót cả dòng mới chưa có trong snapshot. Giao dịch kho vẫn có thể phát sinh giữa các lần lưu nháp, không khóa kho trong suốt quá trình đếm.

Khi tồn đổi, Chốt bị từ chối. Bấm Đối chiếu tồn sẽ cập nhật baseline, giữ số đếm các dòng không đổi, thêm mặt hàng mới, chuyển món rời kho về tồn hệ thống 0; các dòng có thay đổi bị xóa kết quả cũ và đánh dấu Cần kiểm lại. NVL phải nhập lại; serial phải quét lại hoặc xác nhận Đã thấy/Không thấy (kể cả xác nhận 0). Chỉ đối chiếu mà chưa kiểm lại không được chốt. Snapshot ban đầu giữ bất biến ở INITIAL_SNAPSHOT; audit lưu lịch sử đối chiếu.

REVISION trên phiếu chống ghi đè: save/scan/review/close khóa header và kiểm tra revision/status. Lỗi audit phải rollback toàn bộ header/dòng. Mọi truy vấn theo công ty của chi nhánh trong session, và theo đúng chi nhánh; không dùng ID từ client để vượt phạm vi.

## Quyền và triển khai

IStocktakeService (Core) → FirebirdStocktakeService (Infrastructure); StocktakeTabViewModel chỉ điều phối/binding. UI và backend dùng INV_STOCKTAKE: Xem (danh sách/chi tiết/kho), Thêm (tạo), Sửa (đếm/quét/đối chiếu/chốt). Không cấp thêm quyền hoặc bypass qua quyền xem tồn kho.

Migration: database/migrations/0090__stocktake_counting.sql. MAX(VERSION_NO)=89 đã đọc trên database thật trước khi đặt tên. Dùng Invoke-KazukoMigration.ps1 sau backup gbak và khôi phục thử; verification tại database/verification/verify_stocktake_counting.sql. Rollback schema bằng restore backup trước 90 với phiên bản app trước; phải xuất/lưu riêng phiếu kiểm kê phát sinh sau 90 nếu cần giữ. Không sửa checksum migration đã áp dụng.

## Kiểm thử

- Kazuko.Stocktake.IntegrationChecks chỉ nhận file restore test90_*.fdb: tạo/lưu/chốt NVL, quét trùng/unknown/ngoài kho, thay đổi tồn rồi khôi phục lượng cũ, yêu cầu kiểm lại, khóa phiếu đã chốt, phạm vi chi nhánh, audit rollback, hai người lưu đồng thời và không phát sinh movement/chứng từ/thu chi.
- StocktakeTests trong Kazuko.Modules.Warehouse.Tests: validation số lượng/serial, phân quyền backend, lưu số đang gõ, giữ dòng ẩn khi lọc và không nhân đôi serial.
- UI sáng/tối, font 90–150%, cửa sổ hẹp/rộng; bảng cuộn riêng, vùng nhập dòng và thanh thao tác không bị đè.
