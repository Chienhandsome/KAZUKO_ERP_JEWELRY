# Bộ lọc lịch sử mua hàng và công nợ khách hàng

## Hành vi

Mỗi tab có thanh lọc độc lập: tìm khách bằng mã/tên/SĐT → Tìm khách → chọn hồ sơ,
từ ngày/đến ngày và Lọc / Làm mới. Chọn khách tải lại ngay với ngày đang nhập;
sửa ngày rồi bấm Lọc / Làm mới. Để trống ngày nghĩa là không giới hạn đầu/cuối.
Mới mở chưa có lựa chọn khách; không mặc định lấy dòng đầu bên danh sách.
Danh mục, Lịch sử mua hàng và Công nợ giữ lựa chọn riêng. Chỉ shortcut Xem tất cả
hoặc Xem chi tiết ở card danh mục mới chủ động đưa khách vào tab tương ứng.

Lịch sử mua hàng hiện tối đa 100 đơn hợp lệ mới nhất trong khoảng ngày, gồm
mọi giờ của ngày cuối. Bộ lọc được áp trong Firebird trước giới hạn số dòng.
Công nợ: ngày chỉ lọc lịch sử thanh toán/hoàn tiền. Tổng phát sinh, đã thanh toán,
nợ hiện tại và quá hạn vẫn là toàn bộ công nợ hiện tại của khách đã chọn.
Nút Thu tiền dùng ID đã chọn trong tab Công nợ, luôn phân bổ theo khoản nợ mở;
không tính số có thể thu từ vài khoản thanh toán đang hiển thị.

## Kiểm tra

1. Chọn khách A ở danh mục, B ở lịch sử, C ở công nợ. Đổi dòng/tìm/lọc danh mục:
   hai tab vẫn B/C; bộ chọn tìm được cả khách bị ẩn bởi bộ lọc danh mục.
2. Ngày đầu=ngày cuối: đơn lúc 23:59:59 vẫn có, trước/sau ngày bị loại. Kiểm tra
   khách nhiều hơn 100 đơn có thể xem kỳ cũ, không chỉ lọc trên 100 dòng mới nhất.
3. Ngày đầu sau ngày cuối: báo lỗi, xóa kết quả cũ, không gọi query. Bỏ trống một
   hoặc cả hai ngày hoạt động. Chưa chọn khách không gọi history/snapshot.
4. Đổi khoảng ngày thanh toán: khoản thu/hoàn lọc đúng, tổng nợ không đổi. Thu tiền
   khi danh mục đang chọn khách khác phải hiện đúng khách trong tab Công nợ.
   Hủy dialog không ghi; xác nhận cập nhật số nợ của khách tab đã chọn.
5. Đổi khách nhanh khi đang tải: kết quả muộn không ghi đè khách mới. Lỗi đọc dữ
   liệu báo rõ; Lọc / Làm mới thử lại được.
6. Light/Dark, font 90/100/125/150%, cửa sổ 900x520 và 1500x900: thanh lọc wrap,
   tab cuộn được ở cửa sổ nhỏ. Kiểm tra chọn khách/ngày bằng bàn phím.

## Kiểm chứng

Customers.Tests kiểm tra độc lập state, khoảng ngày gửi xuống repository, ngày
thanh toán không đổi nợ hiện tại, shortcut chủ động, response cũ và luồng thu tiền.
WalkInCustomerDatabaseTests dùng DB tạm kiểm tra biên ngày và lọc trước FIRST N.
CustomerTabsViewTests dựng cả tab lịch sử/công nợ trong ma trận theme/font/kích
thước. Ảnh tùy chọn qua KAZUKO_CUSTOMER_UI_CAPTURE_DIR. Không sửa DATA.FDB chính.
Không cần migration hay quyền mới; truy vấn vẫn qua proxy ICustomerRepository
và IDebtWorkbenchRepository, thu tiền giữ DEBT_SETTLEMENT:Add.
