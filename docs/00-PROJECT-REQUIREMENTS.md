# Yêu cầu cốt lõi của KAZUKO ERP

## Mục tiêu sản phẩm

KAZUKO ERP là ứng dụng Windows dành cho xưởng kim hoàn. Hệ thống phải dễ sử dụng ở nhiều độ phân giải, cho phép tùy biến giao diện theo người dùng và có thể nâng cấp từng nghiệp vụ mà không cài lại toàn bộ ứng dụng.

## Yêu cầu bắt buộc

### Nền tảng

- WPF trên .NET 8, target `net8.0-windows`.
- UI theo MVVM. Code-behind chỉ xử lý hành vi thuần View mà WPF binding không hỗ trợ thuận tiện; không chứa nghiệp vụ.
- Dùng constructor injection. Module không được tự tạo repository/service nghiệp vụ bên trong View hoặc ViewModel.
- Firebird là nguồn dữ liệu nghiệp vụ. Mọi thay đổi schema đi qua migration có version và checksum.

### Giao diện có thể tùy chỉnh

- Người dùng đổi được theme và cỡ chữ trong lúc ứng dụng đang chạy.
- Setting giao diện được lưu theo người dùng/máy và áp dụng trước khi hiện màn hình chính.
- Màu, brush, typography và style dùng `DynamicResource`.
- Không hard-code màu hoặc cỡ chữ trong View nghiệp vụ. Ngoại lệ chỉ dành cho hình minh họa cố định và phải có giải thích.
- Layout phải hoạt động ở font scale 90%–150%, DPI 100%–200% và kích thước cửa sổ tối thiểu đã khai báo.

### Mở rộng nghiệp vụ

- Chức năng lớn được đóng thành module triển khai `IKazukoModule`.
- Module tự đăng ký service, navigation, permission và View factory.
- Không tham chiếu trực tiếp giữa hai module nghiệp vụ. Giao tiếp qua contract/event trong `Kazuko.Core`.
- Cấu hình form động dùng metadata JSON có schema cho phép; không thực thi C#, PowerShell, SQL hoặc XAML tùy ý lấy từ database.

### Cập nhật bằng một file

- Đơn vị phát triển phát hành một tệp `.kazuko-module` cho mỗi lần cập nhật module.
- Gói phải có manifest, version, checksum, minimum host version và chữ ký số trước khi triển khai production.
- Ứng dụng phải validate → backup → stage → migrate → activate. Nếu thất bại phải rollback được.
- Không ghi đè trực tiếp module đang chạy.

### An toàn dữ liệu

- View/ViewModel không chạy SQL trực tiếp.
- Nghiệp vụ ghi nhiều bảng phải có transaction.
- Thao tác quan trọng phải ghi audit log: người dùng, thời gian, máy, hành động, bản ghi và dữ liệu trước/sau khi phù hợp.
- Audit nghiệp vụ là bắt buộc: ghi trong cùng transaction với thay đổi dữ liệu và trước commit. Không ghi được audit thì báo lỗi và rollback; không lưu dữ liệu rồi ghi nhật ký sau. Sự kiện không có thay đổi nghiệp vụ (ví dụ đăng nhập sai/từ chối quyền) dùng transaction audit riêng và cũng không được nuốt lỗi.
- Không lưu mật khẩu dạng rõ. Connection secret phải được bảo vệ bằng Windows DPAPI hoặc cơ chế tương đương.
- Không cho phép cập nhật module không có nguồn tin cậy trong production.

## Nguồn tham chiếu

Bản WinForms đã duyệt tại `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` là nguồn tham chiếu nghiệp vụ, schema Firebird và giao diện. Không sửa project đó trong quá trình port sang WPF.
