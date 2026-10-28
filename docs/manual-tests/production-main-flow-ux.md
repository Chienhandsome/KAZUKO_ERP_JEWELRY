# Checklist manual — LSX → nhập TP → đối chiếu → giao hàng

Ngày: 28/10/2026. Ghi người kiểm thử, bản build, database, theme/font/DPI và mã LSX/đơn/phiếu dùng trong mỗi ca. Các ô chưa đánh dấu là chưa chạy, không coi là đã pass.

## Chuẩn bị

- [ ] Dùng bản database restore riêng; thử lỗi mạng, giao lại hoặc thay đổi dữ liệu mẫu chỉ trên bản thử. Không dùng DATA.FDB vận hành.
- [ ] Có tài khoản đủ quyền; thêm tài khoản không có CRAFT_ORDER và tài khoản chỉ xem đơn đặt để kiểm tra quyền.
- [ ] Chuẩn bị LSX nhập kho không gắn đơn, LSX theo đơn đặt đã cọc, LSX nội bộ hai thợ và LSX thợ ngoài.
- [ ] Đơn thử có 2 mẫu: A × 2, B × 1; có NVL đủ tồn, loại vàng active, kho TP, quỹ và tài khoản ngân hàng.
- [ ] Ghi số dư tồn NVL/TP, nợ vàng, tiền cọc và phần còn trước thử.

## Tạo lệnh và giao NVL

- [ ] Tạo LSX, chọn đúng nguồn và thợ. Lưu nháp, mở lại: mẫu/số lượng/thợ/ảnh giữ nguyên.
- [ ] Sửa nháp đã lưu rồi giao NVL: kế hoạch và thợ mới được lưu trước khi xuất; tồn giảm đúng một lần.
- [ ] Thử thiếu tồn: không xuất một phần; nháp đã lưu vẫn mở được để sửa.
- [ ] Hoàn thành: lệnh chờ nhập kho, chưa tăng tồn TP; công nợ tiền công thợ ngoài (nếu có) chỉ sinh một lần.

## Nhập danh sách TP và tiến độ mẫu

- [ ] Mở từ “Nhập / Phiếu” và từ Kho → Nhập thành phẩm: hai đường đều có tiến độ, kiểm tra kết quả và mở đơn.
- [ ] Mở bảng “Tiến độ từng mẫu”: đơn A2/B1, SX A2/B1; nhập A1 thì A đã nhập 1, thiếu 1; B thiếu 1.
- [ ] Thêm/sửa/xóa món: bảng tiến độ cập nhật ngay, đếm đúng mẫu chứ không chỉ tổng.
- [ ] Trên LSX nhập kho không gắn đơn: cột Đơn hiển thị “—”.
- [ ] Trên dữ liệu thử đã lệch đơn/SX: hiển thị “Lệch đơn đặt”, gồm cả mẫu đơn không còn trong LSX. Đây là cảnh báo UX; backend đồng bộ kế hoạch chưa được bổ sung trong thay đổi này.
- [ ] Ghi sổ thiếu A nhưng đủ tổng món bằng B: bị chặn, chưa thay đổi tồn/nợ.
- [ ] Món còn đang gõ/chỉnh chưa thêm vào bảng: lưu nháp/ghi sổ yêu cầu thêm hoặc hủy món, không âm thầm bỏ qua.

## Đối chiếu vàng

- [ ] LSX hai thợ: chưa tự chọn người chịu nợ. Có nợ mà chưa chọn thợ → không xác nhận được.
- [ ] Chọn thợ B, xác nhận: tên B hiển thị trong xác nhận ghi sổ. Đổi thợ/sửa món/hao hụt → phải xác nhận lại.
- [ ] LSX một thợ/thợ ngoài: đúng người được điền sẵn; không lẫn tên thợ giữa hai LSX.
- [ ] Hao hụt đã báo được điền sẵn; giá trị âm, sai định dạng hoặc vượt chênh lệch bị chặn.
- [ ] Ví dụ giao 10, nhận 9, hao hụt 0,2: nợ 0,8 chỉ, đúng loại vàng và đúng thợ.
- [ ] Nhận vượt giao: có cảnh báo trong đối chiếu và xác nhận cuối; không tạo phiếu/tăng tồn NVL.
- [ ] Chỉ xác nhận đối chiếu rồi đóng/lưu nháp: chưa có nợ vàng, chưa có tồn TP mới.

