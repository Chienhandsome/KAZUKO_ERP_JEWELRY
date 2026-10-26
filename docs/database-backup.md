# Sao lưu và kiểm tra bản sao lưu — giai đoạn 1

## Luồng sử dụng

Cài đặt → Cơ sở dữ liệu → **Sao lưu…**: chọn thư mục trên máy đang chạy app. Bản sao lưu logical Firebird có đuôi `.fbk`. Giao diện hiển thị giai đoạn đang chạy, có nút hủy, lịch sử thành công gần nhất và lỗi tác vụ gần nhất. **Tải lại** cập nhật kết quả của tác vụ tự động chạy nền khi khởi động. Xem lịch sử cần SYS_DATABASE/Xem; sao lưu/kiểm tra cần SYS_DATABASE/Sửa ở cả UI và backend.

**Kiểm tra bản sao lưu…** chọn `.fbk`, khôi phục bằng `gbak -create_database` vào `%TEMP%/KazukoBackupVerify/{guid}/verify.fdb`, kiểm tra SCHEMA_MIGRATIONS và COMPANIES, đóng kết nối rồi xóa database tạm. Không nhận đường dẫn database đích từ người dùng, không dùng `-replace`, không đổi connection profile và không khôi phục đè lên database đang dùng. Kiểm tra này xác nhận khả năng khôi phục và schema KAZUKO cơ bản, không thay thế kiểm toán toàn bộ nghiệp vụ/dữ liệu.

## Cách thực hiện

- Embedded: dùng dịch vụ logical backup `FbBackup` của engine Firebird đang nạp trong tiến trình app. Không khởi chạy một engine khác để mở lại file đang bị Embedded Super giữ độc quyền. Kết quả là `.fbk` tương thích `gbak`.
- Build/publish sao chép toàn bộ bundle Firebird được cung cấp, gồm `gbak.exe` và các thư mục phụ thuộc; không chỉ sao chép DLL rồi bỏ thiếu công cụ backup/restore.
- Server: chạy `gbak.exe -backup_database host/port:database output.partial` từ bundle Firebird. Không dùng `-service` để bảo đảm file output nằm trên máy người dùng, không phải ổ đĩa máy chủ. DatabasePath là đường dẫn/alias trên server, không kiểm tra File.Exists trên client.
- Mật khẩu chỉ truyền cho process con qua ISC_PASSWORD, không qua command line, file cấu hình hoặc log. Diagnostics được loại bỏ mật khẩu và giới hạn độ dài. Process không mở cửa sổ terminal.
- Chỉ đổi tên `.partial` thành `.fbk` khi tác vụ thành công và file không rỗng. Lỗi/hủy dọn file tạm, giữ bản thành công cũ. Không đặt tên đích trùng và không ghi đè backup có sẵn.
- Server/restore: hủy kết thúc process con và chờ process thoát trước khi dọn file. Embedded: chờ dịch vụ backup kết thúc an toàn, sau đó hủy kết quả và dọn file; không đóng engine đang phục vụ các màn hình khác.
- Một tác vụ backup/kiểm tra chạy tại một thời điểm trong mỗi tiến trình. Bấm trùng/tác vụ trùng bị từ chối rõ ràng.

## Tự động và lịch sử

Khi bật tùy chọn, khởi động app sẽ backup vào `%LocalAppData%/KazukoERP/backups/{database-key}/files/`. Khóa phân biệt Embedded/Server, host, port, database và tài khoản; không chứa mật khẩu. Lịch sử `status.json` nằm cạnh thư mục files, được thay atomic, giữ lần thành công gần nhất khi tác vụ mới lỗi. Lịch sử nằm trên từng máy/tài khoản Windows; không ghi vào database nghiệp vụ.

Giữ 7 bản `.fbk` tự động gần nhất của từng database; chỉ dọn sau khi bản mới thành công. Không dọn bản thủ công, backup database khác hoặc `.fdb` cũ. Bản cũ bị khóa có thể được dọn ở lần thành công tiếp theo. Backup tự động lỗi không chặn đăng nhập; lỗi được ghi vào lịch sử để xem ở Cài đặt.

Phạm vi giai đoạn 1 không thay luồng **Tạo DB trắng**, **Đổi database**, không có khôi phục database vận hành. Không cần migration. Không tự đổi các `.fdb` backup cũ thành `.fbk`.

## Kiểm thử

- Unit: `dotnet test tests/Kazuko.Infrastructure.Tests --filter FullyQualifiedName~FirebirdBackupTests` — lỗi/hủy/file tạm, giữ bản cũ, retention, đường dẫn server, thao tác đồng thời, đích lỗi và thiếu gbak.
- Integration: `dotnet run --project tests/Kazuko.Backup.IntegrationChecks -- <thư mục thử>` — tự tạo database riêng, backup online Embedded và Server, khôi phục thử, từ chối file hỏng, kiểm tra nguồn không đổi và thử lại sau lỗi. Cần bundle Firebird, cấu hình server và mật khẩu qua môi trường. Không dùng DATA.FDB chính.
- UI: kiểm tra theme sáng/tối, cỡ chữ 90–150%, đường dẫn dài, hiển thị lịch sử/lỗi, đang chạy/hủy và quyền truy cập.

Tham chiếu kỹ thuật: [Firebird gbak](https://www.firebirdsql.org/file/documentation/html/en/firebirddocs/gbak/firebird-gbak.html), [FbBackup của Firebird .NET Provider](https://github.com/FirebirdSQL/NETProvider/blob/master/src/FirebirdSql.Data.FirebirdClient/Services/FbBackup.cs).
