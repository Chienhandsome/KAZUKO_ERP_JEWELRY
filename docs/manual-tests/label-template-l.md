# Mẫu tem L.btw và thông tin đơn vị

1. Đóng Kazuko sau khi lưu công việc. Đọc MAX(VERSION_NO) của CSDL, xác nhận phiên bản trước migration tương ứng.
   Tạo backup `.fbk`, restore thử vào đường dẫn riêng rồi chạy migration 85 bằng
   `database/tools/Invoke-KazukoMigration.ps1` trên bản restore trước, sau đó CSDL thật. Migration 86 thêm bản ghi L.btw riêng; cũng phải backup/restore thử.
   Giữ bản backup trước 85. Kiểm tra 5 cột COMPANIES mới và ledger/checksum 85.
   Phục hồi bằng restore backup và dùng lại build trước migration.
2. Mở build mới → Cài đặt → Thông tin chung. Sửa riêng tên/địa chỉ NPP và NSX,
   xuất xứ, TCCS. Lưu, tải lại và khởi động lại: dữ liệu phải giữ đúng. Tên doanh
   nghiệp/địa chỉ chung cho bill không thay đổi theo các trường riêng trên tem.
3. Cài đặt → Mẫu in → Sửa mẫu tem → chọn **Mẫu 2 — Tem L.btw (QR)** → **Nạp mẫu**. Xem các trường đúng bố cục gốc:
   tên SP, TTL, TLĐ, TLV, gram, công, QR, HLV dọc bên trái; NPP/NSX/xuất xứ/KH/TCCS
   bên phải. Xem trước không chứa `[DataSet...]` hoặc tên/địa chỉ hard-code.
4. **In thử** một bản 203 DPI. Tắt checkbox phải ghi 0,01 chỉ; bật phải ghi 1 ly.
   Dùng hàng thật 0,01/0,1/1 chỉ; gram tương ứng 0,0375/0,375/3,75 g.
   Kiểm tra cả 0 và số lẻ 0,125 chỉ (1,25 phân khi bật). Mở lại hộp in nhớ lựa chọn.
5. QR phải quét đúng mã hàng, không phải BarcodeValue riêng. In 2 hàng khác nhau
   và nhiều bản, xác nhận từng tem đúng hàng. HLV/KH/TCCS có dấu và không chồng QR.
6. Thử tên hàng, doanh nghiệp và địa chỉ dài thường dùng. Kiểm tra chữ thực tế
   không bị cắt, chỉnh khung/cỡ chữ bằng designer nếu nội dung vượt diện tích tem.
7. Nạp mẫu 2 rồi Hủy: cả hai bản ghi còn nguyên. Nạp lại, sửa và Lưu mẫu 2:
   chỉ mẫu 2 đổi. Trong hộp in, chọn mẫu 1/mẫu 2 và xác nhận dùng đúng bản riêng.
   Mở lại hộp in phải nhớ lựa chọn lần trước. Đổi ComboBox trong designer nhưng
   chưa Nạp rồi Lưu: phải lưu vào mẫu đang sửa, không ghi đè mẫu vừa chọn. Không đổi tốc độ, độ đậm hoặc driver khi so sánh bố cục.
8. Kiểm tra Thông tin chung và hộp in ở Light/Dark, font 90/150%, DPI 100/200%:
   có thể cuộn đến các trường mới và nút lưu, checkbox đọc rõ, dùng Tab được.

Lệnh tự động:

```
dotnet test tests/Kazuko.Modules.Warehouse.Tests/Kazuko.Modules.Warehouse.Tests.csproj
dotnet test tests/Kazuko.Modules.Dashboard.Tests/Kazuko.Modules.Dashboard.Tests.csproj
```

Warehouse tests có Firebird tạm riêng, cần `KAZUKO_FIREBIRD_PASSWORD` và thư viện
Firebird (nếu cần đặt `KAZUKO_FIREBIRD_HOME`). Không tác động CSDL nghiệp vụ.
Đặt `KAZUKO_LABEL_PREVIEW_DIR` để xuất ảnh mô phỏng tem theo cả hai chế độ đơn vị.

## Kiểm tra mất nét trên mẫu QR V2

- Sao lưu/restore thử trước migration 87. Chỉ seed mẫu 2 hash V1 được cập nhật;
  mẫu 1 và mẫu 2 tự sửa/import phải giữ nguyên.
- Cùng tốc độ/độ đậm/vật liệu, in một tem mẫu 2 V2 và so với mẫu 1: NPP/NSX/HLV/
  dòng gram giữ chiều cao, không viền xám, không mất dấu hoặc tràn vào QR.
- QR 7.41 mm ở 203 DPI, mã 7 ký tự: 2 chấm/ô, quiet zone 4 ô. Quét lại đúng mã hàng.
- Thử lệch dọc lẻ (ví dụ 0.3 mm); ô QR vẫn vuông, cạnh đều, không có dải mờ.
- Mã dài không đủ 2 chấm/ô phải báo tăng khung QR. Không tự kéo giãn hoặc cắt QR.
- Test `QrPrintQualityTests` đối chiếu từng ô với bộ mã hóa gốc ở 203/300 DPI,
  kiểm tra chỉ chấm đen/trắng, font không bị giảm và Graphics không bị thay đổi sau vẽ.
