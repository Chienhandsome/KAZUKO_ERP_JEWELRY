# Kế hoạch nhập nhanh khách hàng bằng quét QR CCCD

Trạng thái: đã triển khai code và kiểm thử tự động ngày 2026-10-22; chờ kiểm tra trực tiếp máy quét iMarcone.

## 1. Quyết định đã chốt

- Thiết bị: máy quét iMarcone. Bản đầu dùng chế độ USB/Bluetooth HID giả lập bàn phím
  như TestScan; xác nhận thực tế suffix Enter và truyền tiếng Việt khi thử thiết bị.
- Nút **Quét CCCD** trên form thêm/sửa khách hàng dùng chung, bao gồm form mở từ bán hàng.
- Khi CCCD đã tồn tại: hiện khách hàng đã có để chọn, không tạo thêm hồ sơ trùng.
- Không triển khai camera, OCR ảnh thẻ, đọc chip/NFC hoặc tra cứu MST trong đợt này.
- Chỉ nhập nhanh vào bản nháp của form; người dùng kiểm tra, bổ sung rồi bấm Lưu.

## 2. Kết quả khảo sát

Nguồn tham chiếu: `D:/Working/TestScan/app.js`, `index.html`, `style.css`.

- TestScan nhận chuỗi QR qua ô nhập hoặc bộ đệm keydown toàn trang; Enter kết thúc,
  bộ đệm toàn trang reset sau khoảng 250 ms không có phím. Có hỗ trợ dán chuỗi.
- Parser tham chiếu dùng thứ tự 7 trường:
  `CCCD|CMND cũ|Họ tên|Ngày sinh|Giới tính|Địa chỉ thường trú|Ngày cấp`, ngày `ddMMyyyy`.
  Bản tham chiếu còn chấp nhận dạng 6/5 trường. Đây là định dạng của prototype;
  chỉ hỗ trợ biến thể đã được xác thực bằng mẫu thiết bị và test cụ thể.
- Không sao chép nguyên bộ nhận phím toàn cục, lưu lịch sử localStorage, xuất bảng
  hay phần tra cứu MST của TestScan vào ERP.
- Form hiện tại đã có Name, TaxCode (ô CMND/CCCD/MST), BirthDate, Gender, Address,
  IdIssueDate, IdIssuePlace. Phone hiện bắt buộc nhưng không có trong dữ liệu quét.
- `CustomerEditorService` / `ICustomerEditorService` được dùng lại bởi Khách hàng,
  Bán hàng và Công nợ. Tích hợp tại đây để các nơi gọi hưởng cùng tính năng.
- `FindDuplicateWarningAsync` hiện chỉ trả chuỗi cảnh báo và cho phép tiếp tục lưu;
  không đủ cho chính sách không tạo trùng CCCD. Query hiện có còn gắn company code
  KAZUKO; truy vấn nhận diện mới phải lấy công ty từ chi nhánh/phiên đăng nhập thực.
- Khách vãng lai hệ thống đang bị chặn sửa hồ sơ; quét không được mở đường bỏ qua.

## 3. Luồng sử dụng

1. Mở Thêm/Sửa khách hàng → **Quét CCCD**.
2. Hiện hộp quét nhỏ và tự focus ô nhận chuỗi. Quét iMarcone hoặc dán dữ liệu rồi
   bấm **Đọc thông tin**; Enter trong ô quét chỉ xử lý chuỗi, không lưu form cha.
3. Phân tích, kiểm tra và tra trùng. Hiện bản xem trước gồm CCCD, CMND cũ nếu có,
   họ tên, ngày sinh, giới tính, địa chỉ, ngày cấp; lỗi chỉ rõ trường cần quét lại.
4. Không có hồ sơ trùng: **Áp dụng vào form**, đóng hộp quét, focus SĐT nếu đang trống.
5. Có hồ sơ trùng: hiện mã KH, họ tên, SĐT, trạng thái. Cho **Dùng khách hàng này**
   hoặc **Mở hồ sơ để cập nhật** nếu có quyền. Không tạo hồ sơ mới mang cùng CCCD.
   Khi form được mở từ bán hàng/công nợ, trả đúng ID khách hiện có qua kết quả dùng chung.
6. Chỉ bấm **Lưu** mới ghi DB. Hủy ở bất kỳ bước nào không ghi dữ liệu nhận từ quét.

### Khi đang sửa khách hàng

- Không tự ghi đè thông tin khi vừa nhận mã. Hiện so sánh các trường khác nhau;
  người dùng chọn áp dụng, ô trống trong QR không xóa dữ liệu đang có.
- Quét đúng CCCD của khách đang sửa không bị nhận nhầm là hồ sơ trùng khác.
- Quét CCCD thuộc người khác: chặn việc gán số đó cho hồ sơ đang sửa; cho chuyển
  sang hồ sơ khớp khi người dùng chọn. Không tự đổi danh tính của hồ sơ hiện tại.
