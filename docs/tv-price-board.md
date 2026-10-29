# Bảng giá vàng trên TV

Mở **Bảng giá → Bảng giá TV** bằng tài khoản có quyền sửa bảng giá vàng. Hỗ trợ màn hình HDMI/Miracast và TV có trình duyệt trong cùng mạng LAN.

## Thiết lập

1. Nhập tên tiệm, nội dung chạy; chọn màu chữ tên tiệm, nền, viền và giá. Có thể chọn màu sẵn hoặc nhập mã `#RRGGBB`.
2. Chọn căn lề giá, đơn vị VND/chỉ hoặc VND/lượng, số dòng mỗi trang. Tích các loại vàng muốn hiển thị và dùng nút lên/xuống để sắp thứ tự.
3. Nhấn **Áp dụng & xem thử** để lưu và xem bố cục. Bản xem thử vẫn dùng được khi chưa bật phát TV.
4. HDMI: chọn đúng màn hình trong danh sách rồi nhấn **Chiếu lên màn hình đã chọn**. Miracast: dùng bước **Chọn thiết bị Miracast…** trước, chọn TV trong Windows rồi chọn màn hình đích; xem hướng dẫn bên dưới. Windows cần nhận TV như một màn hình mở rộng để máy tính tiếp tục làm việc riêng. Esc hoặc Alt+F4 đóng cửa sổ toàn màn hình này.
5. LAN: bật hiển thị TV và cho phép LAN, áp dụng, rồi mở đầy đủ địa chỉ được cung cấp trên trình duyệt TV. Máy ERP và TV phải cùng mạng, không bị tính năng cô lập thiết bị của Wi-Fi chặn. Nếu Windows hỏi quyền mạng, cho phép ứng dụng trên mạng riêng đang sử dụng. Ứng dụng không tự thay đổi firewall. Có thể đổi cổng khi cổng mặc định 8878 đang bận. Nút toàn màn hình trên TV phụ thuộc trình duyệt hỗ trợ.

Sau khi bật phát, tiến trình bảng giá riêng tiếp tục chạy khi đóng màn hình thiết lập, đăng xuất hoặc thoát ERP. Giữ máy tính bật và không ngủ. Dừng phát bằng biểu tượng Bảng giá TV ở khay hệ thống. Cấu hình được lưu theo công ty/chi nhánh trên máy hiện tại. Mở lại ERP sẽ nối lại tiến trình bảng giá đang chạy khi vào Bảng giá TV. Sau khi máy tắt hoặc đã dừng host ở khay hệ thống, cần mở ERP để bật phát lại và chọn màn hình HDMI/Miracast nếu cần.

## Nguồn giá và cập nhật

- Lấy giá mua/bán đã lưu từ **bảng giá vàng bán lẻ hiện có**, chỉ loại vàng đang hoạt động của công ty và tiền tệ VND. Không có bảng giá TV riêng.
- Chuyển đổi đơn vị từ giá/chỉ bằng quy tắc hiện có của Core. Giá định dạng số nguyên theo Việt Nam.
- ERP đọc lại giá mỗi 3 giây; trình duyệt TV lấy dữ liệu mỗi 3 giây, nên có thể cần khoảng 6 giây để thấy giá vừa lưu. Thời gian “Giá cập nhật” lấy từ dữ liệu giá.
- Quá số dòng đã chọn thì tự chuyển trang sau 12 giây. Chữ chạy ở cuối màn hình.
- Lỗi đọc dữ liệu ERP: hiện cảnh báo giá có thể cũ và ẩn giá sau 60 giây không đọc thành công. Mất kết nối trình duyệt: cảnh báo và ẩn giá sau 3 lần tải thất bại; tự phục hồi khi kết nối trở lại.

## Kỹ thuật và kiểm tra

Core chứa cấu hình/snapshot và contract host; Infrastructure lưu JSON, đọc giá theo công ty và phục vụ HTTP/IPC; module PriceBoard điều khiển thiết lập. Chế độ host riêng của executable ERP quản lý lịch cập nhật, cửa sổ HDMI/Miracast và khay hệ thống. Không thay schema database hoặc ghi dữ liệu nghiệp vụ.

HTTP chỉ có GET cho trang, JS/CSS và snapshot công khai (tên tiệm, loại vàng, giá, giao diện). Không có API sửa giá hay truy cập hồ sơ ERP. Mặc định chỉ bind loopback; chỉ bind LAN khi đã bật cả TV và LAN. Điều khiển host dùng named pipe chỉ dành cho cùng tài khoản Windows, không có API sửa cấu hình trên HTTP/LAN. Đường dẫn có token ngẫu nhiên, đổi khi máy chủ khởi động lại; cần chép lại địa chỉ sau khi đổi cổng/chế độ hoặc khởi động lại tiến trình bảng giá. Đây là HTTP trong LAN; chỉ chia sẻ địa chỉ trong mạng cửa hàng. HDMI và xem thử nhúng cần WebView2 Runtime; LAN vẫn thiết lập được khi thiếu runtime.