## Ghi sổ, kiểm tra kết quả và in

- [ ] Xác nhận cuối có mã LSX, kho, số món, tổng giá vốn, tiến độ mẫu, vàng giao/nhận/hao hụt/nợ và thợ. Chọn Không → chưa ghi gì.
- [ ] Chọn Có: một phiếu nhập, mỗi món một serial, nợ vàng một lần; form chuyển xem, không sửa hoặc ghi lại.
- [ ] “Kiểm tra kết quả” sau lưu trả đúng mã phiếu và món đã lưu; số phiếu/tồn/nợ không tăng.
- [ ] Tạm mất kết nối lúc kiểm tra: báo chưa xác minh; giữ dữ liệu nháp. Kết nối lại, bấm kiểm tra: đọc được phiếu hoặc báo chưa tìm thấy.
- [ ] Nếu mất phản hồi lúc ghi sổ: không lập phiếu mới; bấm kiểm tra. Chỉ thử lại cùng yêu cầu khi chưa tìm thấy. Xác nhận cuối cùng không trùng tồn/nợ/serial.
- [ ] Đóng/mở lại phiếu đã lưu: xem/in lại đúng dữ liệu; lỗi máy in không ghi thêm tồn.

## Giao hàng nếu có đơn đặt

- [ ] Trước ghi sổ hoặc LSX không gắn đơn: “Mở đơn / Giao hàng” không thực hiện được.
- [ ] Sau ghi sổ: nút mở đúng mã đơn, đúng khách, đúng cọc/phần còn và mẫu; chưa tự xuất/thu tiền.
- [ ] Tài khoản không có quyền xem đơn: không mở được. Chỉ xem đơn: không giao/thu được.
- [ ] Giao đủ A2/B1 từ cùng kho, thu phần còn bằng tiền mặt/chuyển khoản hoặc công nợ được duyệt: xuất đủ serial và thanh toán trong một lần.
- [ ] Thiếu món, món khác đơn, sai kho hoặc lỗi quỹ: báo lỗi; không để xuất kho/thu tiền một phần.
- [ ] Sau lỗi mạng khi giao: bấm “Kiểm tra kết quả”; trạng thái và số tiền được đọc lại. Nếu đã xuất, nút giao bị khóa; không thu/giao lại.
- [ ] Đơn có hàng ở nhiều kho: thông báo tập trung đủ món, không tự chọn một nhóm để giao thiếu.

## Giao diện

- [ ] Light/Dark, font 90%/100%/125%/150%, DPI 100%/150%/200%; thử cửa sổ nhỏ nhất và maximized.
- [ ] Mở/đóng bảng tiến độ, nhiều mẫu và tên tiếng Việt dài: bảng cuộn, nút ghi sổ/đối chiếu không bị đẩy ngoài cửa sổ.
- [ ] Tab/Shift+Tab, Enter, Escape, F1/F2 và Ctrl+P hoạt động; Enter trong xác nhận không bỏ qua chọn thợ.
- [ ] Khi đang tải/lưu/kiểm tra: không đóng hoặc gửi giao dịch trùng; khi hoàn tất trạng thái nút cập nhật lại.

## Kết quả

Ghi PASS/FAIL theo từng ca, ảnh chụp lỗi, mã LSX/đơn/phiếu, số dư trước–sau và lỗi thực tế. Phiếu TP đã ghi sổ chưa hỗ trợ sửa/đảo trực tiếp; sai dữ liệu phải ghi nhận để xử lý bằng quy trình riêng. Kiểm thử mạng đúng thời điểm commit và máy in vật lý cần chạy thủ công.
