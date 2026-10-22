# Khách vãng lai — triển khai và nghiệm thu

## Migration 83

1. Backup bằng gbak và giữ build ứng dụng tương ứng. Chạy script chỉ đọc
   database/verification/verify_walk_in_customer_preflight.sql trên database trước 83.
   Rà các mã vãng lai trùng trong công ty, bản ghi ngừng hoạt động và công nợ/đơn đặt.
   Không tự sửa, gộp, chuyển lịch sử hay số dư; báo ngoại lệ để xử lý nghiệp vụ riêng.
2. Chạy migration trên bản sao bằng database/tools/Invoke-KazukoMigration.ps1,
   truyền MigrationPath database/migrations/0083__walk_in_customer.sql và Database
   trỏ tới bản sao; runner ghi SHA-256 toàn artifact và kiểm tra version trước đó.
3. Chạy database/verification/verify_walk_in_customer_schema.sql; đối chiếu ID/mã,
   trạng thái khách, số lượng chứng từ và tổng tiền trước/sau.
4. Dừng phiên app dùng database khi triển khai. Chạy cùng runner với database đích
   và triển khai build có hỗ trợ cờ sau khi migration thành công. Không chạy app mới
   trên schema trước 83. Giữ backup riêng ngoài backup tạm của runner.
5. Nếu cần khôi phục: ngừng app, restore backup trước 83 bằng gbak sang file mới,
   xác minh, trỏ cấu hình về file phục hồi và chạy build tương ứng. Không sửa checksum
   hoặc xóa ledger để chạy lại; không drop cột trên database đã phát sinh giao dịch mới.

## Kiểm tra hành vi

- Danh mục mặc định Có hồ sơ; Vãng lai/Tất cả trả đúng khách và nhãn Hệ thống.
- Chọn khách hệ thống: nút sửa/ngừng/xóa bị khóa, tooltip nêu lý do; lịch sử vẫn xem được.
- Gọi Save/SetActive backend với ID khách hệ thống phải bị từ chối.
- Bán hàng thanh toán đủ dùng vãng lai; ghi nợ, vàng cũ và đơn đặt bị chặn ở backend.
- Đổi mã trong dữ liệu thử nghiệm không làm mất nhận diện theo cờ; khách thường
  có mã legacy nhưng cờ false trong fixture vẫn được xử lý theo cờ.
- Top/khách chưa quay lại/phân bố không có vãng lai. Tổng hợp cùng kỳ có hai dòng,
  tổng hai dòng khớp truy vấn doanh thu không giới hạn Top N. Nháp/hủy bị loại.
- Export tổng hợp/danh mục khớp dữ liệu đã tải; bản in ghi bộ lọc loại khách và cột loại.
- Light/Dark, font 90/100/125/150%, cửa sổ 900×520 và 1500×900; kiểm tra DPI
  100/150/200%, keyboard, rỗng/loading và thông báo lỗi.

## Bản ghi hệ thống ngoại lệ

Migration giữ nguyên trạng thái hoạt động và nhiều bản ghi legacy. Lookup bán hàng
ưu tiên vãng lai đang hoạt động có ID nhỏ nhất; không tạo ngầm hoặc kích hoạt lại
khách. Nếu không có khách vãng lai hoạt động, quản trị viên cần kiểm tra dữ liệu
qua quy trình quản trị trước khi sử dụng chế độ vãng lai.