- Nếu form đã có CCCD khác nhưng mã mới chưa tồn tại: yêu cầu xem lại danh tính
  trước khi thay số; không áp dụng nền hoặc tự lưu.

### Ngoại lệ trùng dữ liệu

- Tìm theo CCCD mới và đối chiếu CMND cũ nếu QR có, để nhận ra hồ sơ lưu giấy tờ cũ.
- Nhiều hồ sơ cũ cùng khớp: hiện danh sách để chọn, không dùng SELECT FIRST 1.
- Hồ sơ ngừng hoạt động: hiện trạng thái, không tự kích hoạt; chỉ mở xử lý khi có quyền.
- Không gộp hồ sơ, chuyển chứng từ hay sửa công nợ trong tính năng này.

## 4. Ánh xạ và quy tắc dữ liệu

| Dữ liệu từ QR | Đích trong ERP | Quy tắc |
|---|---|---|
| CCCD | TaxCode / ô CMND/CCCD/MST hiện có | Giữ string và số 0 đầu; 12 chữ số cho dạng CCCD đang hỗ trợ |
| Họ tên | Name | Trim, chuẩn hóa Unicode; giữ dấu, không tự sửa tên bằng suy đoán |
| Ngày sinh | BirthDate | ParseExact ddMMyyyy, ngày tồn tại, không ở tương lai |
| Giới tính | Gender | Ánh xạ vào các giá trị form hỗ trợ; giá trị lạ phải báo để kiểm tra |
| Thường trú | Address | Kiểm tra chiều dài theo contract hiện có |
| Ngày cấp | IdIssueDate | Ngày hợp lệ; không ở tương lai hoặc trước ngày sinh |
| CMND cũ | Xem trước + dò hồ sơ cũ | Bản đầu không thêm cột hoặc tự ghép vào Ghi chú |
| SĐT, nơi cấp, email | Không có trong QR prototype | Giữ dữ liệu đã nhập; bổ sung thủ công khi cần |

- Parser tách giữ trường rỗng; không bỏ ô CMND cũ làm lệch vị trí các trường sau.
- Không dùng replace bỏ mọi ký tự không phải số để biến chuỗi sai thành CCCD hợp lệ.
- Giới hạn độ dài đầu vào và từ chối QR hàng hóa/URL/chuỗi lạ, ngày sai, thiếu tên/CCCD.
- Dạng 5/6 trường chỉ được nhận qua nhánh riêng có kiểm tra vị trí và trường bắt buộc;
  dữ liệu thiếu ngày cấp được ghi rõ, không bịa giá trị. Trường hợp không nhận diện
  chắc chắn báo không hỗ trợ, không điền từng phần ngầm.
- Không tự suy ra nơi cấp, SĐT, nhóm khách, điểm, hạn mức hoặc các dữ liệu kinh doanh khác.

## 5. Thiết kế triển khai

### Bước A — Parser độc lập và contract

- Core/Customers: thêm `CitizenIdentityScanData`, kết quả parse có lỗi theo trường,
  `CitizenIdentityQrParser` không phụ thuộc WPF/DB. Không giữ raw QR trong DTO sau parse.
- Nhận ngày hiện tại qua tham số/clock để test quy tắc thời gian ổn định.
- Bổ sung DTO kết quả đối chiếu hồ sơ và phương thức repository tra chính xác theo
  số CCCD/CMND, có session/company scope rõ ràng và bỏ qua ID đang sửa.

### Bước B — Nhận máy quét và UI

- Module Customers: hộp quét cùng ViewModel, nút mở từ CustomerEditDialog.
- Nhận nội dung bằng TextBox/TextInput để hỗ trợ dấu Việt; không ráp dữ liệu bằng
  tên phím KeyDown. KeyDown chỉ xử lý Enter/Escape khi hộp quét có focus.
- Không hook bàn phím toàn ứng dụng, không bắt nhầm QR sản phẩm hoặc nhập liệu bán hàng.
- Quét lại thay bản xem trước; chống xử lý lặp một lượt quét, không mở nhiều hộp quét.
- Hiện trạng thái Đang chờ/Đã đọc/Không hợp lệ; không báo thiết bị đã kết nối chỉ vì ô nhập có focus.
- Áp dụng vào form qua ViewModel; code-behind chỉ xử lý focus và hành vi View.

### Bước C — Trùng khách và lưu

- Infrastructure: lookup chính xác nhiều kết quả, lọc đúng doanh nghiệp và quyền.
- Dùng kết quả typed để chọn khách đang có, không phân tích chuỗi cảnh báo hiện hữu.
- Thực thi chính sách không tạo trùng CCCD ở backend khi lưu, không chỉ chặn nút UI.
  Các đường Save khách có số CCCD phải dùng chung kiểm tra này; kiểm tra lại trong
  transaction và có cơ chế tuần tự hóa theo doanh nghiệp để hai quầy không cùng tạo mới.
