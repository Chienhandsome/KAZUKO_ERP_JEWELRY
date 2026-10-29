# Lưu cấu hình cột vào Firebird (29/10/2026)

## Phạm vi

78 DataGrid có quick menu, khóa bảng duy nhất và khóa cột ổn định. DataGrid và
cột báo cáo PDF cùng dùng DataGridColumnLayoutStore. Lịch chấm công theo ngày/ca
là lịch tương tác với header/body cuộn đồng bộ, không thuộc layout cột DataGrid;
danh sách card/ListBox không có cột cũng không áp dụng.

## Lưu và tải

- Migration 93: USER_TABLE_LAYOUTS. PK (COMPANY_ID, USER_ID, SETTINGS_KEY), payload
  JSON BLOB UTF8, REVISION tăng mỗi lần ghi và UPDATED_AT. FK xóa theo tài khoản/
  công ty; không chặn xóa tài khoản vì cấu hình cá nhân.
- Cùng tài khoản trong cùng công ty dùng cùng layout trên các máy/chi nhánh;
  tài khoản hoặc công ty khác có cấu hình riêng. Không dùng MachineId trong DB.
- Khi đăng nhập, App tạo repository ghim session + connection string gốc, tải DB
  bất đồng bộ trước khi tạo ShellViewModel. View/ViewModel không truy vấn SQL.
- ITableColumnLayoutStore là cache đồng bộ; thay đổi được ghi ngay vào outbox JSON
  riêng theo DB/user/branch trên máy rồi đẩy DB bằng worker bất đồng bộ. Save DB
  nằm trong transaction; lỗi không làm mất snapshot cũ. Đây là tùy chọn trình bày,
  không phải dữ liệu nghiệp vụ, không tạo audit nghiệp vụ cho từng lần kéo cột.
- Worker ghi tuần tự; acknowledgment của bản cũ không xóa thay đổi mới phát sinh
  khi request đang chạy. Trên nhiều máy, snapshot commit sau cùng thắng; REVISION
  theo dõi lần ghi, không phải khóa chặn người dùng đổi cấu hình ở máy khác.
- Mất kết nối: dùng cache có sẵn, giữ thay đổi Pending qua lần tắt/mở, tự thử lại
  mỗi 15 giây. Cấu hình đang mở không tự nhảy theo thay đổi từ máy khác; lần đăng
  nhập tiếp theo tải snapshot DB mới. Thanh trạng thái hiển thị đã lưu/đang lưu/
  chờ đồng bộ, tooltip giải thích khi chưa kết nối được.
- Đóng app/đăng xuất flush debounce của grid trước rồi chờ DB tối đa 8 giây. Nếu
  chưa xong, outbox còn nguyên để thử lại lần đăng nhập sau. Grid giữ store của
  phiên lúc mở để sự kiện muộn không ghi nhầm sang tài khoản mới.
- Đổi DB vẫn cần khởi động lại theo luồng hiện có. Repository đang chạy giữ kết
  nối DB gốc ngay cả khi profile toàn cục đã đổi, không đẩy outbox cũ vào DB mới.

## Tương thích JSON và snapshot

Snapshot v1: SchemaVersion và Columns(Key, IsVisible, DisplayIndex, Width).
Tất cả table keys ASCII được chuẩn hóa chữ thường, tối đa 160 ký tự; key cột không
lấy từ nhãn hiển thị. Từ chối key cột trùng, width vô hạn/không dương, version lạ.
Cột mới chưa có trong snapshot giữ mặc định; cột đã bỏ được UI bỏ qua.

File legacy %LocalAppData%\KazukoERP\{SettingsKey}.json chỉ nhập khi tài khoản chưa
có cấu hình bảng trong DB và đã kiểm tra DB thành công. Nhập khi mở bảng lần đầu;
DB đã có thì luôn ưu tiên DB. File cũ không bị xóa. Khi startup offline, không
nhập file legacy dùng chung vì chưa thể biết DB có cấu hình của tài khoản hay chưa.
Outbox mới ở %LocalAppData%\KazukoERP\table-layouts\{hash-scope}\cache.json, chứa
snapshot + cờ Pending, không chứa mật khẩu/connection string. Hash gồm danh tính
DB/kết nối và user/branch; DB vẫn chia sẻ layout theo company/user.

## Migration và khôi phục

- Đã đọc MAX(VERSION_NO)=92 trên DB ứng dụng trước khi cấp migration 93.
- Artifact: database/migrations/0093__user_table_layouts.sql, checksum toàn tệp
  EE0A9C3E946DA4721F2864F2C85E3ECFE970916782921F01F93FE58CAAEDEC9D.
- Tạo logical backup artifacts/table-layout-db/pre93.fbk, khôi phục độc lập sang
  artifacts/table-layout-db/test89_table_layout.fdb và chạy migration bằng
  database/tools/Invoke-KazukoMigration.ps1 trên bản test trước khi áp dụng DB chính.
- Rollback: đưa app về bảo trì, dùng bản backup đã xác minh khôi phục sang file mới
  và chạy phiên bản app cũ với DB đó. Không drop bảng/ledger của DB đang vận hành.
  Giữ file JSON/outbox để có thể nhập lại setting; không phục hồi đè DB có giao dịch
  mới nếu chưa đánh giá dữ liệu phát sinh sau backup. Ưu tiên forward fix.

## Kiểm tra

- DatabaseTableColumnLayoutSessionTests: DB ưu tiên legacy, nhập bảng còn thiếu,
  hàng đợi offline qua dispose/reopen, retry sau kết nối, thay đổi trong lúc save.
- TableColumnLayoutStoreTests: JSON legacy và snapshot mới.
- DataGridColumnLayoutTests: 78 bảng có key/menu, dialog giữ width, flush khi đóng,
  grid cũ không ghi nhầm store sau đổi user.
- Kazuko.TableLayouts.IntegrationChecks chỉ nhận bản restore test89_*.fdb. Tạo
  công ty/chi nhánh test trong bản restore, kiểm tra user/company isolation,
  cùng công ty chia sẻ layout, BLOB lớn, version/checksum, rollback update lỗi và
  repository không đổi DB theo profile toàn cục.
- Kiểm tra tay: login → chọn cột/đổi thứ tự/kéo rộng → tắt/mở → login cùng user;
  login user khác; đổi máy; ngắt DB rồi sửa cột, mở lại/kết nối và chờ đồng bộ.
  Không tạo dữ liệu nghiệp vụ thử trên DATA.FDB.

## Kết quả triển khai

Ngày 29/10/2026 đã áp dụng migration 93 lên DATA.FDB bằng runner, sau khi backup/
restore và kiểm thử bản test thành công. Schema verification đạt; ledger checksum
khớp artifact. USER_TABLE_LAYOUTS trên DB chính có 0 dòng ngay sau migration,
không nhập dữ liệu thử; setting cũ sẽ được nhập theo tài khoản khi mở từng bảng.
.gitattributes giữ nguyên byte artifact 93 để checkout không thay checksum.

Build toàn solution: 0 warning/0 error. 21 test Shared.UI và 6 test persistence/
cache đều đạt. Kiểm thử tích hợp Firebird đạt các bước ở trên; tiến trình kiểm tra
mới đọc lại đúng snapshot do tiến trình trước lưu. Chưa kiểm tra tay đầy đủ phiên
ERP với mọi theme/DPI hoặc đồng thời trên hai máy.
