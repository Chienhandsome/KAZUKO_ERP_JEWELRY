# Bảng giá vàng trên TV

Mở **Bảng giá → Bảng giá TV** bằng tài khoản có quyền sửa bảng giá vàng. Có thể dùng đồng thời TV nối HDMI và TV có trình duyệt trong cùng mạng LAN.

## Thiết lập

1. Nhập tên tiệm, nội dung chạy; chọn màu chữ tên tiệm, nền, viền và giá. Có thể chọn màu sẵn hoặc nhập mã `#RRGGBB`.
2. Chọn căn lề giá, đơn vị VND/chỉ hoặc VND/lượng, số dòng mỗi trang. Tích các loại vàng muốn hiển thị và dùng nút lên/xuống để sắp thứ tự.
3. Nhấn **Áp dụng & xem thử** để lưu và xem bố cục. Bản xem thử vẫn dùng được khi chưa bật phát TV.
4. HDMI: chọn đúng màn hình trong danh sách rồi nhấn **Mở toàn màn hình HDMI**. Windows cần nhận TV như một màn hình mở rộng. Esc hoặc Alt+F4 đóng cửa sổ toàn màn hình này.
5. LAN: bật hiển thị TV và cho phép LAN, áp dụng, rồi mở đầy đủ địa chỉ được cung cấp trên trình duyệt TV. Máy ERP và TV phải cùng mạng, không bị tính năng cô lập thiết bị của Wi-Fi chặn. Nếu Windows hỏi quyền mạng, cho phép ứng dụng trên mạng riêng đang sử dụng. Ứng dụng không tự thay đổi firewall. Có thể đổi cổng khi cổng mặc định 8878 đang bận. Nút toàn màn hình trên TV phụ thuộc trình duyệt hỗ trợ.

Giữ ERP chạy và đăng nhập trong lúc phát. Đóng màn hình thiết lập vẫn tiếp tục phát nếu đã bật TV; đăng xuất hoặc thoát ERP dừng phát. Cấu hình được lưu theo công ty/chi nhánh trên máy hiện tại. Sau khi mở lại ERP, vào Bảng giá TV để khởi động lại; HDMI cần bấm mở lại cửa sổ.

## Nguồn giá và cập nhật

- Lấy giá mua/bán đã lưu từ **bảng giá vàng bán lẻ hiện có**, chỉ loại vàng đang hoạt động của công ty và tiền tệ VND. Không có bảng giá TV riêng.
- Chuyển đổi đơn vị từ giá/chỉ bằng quy tắc hiện có của Core. Giá định dạng số nguyên theo Việt Nam.
- ERP đọc lại giá mỗi 3 giây; trình duyệt TV lấy dữ liệu mỗi 3 giây, nên có thể cần khoảng 6 giây để thấy giá vừa lưu. Thời gian “Giá cập nhật” lấy từ dữ liệu giá.
- Quá số dòng đã chọn thì tự chuyển trang sau 12 giây. Chữ chạy ở cuối màn hình.
- Lỗi đọc dữ liệu ERP: hiện cảnh báo giá có thể cũ và ẩn giá sau 60 giây không đọc thành công. Mất kết nối trình duyệt: cảnh báo và ẩn giá sau 3 lần tải thất bại; tự phục hồi khi kết nối trở lại.

## Kỹ thuật và kiểm tra

Core chứa cấu hình và bộ dựng snapshot; Infrastructure lưu JSON cục bộ và phục vụ HTTP; module PriceBoard quản lý thiết lập, lịch cập nhật và cửa sổ HDMI/WebView2. Không thay schema database hoặc ghi dữ liệu nghiệp vụ.

HTTP chỉ có GET cho trang, JS/CSS và snapshot công khai (tên tiệm, loại vàng, giá, giao diện). Không có API sửa giá hay truy cập hồ sơ ERP. Mặc định chỉ bind loopback; chỉ bind LAN khi đã bật cả TV và LAN. Đường dẫn có token ngẫu nhiên, đổi khi máy chủ khởi động lại; cần chép lại địa chỉ sau khi đổi cổng/chế độ hoặc mở lại ERP. Đây là HTTP trong LAN; chỉ chia sẻ địa chỉ trong mạng cửa hàng. HDMI và xem thử nhúng cần WebView2 Runtime; LAN vẫn thiết lập được khi thiếu runtime.

Kiểm thử tự động: `PriceBoardTvTests` kiểm tra lọc/đơn vị/thứ tự, cấu hình theo công ty/chi nhánh, GET/token/cache/method và cập nhật snapshot, kết nối LAN, giải phóng cổng. Kiểm tra thực tế khi triển khai: hai màn hình có DPI khác nhau, chọn đúng TV, Esc, TV browser/fullscreen, lưu giá mới, đổi cổng, ngắt mạng và đăng xuất. Khả năng tương thích trình duyệt TV phụ thuộc model TV.
