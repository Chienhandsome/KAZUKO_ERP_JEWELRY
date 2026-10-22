# Kế hoạch tách cách xử lý khách vãng lai

Trạng thái: đã triển khai; xem kết quả kiểm chứng bên dưới.

## Mục tiêu và quyết định

- Giữ khách vãng lai trong `CUSTOMERS` để các chứng từ tiếp tục dùng cùng khóa ngoại.
- Thêm `IS_WALK_IN` boolean, mặc định false. Không dùng nhóm khách hoặc mã khách làm nguồn phân loại lâu dài; không thay đổi ý nghĩa cột `CUSTOMER_TYPE` hiện hữu.
- Khách có hồ sơ được tạo qua luồng hiện tại; người dùng không tự đổi loại qua form sửa khách.
- Khách vãng lai là bản ghi hệ thống: được xem giao dịch, không sửa hồ sơ hoặc ngừng hoạt động qua luồng quản lý thông thường. Chặn ở backend và UI.
- Bán hàng thanh toán đủ vẫn được dùng khách vãng lai. Ghi nợ, mua/thu đổi vàng cũ và đặt chế tác yêu cầu khách có hồ sơ, giữ quy tắc hiện tại.
- Doanh thu vãng lai vẫn thuộc tổng doanh thu, nhưng không xem là doanh số của một cá nhân trong thống kê chăm sóc khách.

## 1. Schema và chuyển dữ liệu

- Thêm migration tiến tới, lấy số version tiếp theo còn trống tại lúc triển khai (hiện cuối danh sách là 0082); không sửa migration cũ.
- Backfill `IS_WALK_IN = true` cho các mã chuẩn hóa `KH-VL`, `WALK_IN`, `KHACH_LE`; khách khác giữ false.
- Rà dữ liệu trước chuyển: nhiều bản ghi vãng lai trong cùng công ty, bản ghi đã ngừng hoạt động, công nợ/đơn đặt bất thường. Báo rõ ngoại lệ, không tự xóa, gộp, chuyển chứng từ hoặc sửa số dư.
- Giữ nguyên ID, mã và toàn bộ khóa ngoại. Luồng tạo dữ liệu ban đầu/demo gán cờ trực tiếp; quy tắc chọn khách vãng lai mặc định phải xác định, không phụ thuộc thứ tự tên nếu có nhiều bản ghi cũ.
- Thêm script verification, checksum qua runner hiện hữu và hướng dẫn phục hồi từ backup cùng phiên bản app tương ứng.

## 2. Contract và bảo vệ nghiệp vụ

- Bổ sung cờ vào DTO khách hàng và các kết quả tra cứu/chỉnh sửa cần thiết trong Core; rà tất cả nơi khởi tạo DTO, fake và fixture.
- Customers repository trả cờ; chặn lưu sửa/ngừng hoạt động với khách hệ thống, kể cả gọi trực tiếp bỏ qua UI.
- Khách tạo mới qua form luôn có `IS_WALK_IN = false`; không cho gửi request để tự tạo khách hệ thống.
- Sales query, checkout, bán sỉ, đơn đặt và lựa chọn khách trong Production đọc cờ từ database; bỏ các phép suy luận mã rải rác.
- Backend đọc lại loại khách khi ghi nghiệp vụ; không tin cờ từ UI. Giữ kiểm tra phân quyền, công ty và trạng thái hoạt động hiện hữu.

## 3. Giao diện

- Danh mục khách mặc định hiển thị khách có hồ sơ; thêm lọc `Có hồ sơ / Vãng lai / Tất cả` và nhãn `Vãng lai · Hệ thống`.
- Bản ghi hệ thống chỉ xem thông tin/lịch sử; vô hiệu hóa thao tác sửa và ngừng hoạt động, có giải thích ngay tại thao tác.
- Lookup bán hàng phân biệt rõ khách vãng lai; mua bán thường tiếp tục chọn mặc định theo quy tắc ổn định.
- Lookup cho nghiệp vụ cần hồ sơ chỉ trả khách hợp lệ; có thể mở luồng tạo khách hiện hữu rồi chọn lại. Không biến bản ghi vãng lai chung thành hồ sơ của một người.
- Dùng resource/style hiện hữu, kiểm tra theme và font scale theo quy ước dự án.

## 4. Báo cáo, thống kê và xuất dữ liệu

