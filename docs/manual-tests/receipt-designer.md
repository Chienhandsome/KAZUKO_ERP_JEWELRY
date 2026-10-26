# Sửa mẫu bill kéo-thả

## Kiểm tra thao tác

1. Cài đặt → Mẫu in → Mẫu bill, chọn A5 ngang → Sửa mẫu bill. Tên mẫu phải đúng khổ đã chọn. Kiểm tra tương tự A5 dọc.
2. Chọn vùng đầu phiếu. Kéo một dòng chữ, kéo tám điểm để resize, nhập X/Y/Rộng/Cao bằng mm. Bật bám lưới, kéo lại. Mũi tên dịch 1 px, Shift+mũi tên dịch 10 px khi canvas có focus.
3. Chọn bảng từ danh sách/canvas, kéo cả bảng; đổi rộng/cao phải phân bổ cho cột/hàng. Chọn ô trong danh sách hoặc nhấp đúp ô, sửa chữ/font/đậm/nghiêng/căn ngang/dọc. Biểu thức trong [ ] vẫn được giữ nếu không sửa.
4. Chuyển vùng khác và quay lại: chỉnh sửa còn nguyên. Trang phụ (Page2/3/4 nếu có) chỉnh riêng trong bộ chọn vùng. Thêm chữ, sửa và xóa chữ; đổi chiều cao vùng.
5. Xem trước: hiển thị bản nháp dùng dữ liệu minh họa, chưa ghi DB. Thử biểu thức không hợp lệ, bấm Lưu: báo lỗi, cửa sổ vẫn mở và mẫu cũ không bị thay.
6. Lưu mẫu hợp lệ: designer đóng, preview Cài đặt cập nhật. Mở lại: nội dung/vị trí còn đúng. Mẫu khổ còn lại không đổi. Mở bill bán hàng thật và đối chiếu preview; không gửi lệnh in nếu chưa chủ động bấm In.
7. Đóng/Hủy khi có thay đổi: chọn Không phải tiếp tục sửa; bấm Hủy lần nữa vẫn hỏi và chọn Có đóng mà không lưu. Đóng khi không có thay đổi không hỏi.
8. Tài khoản chỉ Xem không có nút sửa khả dụng. Mất quyền Sửa trong khi đang mở: lưu phải bị từ chối.

## Ma trận hiển thị

- Light/Dark; font 90%, 100%, 125%, 150%; DPI Windows 100%, 150%, 200%.
- Cửa sổ 900×550 và maximized; toolbar tự xuống dòng, canvas có cuộn hai chiều, panel thuộc tính cuộn dọc. Thử zoom 50–200% và chữ tiếng Việt dài.
- Kiểm tra Tab/focus/Enter/Escape, dropdown chọn vùng, danh sách và các ô số. Mặt giấy trắng và chữ đen là màu in, phần khung theo theme.
- Mở lại designer tem để kiểm tra bộ style dùng chung vẫn giữ giao diện tem.

## Dữ liệu và phục hồi

Không thay schema. Sao lưu `.frx`/bản ghi PRINT_TEMPLATES của hai mã A5 trước khi kiểm tra lưu trên DB thật. Hủy chỉ bỏ bản nháp; khôi phục mẫu đã lưu cần dùng bản sao lưu qua công cụ quản trị. Các test tự động dùng store giả, không ghi DATA.FDB.
