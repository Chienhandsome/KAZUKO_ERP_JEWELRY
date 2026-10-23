# Kiểm tra quét CCCD khách hàng

Ngày triển khai: 2026-10-22. Thiết bị: iMarcone HID bàn phím, USB/Bluetooth.

## Cách dùng

Mở Thêm/Sửa khách hàng (kể cả từ bán hàng) → **Quét CCCD**. Ô nhập tự nhận focus.
Quét QR trên CCCD; Enter đọc thông tin. Nếu máy chưa cấu hình suffix Enter, bấm
**Đọc thông tin**. Có thể dán chuỗi QR. Xem sáu trường so sánh, sửa trực tiếp cột **Từ CCCD** nếu sai dấu/thông tin rồi **Áp dụng vào
form**, bổ sung SĐT và bấm **Lưu**. CCCD không chứa SĐT/nơi cấp nên không tự điền.

Nếu có hồ sơ khớp CCCD/CMND cũ, chọn đúng hồ sơ rồi **Dùng khách hàng này**.
**Mở hồ sơ để cập nhật** cần quyền sửa khách hàng. Khách ngừng hoạt động không
được chọn bán hàng; không tự kích hoạt. Khách hệ thống không được gán CCCD.
Thay số giấy tờ khác đang có phải đánh dấu xác nhận sau khi đối chiếu.

## Kiểm tra trên thiết bị thật (chưa thực hiện tự động)

1. iMarcone nhập đúng dấu tiếng Việt và số 0 đầu trong ô quét; suffix Enter chỉ
   mở xem trước, không áp dụng/lưu ngoài ý muốn. Nếu sai dấu, cấu hình Unicode/HID
   của máy quét; ERP không đoán để sửa tên sai. Dán chuỗi là phương án kiểm tra riêng.
2. Quét hồ sơ chưa có; kiểm tra họ tên, CCCD, ngày sinh, giới tính, địa chỉ, ngày cấp.
   SĐT/nơi cấp/email/ghi chú đã nhập giữ nguyên; địa chỉ/ngày cấp thiếu giữ nguyên.
3. Quét số đã tồn tại, có nhiều hồ sơ và hồ sơ lưu CMND cũ: hiển thị đầy đủ để chọn.
   Từ bán hàng/công nợ, kết quả trả đúng ID khách sẵn có, không tạo mới.
4. Hồ sơ ngừng hoạt động/khách hệ thống không chọn được. Người thiếu quyền sửa
   không mở được sửa từ kết quả. Người thiếu quyền xem không tra cứu được qua proxy.
5. Quét lại chuỗi lỗi hoặc dữ liệu không phải CCCD, ngày sai, quá dài: báo lỗi,
   không giữ bản xem trước cũ. Hủy/Escape không ghi; đóng khi đang tra cứu không áp dữ liệu muộn.
6. Sửa hồ sơ: quét cùng CCCD loại trừ chính ID; quét số khác cần xác nhận.
   Kiểm tra form vẫn bắt buộc SĐT, nhóm hoạt động; nhập tay CCCD trùng cũng bị chặn.
7. Hai quầy cùng lưu một CCCD: chỉ một hồ sơ được tạo. Quầy còn lại nhận thông báo
   trùng sau khi chờ khóa. Khi DB đang bị khóa lâu, thử lưu lại sau khi quầy kia xong.
8. Light/Dark, font 90/100/125/150%, DPI hệ điều hành 100/150/200%, cửa sổ nhỏ/lớn:
   nội dung dài cuộn được, nút Hủy/Áp dụng/Chọn khách luôn truy cập được, Tab/Enter/Escape đúng.

## Kiểm thử tự động và giới hạn

Kết quả ngày 2026-10-22: Customers.Tests 61/61, Sales.Tests 43/43; build Kazuko.App
0 warning, 0 error. Build solution còn lỗi bộ test nhà cung cấp nêu bên dưới.

Bộ Customers.Tests gồm parser, preview/draft, hủy, bỏ phản hồi muộn, giữ trường cũ,
chặn trùng trước lưu, quyền đọc/session và Firebird database tạm. Test cạnh tranh
chạy năm cặp lưu cùng CCCD, kiểm tra chỉ một thành công và chỉ một dòng DB.
View test dựng Light/Dark, font 90/100/125/150%, bitmap DPI 100/150/200%, hai kích
thước và trạng thái trống/xem lại/trùng/lỗi; Enter được phát vào ô quét để xác nhận
chỉ đọc preview. Đây không thay thế kiểm tra HID/Unicode và DPI Windows thực tế.
Ảnh kiểm tra tùy chọn: đặt KAZUKO_CCCD_PREVIEW_DIR khi chạy test.

Không có migration mới; DATA.FDB chính không bị ghi dữ liệu thử. QR thô không được
lưu lịch sử/log; CMND cũ chỉ dùng đối chiếu trong phiên/form, không thêm cột lưu.
Độ dài hỗ trợ 7 trường đầy đủ, 6 trường không CMND cũ, 5 trường không CMND cũ/ngày cấp.
TAX_CODE 12 chữ số ASCII áp chính sách không tạo trùng; các MST độ dài khác giữ
cảnh báo hiện có. Chưa camera/OCR/NFC.

## Build và rollback

Build ứng dụng riêng: `dotnet build src/Kazuko.App/Kazuko.App.csproj --no-restore`.
Build toàn solution hiện vướng 7 lỗi có sẵn ở SupplierRepositoryTests vì thiếu
BrandCode trong constructor; các lỗi này không thuộc luồng CCCD.

Rollback bằng bản ứng dụng trước tính năng; không cần rollback schema. Hồ sơ đã
lưu là dữ liệu khách hàng thông thường và được giữ. Không tự xóa hồ sơ khi rollback.

## Sửa thông tin xem trước

Cột Từ CCCD có ô nhập cho cả sáu trường; cột Trường/Trên form giữ chỉ đọc.
Ngày nhập dd/MM/yyyy, giới tính Nam/Nữ/Khác. Bỏ trống địa chỉ/ngày cấp giữ nguyên
trường tương ứng trên form cha. Khi sửa, kết quả trùng cũ bị xóa; bấm Áp dụng
validate lại toàn bộ và tra cứu lại CCCD/CMND trước khi trả dữ liệu. Nếu trùng,
hiện lựa chọn hồ sơ đã có, không áp dụng thành hồ sơ mới. Sửa CCCD reset xác nhận
thay giấy tờ. Hủy không áp dữ liệu sửa. Test nhập trong TextBox xác nhận binding
TwoWay cập nhật giá trị thực dùng khi áp dụng.