- Loại khách vãng lai khỏi Top doanh số, khách chưa quay lại, số lượng khách có hồ sơ và phân bố nhóm/khu vực phục vụ chăm sóc khách.
- Bổ sung tổng hợp `Bán vãng lai` theo cùng kỳ, trạng thái chứng từ và phạm vi dữ liệu với phần khách có hồ sơ: số đơn và doanh thu.
- Tổng doanh thu = doanh thu khách có hồ sơ + doanh thu vãng lai theo cùng định nghĩa; không lọc mất doanh thu khi sửa báo cáo.
- Danh sách/in/xuất danh mục khách dùng cùng bộ lọc loại khách và ghi rõ bộ lọc. Các báo cáo doanh thu chung tiếp tục tính đủ.
- Rà các truy vấn khác dùng CUSTOMERS để tránh chỗ lọc theo cờ, chỗ còn lọc theo mã.

## 5. Kiểm thử và nghiệm thu

- Migration: dữ liệu cũ được phân loại đúng, ID/lịch sử không đổi; nhận diện ngoại lệ mà không tự sửa dữ liệu nghiệp vụ.
- Backend: khách vãng lai không sửa/ngừng hoạt động; khách thường vẫn thao tác đúng quyền.
- Nghiệp vụ: bán đủ tiền thành công; ghi nợ, vàng cũ và đơn đặt bị từ chối ở backend với khách vãng lai; các luồng khách có hồ sơ vẫn chạy.
- Nhận diện: dùng bản ghi có cờ true nhưng mã khác để chứng minh logic không còn phụ thuộc ba mã cũ; khách thường không bị nhận nhầm.
- Báo cáo: dữ liệu trộn hai loại, chứng từ nháp/hủy và biên ngày; Top/CRM bỏ vãng lai, tổng doanh thu đối soát đúng và export khớp bộ lọc.
- Chạy test liên quan Customers, Sales, Production, Reports và Infrastructure, sau đó build solution và chạy bộ test theo quy trình dự án. Ghi riêng lỗi nền có sẵn nếu gặp.
- Kiểm tra UI Light/Dark, font 90–150%, kích thước cửa sổ tối thiểu và DPI phù hợp; kiểm tra trạng thái rỗng/loading/thông báo chặn.

## Thứ tự thực hiện

1. Cập nhật tài liệu quyết định, migration và Core contracts.
2. Repository/backend và các luồng Sales/Production.
3. Danh mục, lookup và báo cáo/export.
4. Test, kiểm tra UI, đối soát dữ liệu và ghi kết quả nghiệm thu.

Working tree đang có thay đổi khác, gồm database và Nhân sự. Khi triển khai phải giữ nguyên các thay đổi đó; kiểm chứng migration trên database thử nghiệm/bản sao trước.

## Kết quả triển khai 22/10/2026

- Migration 83: cờ IS_WALK_IN, backfill ba mã legacy, script preflight/verification;
  đã thử trên bản sao rồi áp dụng database phát triển. Không có ngoại lệ preflight.
  Giữ backup trong artifacts/walk-in-db/development-pre83-20261022-083923.fbk.
- Đối chiếu ID/mã/trạng thái khách, tổng số và tổng tiền đơn/thanh toán trước–sau: khớp.
- Đã cập nhật Core/Customers/Sales/Wholesale/Craft/Production/Reports và seed demo.
- Danh mục có lọc loại khách, nhãn hệ thống, khóa thao tác; cửa sổ nhỏ cuộn được.
  Lookup mua vàng cũ không còn fallback sang vãng lai khi thiếu khách có hồ sơ.
- Báo cáo CRM bỏ vãng lai; tổng hợp hai loại và export riêng dùng cùng kỳ/nguyên tắc
  của Top, tổng không giới hạn Top N. In danh mục có loại khách và bộ lọc.
- Test Customers: 23/23; Sales module: 43/43; Sales validator/calculator/contracts: 39/39
  (project kiểm tra tạm trong artifacts tái sử dụng nguyên test nguồn).
- App và DemoDataReset build không warning/error. Full solution build bị chặn bởi
  7 lỗi thiếu BrandCode trong test Suppliers hiện hữu; không sửa phạm vi Nhà cung cấp.
- 53 test tích hợp Sales cũ không chạy được vì thiếu fixture schema-v51
  sales_checkout_revised_v51_final.fdb. Test database mới xác minh mapping, bộ lọc,
  khóa backend, tạo/sửa khách có hồ sơ, lookup Sales và chặn Craft bằng cờ.
- UI đã render Light/Dark × font 90/100/125/150% × 900×520/1500×900 cho danh mục,
  công nợ, báo cáo. Ảnh ở artifacts/walk-in-ui. DPI Windows thực tế và máy in
  chưa kiểm tra trực tiếp; checklist triển khai nằm trong docs/manual-tests/walk-in-customer.md.