Kiểm thử tự động: `PriceBoardTvTests` kiểm tra lọc/đơn vị/thứ tự, cấu hình theo công ty/chi nhánh, GET/token/cache/method và cập nhật snapshot, kết nối LAN, giải phóng cổng. Kiểm tra thực tế khi triển khai: hai màn hình có DPI khác nhau, chọn đúng TV, Esc, TV browser/fullscreen, lưu giá mới, đổi cổng, ngắt mạng, đóng/đăng xuất ERP và dừng host ở khay hệ thống. Khả năng tương thích trình duyệt TV phụ thuộc model TV.

## Miracast — chọn thiết bị rồi chọn màn hình

Mở Bảng giá TV → phần Kết nối TV:

1. Bật Screen Mirroring / Miracast trên TV.
2. Bấm **1. Chọn thiết bị Miracast…**. App mở bảng Cast của Windows (Windows + K).
   Người dùng chọn TV và hoàn tất kết nối/xác nhận trên TV nếu Windows yêu cầu.
   Đóng/hủy bảng Cast không tự phát bảng giá hoặc thay đổi setting LAN.
3. Bấm **2. Chọn chế độ Mở rộng (Extend)…** để mở lựa chọn Windows + P; chọn Extend
   nếu muốn máy tính tiếp tục làm việc riêng. App không tự đổi chế độ hệ thống.
4. Danh sách **3. Chọn màn hình để chiếu (HDMI / Miracast)** tự tải lại khi màn hình
   kết nối/ngắt hoặc khi quay về cửa sổ; cũng có nút Tải lại màn hình. Chọn rõ màn
   hình TV. Không tự chọn màn hình chính khi TV đã lưu không còn kết nối.
5. Bấm **Chiếu lên màn hình đã chọn**. App kiểm tra lại màn hình trước/sau khi lưu
   cấu hình, rồi mở cửa sổ bảng giá toàn màn hình. HDMI dùng cùng bước chọn màn hình.

Chỉ mở danh sách Cast chưa đồng nghĩa đã kết nối. Nếu máy hoặc TV thiếu Miracast,
Windows sẽ báo; vẫn có thể dùng HDMI hoặc LAN bằng trình duyệt. Khi TV ngắt kết
nối, cửa sổ bảng giá trên màn hình đó đóng, không tự chuyển sang màn hình máy tính.
Esc/Alt+F4 đóng cửa sổ bảng giá; ngắt Miracast bằng Windows + K → Disconnect.
Đóng cửa sổ bảng giá hoặc đăng xuất ERP không tự ngắt kết nối màn hình của Windows; host vẫn chạy nền.

Kỹ thuật: ITvDisplayService đóng gói danh sách màn hình và mở picker/chế độ chiếu
bằng shortcut Windows được SendInput gửi sau thao tác bấm nút của người dùng.
Không dùng API Casting cho media vì bảng giá là cửa sổ desktop/WebView2. Window
lắng nghe DisplaySettingsChanged và tháo listener khi đóng; VM giữ lựa chọn theo
Device qua lượt nạp lại, không theo chỉ số danh sách. Không migration DB hoặc
thay đổi hợp đồng lưu cấu hình TV; MonitorDevice hiện có vẫn lưu màn hình đã chọn.

Tham chiếu Microsoft:
- https://support.microsoft.com/en-us/windows/hardware/display-graphics/how-to-use-multiple-monitors-in-windows
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput

Kiểm thử thêm: Kazuko.Modules.PriceBoard.Tests kiểm tra mở picker nhưng chưa phát,
chọn target bắt buộc, TV mới xuất hiện, giữ lựa chọn khi refresh, ngắt kết nối,
ngắt trong lúc áp dụng, bounds mới, màn hình đã lưu bị thiếu và lỗi mở picker.
Kiểm tra với TV Miracast thật vẫn cần máy/TV hỗ trợ: chọn đúng tên TV, xác nhận
kết nối, Extend, chọn màn hình, phát, ngắt kết nối và kết nối lại. Không coi unit
test dùng fake display service là bằng chứng đã pair thành công với TV vật lý.

Kết quả kiểm tra 29/10/2026: build solution và app thành công, 0 warning/0 error.
10 test luồng Miracast/màn hình và 7 test nguồn giá/HTTP-LAN đều đạt. Đã render
bố cục Light/Dark ở font 90%, 100%, 125%, 150% (kích thước tối thiểu và mặc định),
ảnh kiểm tra dưới artifacts/miracast-preview. Preview dùng dữ liệu/dịch vụ giả,
chưa xác minh ghép nối hoặc phát lên TV Miracast vật lý; chưa kiểm tra DPI Windows
khác nhau trên phần cứng thật. Không tự khởi động ERP hoặc kết nối TV của người dùng.