- Giữ cảnh báo trùng SĐT và xử lý MST hiện hữu; không thêm unique toàn cục lên cột
  TaxCode đang dùng chung cho nhiều loại giấy tờ. Rà dữ liệu cũ để quyết định cần
  index hỗ trợ hoặc cột phân loại giấy tờ trước khi chốt ràng buộc DB.
- Không xóa/gộp các bản ghi trùng cũ. Nếu phát hiện, vẫn trả danh sách để chọn và báo rõ.
- Cập nhật BackendAccess/proxy và fake/test khi thêm repository contract. Giữ quyền
  Thêm/Sửa khách hàng của các module đang gọi, không cấp quyền rộng chỉ để quét.
- Tái sử dụng các trường lưu hiện tại. Dự kiến không cần migration cho phần điền
  thông tin; nếu bước kiểm tra trùng cần thay schema/index, tạo migration mới sau
  khi đọc phiên bản DB thực, có backup/restore thử và verification riêng.

### Bước D — Tích hợp và bàn giao

- Kiểm tra form từ Danh mục KH, Bán hàng, Công nợ đều nhận thông tin và trả ID đúng.
- Dùng khách cũ phải cập nhật lựa chọn ở màn hình gọi, không để bản nháp khách mới tồn tại.
- Không cho luồng quét chuyển khách vãng lai hệ thống thành hồ sơ cá nhân.
- Không lưu lịch sử quét, ảnh CCCD hoặc raw QR; không đưa thông tin quét vào log lỗi.
  Dữ liệu form được lưu theo luồng khách hàng và audit hiện hữu, không tự tạo thêm kho dữ liệu quét.
- Cập nhật tài liệu luồng, hướng dẫn iMarcone, manual tests; build app và kiểm tra các caller.

## 6. Kiểm thử và tiêu chí nghiệm thu

**Parser:** chuỗi 7 trường đầy đủ; CMND cũ trống; biến thể đã hỗ trợ; CR/LF ở cuối;
ngày nhuận/ngày sai/tương lai; số 0 đầu; thiếu trường; ký tự lạ; Unicode tiếng Việt;
độ dài vượt giới hạn; QR sản phẩm/URL bị từ chối.

**Form:** điền đúng 6 trường; SĐT và nơi cấp đang có không mất; dữ liệu lỗi không
điền vào form; Hủy không thay đổi form/DB; quét liên tiếp không lưu hoặc áp dụng hai lần;
sửa khách có xem khác biệt trước khi thay; Enter không kích hoạt Lưu form cha.

**Trùng và backend:** mới/đã có/có CMND cũ/nhiều bản ghi/ngừng hoạt động/khác công ty;
đang sửa cùng ID; khác ID; mất quyền; khách vãng lai; hai phiên tạo đồng thời cùng CCCD
chỉ có một hồ sơ mới; không thay chính sách MST/SĐT ngoài phạm vi đã chốt.

**Thiết bị thật:** quét nhiều thẻ được phép dùng để thử bằng iMarcone USB hoặc
Bluetooth đang vận hành; xác nhận dấu Việt, số 0 đầu, Enter và tốc độ truyền. Tách
lỗi cấu hình máy quét khỏi parser, không tự bỏ dấu hoặc đoán lại dữ liệu sai mã hóa.

**Giao diện:** Light/Dark; font 90–150%; DPI 100–200%; bàn phím Tab/Enter/Escape;
địa chỉ dài có thể đọc/cuộn; không che các nút Áp dụng/Hủy.

Hoàn tất khi quét → xem lại → áp dụng → bổ sung SĐT → lưu hoạt động ở form dùng chung;
trường hợp đã tồn tại chọn lại đúng hồ sơ; không có khách mới trùng do hai lượt quét đồng thời.

## 7. Phạm vi chưa làm trong đợt này

Camera/OCR/NFC, quét từ ảnh, tra cứu MST, lưu lịch sử quét, lưu ảnh thẻ, tự gộp dữ liệu
khách và chuẩn hóa hàng loạt dữ liệu giấy tờ cũ. Chỉ bổ sung nếu có yêu cầu riêng.

## Kết quả triển khai

- Đã thêm nút Quét CCCD vào shared CustomerEditDialog và modal xem lại/đối chiếu.
- Đã thêm parser độc lập Core, query nhận diện có kiểu, guard trùng trong transaction
  và test hai quầy cùng lưu. Firebird yêu cầu CAST rõ độ dài ở tham số CMND tùy chọn;
  transaction bật Wait + RecVersion để quầy sau chờ rồi thấy hồ sơ vừa commit.
- Dùng các trường hiện có, không cần migration hoặc thay database chính.
- Hướng dẫn dùng, giới hạn, kiểm thử và rollback tại
  `docs/manual-tests/customer-cccd-scan.md`.
