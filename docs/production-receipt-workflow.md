# Sản xuất → nhập thành phẩm (26/10/2026)

## Cách dùng

1. Tạo/sửa LSX. Khi bấm giao NVL, ứng dụng lưu lại toàn bộ bản nháp hiện tại trước khi ghi sổ giao; thiếu tồn hoặc xung đột phiên bản sẽ chặn ghi sổ.
2. Hoàn thành LSX. Trạng thái danh sách phân biệt **Hoàn thành — chờ nhập kho** và **Đã nhập kho**. Hoàn thành chưa làm tăng tồn thành phẩm.
3. Bấm **Nhập / Phiếu** trên dòng LSX, hoặc vào Kho → Nhập thành phẩm và chọn lệnh. Không tự chọn lệnh đầu tiên.
4. Chọn dòng mẫu; nhập tên, loại vàng từ danh mục, tổng/đá/vàng, giá vốn và các thông tin món. Tổng phải bằng vàng + đá với sai số tối đa 0,0001 chỉ. Tiền nguyên VND, số lượng nguyên dương. Thêm/cập nhật món vào bảng trước khi lưu phiếu. Mỗi món một mã riêng; mã trên form chỉ tạm, mã chính thức cấp khi ghi sổ.
5. Có thể **Lưu nháp** và mở lại trên cùng máy/tài khoản/chi nhánh. Đổi LSX tự lưu danh sách đã thêm thành nháp của lệnh cũ; không cho đổi nếu còn món đang gõ/sửa. Ghi chú phiếu và ghi chú món là hai trường riêng.
6. Mở **Đối chiếu vàng**, nhập hao hụt hợp lệ và chọn thợ chịu nợ nếu cần. **Xác nhận đối chiếu** chỉ chuẩn bị, chưa ghi nợ. Sửa sản phẩm/hao hụt/thợ sẽ yêu cầu xác nhận lại.
7. Ghi sổ **đủ từng dòng mẫu trong một lần**. Phiếu nhập, sản phẩm, movement, liên kết LSX, nợ vàng, snapshot và audit cùng transaction. Lỗi giữa chừng rollback toàn bộ. UI khóa nhập/đóng lúc lưu.
8. Sau ghi sổ, cửa sổ ở chế độ xem; có thể in phiếu A4 và in tem chính thức, hoặc chọn món để xem dữ liệu đã lưu. Lỗi in không ghi lại tồn. Mở lại bằng **Nhập / Phiếu** để in lại. Phiếu cũ trước schema 91 vẫn xem tại danh sách nhập kho; không có snapshot mới để mở qua màn hình này.

Nếu chưa rõ kết quả vì mất kết nối, giữ phiếu và bấm Lưu để xác minh/thử lại cùng mã yêu cầu. Nháp giữ mã yêu cầu trước khi gửi. Không lập phiếu khác để thay lần lưu chưa rõ kết quả. Sau mở lại, ưu tiên tra phiếu đã ghi sổ; nếu chưa thấy thì giữ nguyên request để thử lại.

## Giá vốn và giới hạn đã chốt

- Chưa tự phân bổ giá vốn từ LSX: cần người dùng duyệt công thức theo từng lô/nguyên liệu và tiền công trước khi triển khai. Hiện yêu cầu nhập giá vốn dương; công nhập không vượt tổng giá vốn. Không âm thầm thay giá vốn trống thành 0.
- Không hỗ trợ nhập từng đợt: số món của từng mẫu phải đủ, không chỉ tổng món của cả lệnh. Nếu cần nhận thiếu/hỏng thì phải có quyết định nghiệp vụ riêng.
- Các trường nhập được giữ trong snapshot bất biến của phiếu; thuộc tính tồn kho cốt lõi vẫn lưu FINISHED_PRODUCTS. Chưa thay công thức giá bán của phân hệ Bán hàng bằng ô giá bán nhập tay.
- Nháp lưu cục bộ theo database/chi nhánh/người dùng/LSX; chưa chia sẻ nháp giữa máy.
- File thiết kế dùng kho file cục bộ hiện có. Lưu nháp LSX chuẩn bị bản sao, giữ staging để thử lại khi rollback, hoãn xóa file cũ sau commit. Nếu mất kết nối đúng lúc commit thì giữ file mới thay vì tự xóa file có thể đã được DB tham chiếu. Sự cố tiến trình/máy ở thời điểm này có thể để lại file dư; không tự dọn file chưa xác định kết quả. Chia sẻ file thiết kế giữa máy cần kho file dùng chung riêng.

## Kiến trúc và migration

- Core: FinishedReceiptValidation, DTO gắn OrderLineId, RequestId, GoldSettlements, contract presenter/draft/documents.
- Infrastructure: transaction trong FirebirdProductionApplicationService; backend đọc lại số vàng đã giao (trừ trả lại/phế liệu thu hồi), recompute nợ từ sản phẩm thực nhập và hao hụt. Không tin số nợ từ UI. Chỉ thợ thuộc lệnh được nhận khoản nợ. Catalog active và đúng loại vàng dòng mẫu được kiểm tra lại ở backend.
- Schema **91**, `0091__production_receipt_reliability.sql`: PRODUCTION_RECEIPT_REQUESTS lưu khóa request theo công ty/chi nhánh, document duy nhất, hash nội dung gốc và snapshot toàn bộ món với mã chính thức. Không sửa nội dung migration sau khi áp dụng.
- Lưu lại cùng request/nội dung trả phiếu cũ; cùng request khác nội dung bị chặn. Hai phiên nhập cùng LSX được bảo vệ bằng trạng thái, row version và unique document nguồn, rollback phiên thua.
- Quyền ghi: đồng thời INV_FINISHED_RECEIPT:Add và PROD_GOLD_RECONCILE:Add. Đọc snapshot qua IFinishedReceiptDocuments có kiểm tra quyền xem nhập TP và scope chi nhánh.
- Rollback triển khai: dùng bản .fbk trước migration; không DROP bảng request đã có chứng từ. Lấy backup bằng gbak trước cập nhật, dùng pipeline Invoke-KazukoMigration.ps1 có checksum và snapshot phục hồi.

## Kiểm chứng

- Kazuko.Infrastructure.Tests: FinishedReceiptTests, ProductionValidatorTests (validation, file rollback/retry, scope nháp và quyền).
- Kazuko.Modules.Warehouse.Tests: nhập/đọc số khác culture, async đổi LSX, xác nhận chưa ghi nợ, khóa form, report nhiều trang được render có nội dung; layout Light/Dark ở font 90%/150%.
- Kazuko.Modules.Production.Tests: bộ in và chọn thợ hiện có.
- Kazuko.ProductionReceipt.IntegrationChecks: chỉ nhận bản restore test91_*.fdb; kiểm tra nhận thiếu/sai dòng, audit fault rollback kho/nợ, idempotency, dữ liệu snapshot, scope, và hai phiên cùng nhập một lệnh. Lệnh chuẩn bị tạo backup/restore riêng, không ghi dữ liệu mẫu vào DATA.FDB.

Chưa thử máy in vật lý hoặc mô phỏng rớt mạng đúng thời điểm commit. Các test retry/snapshot kiểm chứng cơ chế phục hồi nhưng không thay thế kiểm thử thiết bị tại cửa hàng.