## Phát nền khi đóng ERP

Sau khi bật hiển thị TV và áp dụng, có biểu tượng **Bảng giá TV** ở khay hệ thống.
ERP có thể đóng hoặc đăng xuất; màn hình HDMI/Miracast và địa chỉ LAN vẫn do host
độc lập phục vụ, tiếp tục đọc giá từ DB mỗi 3 giây. Mở lại Bảng giá TV trong ERP
nối lại host hiện có của đúng DB/chi nhánh/phiên Windows, không tạo trùng hay đổi
URL/token khi chỉ nối lại hoặc đổi nội dung/màu. Đổi cổng/chế độ LAN vẫn tạo URL mới.

Nhấp phải biểu tượng khay hệ thống để xem trạng thái, chép địa chỉ LAN, hiện lại
bảng giá trên màn hình đã chọn, tạm dừng/tiếp tục, mở ERP để thiết lập hoặc **Dừng
phát và thoát**. Bật lại sau khi dừng bằng cách mở Bảng giá TV trong ERP. Tiến trình
chỉ xem thử (chưa bật phát) tự thoát sau 30 giây không còn cửa sổ ERP điều khiển.
Đóng ERP không ẩn toàn bộ ERP chạy nền; chỉ tiến trình bảng giá còn sống. Máy tắt/
đăng xuất Windows thì host dừng. Sau khi mở lại máy cần mở ERP để bật phát lại.

Host giữ đúng kết nối DB lúc khởi động, không tự chuyển theo DB mới của ERP.
Đổi DB trong ERP không dừng host cũ; dừng đúng host tại khay hệ thống trước khi
phát DB mới nếu dùng cùng cổng. Mỗi DB/chi nhánh/phiên Windows có một instance;
phát nhiều scope cùng lúc phải chọn cổng khác nhau. Mỗi instance có một cửa sổ
HDMI/Miracast, có thể đồng thời phục vụ nhiều trình duyệt LAN.

Triển khai: dùng cùng KAZUKO_ERP_JEWELRY.exe với chế độ --price-board-host, chạy
thành process riêng; nhánh này không bootstrap shell, module nghiệp vụ hoặc login.
Client truyền snapshot cấu hình/đích chiếu qua named pipe CurrentUserOnly có frame
JSON giới hạn 1 MiB và allow-list lệnh. Mutex theo scope chống mở trùng. Connection
profile gốc được chụp vào environment của child process, không đưa password lên
command line hoặc ghi vào file. Không lưu session/mật khẩu người dùng ERP vào JSON.
Host tự kiểm tra user/quyền sửa bảng giá từ DB định kỳ: logout ERP không thu hồi
quyền phát đã bật, nhưng khóa user/thu hồi quyền trong DB sẽ tạm ngừng công khai giá.
Nếu không đọc được DB, báo giá cũ và ẩn sau 60 giây; kiểm tra tuổi dữ liệu cả lúc
HTTP đọc snapshot để tránh lộ giá cũ khi một truy vấn đang chờ lâu.

Host không ghi giá hay dữ liệu nghiệp vụ. Giá lấy qua FirebirdTvPriceSource theo
COMPANY_ID đã đối chiếu chi nhánh. Không migration mới, vẫn dùng cấu hình TV JSON
hiện có. Các lần sửa giá vẫn qua ERP và quyền/nghiệp vụ hiện có.

Kiểm tra tự động: TvHostLifetimeTests xác nhận detach/logout không gửi Stop và
callback muộn không nối lại phiên cũ; PriceBoardHostProtocolTests kiểm tra framing,
size và scope. Kazuko.PriceBoardHost.IntegrationChecks dùng hai tiến trình điều
khiển khác nhau trên bản restore test89_*.fdb: host sống sau controller exit,
reattach giữ PID/URL, đọc giá DB mới khi ERP không còn, từ chối đổi company và dừng
thì process/cổng thoát sạch. Không chạy bài ghi dữ liệu này trên DATA.FDB.

Kết quả kiểm tra phát nền 29/10/2026: build solution và app sạch, 0 warning/0 error;
14 test module PriceBoard và 12 test Infrastructure (HTTP/LAN + IPC) đều đạt.
Integration qua hai controller process đã xác minh host độc lập còn sống sau exit,
PID/URL giữ nguyên khi nối lại, giá DB mới tự xuất hiện, sai company bị từ chối,
khóa tài khoản thì ẩn giá và không cho sửa qua IPC, mở lại tài khoản thì phục hồi;
dừng host giải phóng process/cổng. Mọi dữ liệu thử chỉ ghi vào bản restore test89.
Đã render lại thông báo phát nền ở Light/Dark và font 90–150%; chưa thử phát lên
TV HDMI/Miracast vật lý hoặc Windows sleep/logoff thật. Host thử nghiệm đã dừng.
