# Sửa lỗi mất kỳ chấm công

## Nguyên nhân

Truy vấn danh sách dùng alias Year/Month không quote, gây Firebird SQL -104.
Nút Thêm/Xóa kỳ cũ chỉ đổi collection trong ViewModel, không ghi database.

## Triển khai

- Giữ backup gbak trước migration 84 cùng build cũ; áp dụng bằng
  database/tools/Invoke-KazukoMigration.ps1 với 0084__attendance_periods.sql.
- Chạy thử trên database clone trước. Migration backfill tháng có chấm công,
  không sửa HR_ATTENDANCE_RECORDS hoặc các bảng HR_PAYROLL_*.
- Ngừng phiên app khi triển khai rồi dùng build có contract kỳ chấm công mới.
- Khôi phục: ngừng app, restore bản gbak trước 84 sang file mới, xác minh và
  dùng build tương ứng; không sửa migration đã ghi checksum hoặc drop cột/bảng
  trên database đã có giao dịch mới.

## Nghiệm thu

1. Danh sách kỳ tải thành công khi chưa có record nào.
2. Thêm kỳ, chưa chấm ô nào; Làm mới và mở lại app vẫn thấy kỳ đó.
3. Lỗi lưu hoặc thiếu quyền không thêm kỳ giả vào danh sách; lỗi tải có thể thử lại.
4. Gán Đi làm cho một ô; tải lại vẫn giữ; backend từ chối xóa kỳ có record.
5. Đặt ô cuối về Không có lịch: kỳ vẫn còn; xóa kỳ trống rồi mở lại không xuất hiện.
6. Hai lần tạo cùng tháng không tạo hai header; không ảnh hưởng công ty khác.
7. Xác minh lưu/tải lại bản nháp bảng lương, điều chỉnh, chốt/mở chốt và audit.

## Kiểm tra tự động

- tests/Kazuko.Modules.HumanResources.Tests: unit/UI state gồm ba test persistence.
- tests/Kazuko.Payroll.IntegrationChecks: backup/clone database, chạy migration,
  tạo kỳ trống, mở lại repository, chặn xóa kỳ có công và toàn bộ luồng payroll.
  Công cụ chỉ thay đổi database bản sao.

## Kết quả 22/10/2026

- Tái hiện SQL -104 với alias Year không quote trước khi sửa.
- 50 test Nhân sự đi qua; app build không warning/error.
- Bài kiểm tra trên database clone: tạo kỳ trống/lặp lại, tải lại bằng repository mới,
  lưu và xóa trạng thái, chặn xóa kỳ có record, xóa kỳ trống, phân quyền đều qua.
- Bảng lương: tính/lưu/tải lại, giữ điều chỉnh khi tính lại, revision, chặn chốt nguồn
  đã đổi, snapshot sau chốt, mở chốt và audit đều qua.
- Database phát triển đã áp dụng migration 84. Đối chiếu records chấm công và
  header/tổng dòng lương trước–sau không đổi. Backup giữ tại
  artifacts/attendance-fix/development-pre84-20261022-085657.fbk.
- Kỳ trống đã tạo bằng bản cũ chưa từng lưu database không có dữ liệu để tự khôi
  phục; cần tạo lại kỳ đó một lần bằng bản mới.
