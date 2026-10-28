# Rà soát sản xuất → hoàn thành → nhập kho (26/10/2026)

**Cập nhật triển khai:** đã xử lý luồng nhập tay theo [hướng dẫn mới](production-receipt-workflow.md),
bao gồm khóa/đổi lệnh, validation, nhập đủ mẫu, transaction kho–nợ, request retry,
nháp, file rollback và in. Báo cáo bên dưới giữ nguyên làm dấu vết phát hiện trước sửa.
Giá vốn tự phân bổ và kho file dùng chung chưa triển khai; không coi là đã hoàn thành hai phần này.

Phạm vi: đọc lại luồng tạo/sửa nháp LSX, giao NVL, hoàn thành, nhập tay thành phẩm, đối chiếu vàng, file đính kèm và in. Đã chạy 12 ProductionValidatorTests và 4 test module Production (in/chọn thợ), đều qua. Chưa chạy các kịch bản gây lỗi trên database thử hoặc thao tác toàn luồng trên app; các phát hiện bên dưới dựa trên mã nguồn. Các test đã qua không xác nhận độ an toàn của nhánh nhập tay/transaction. Chưa sửa logic nghiệp vụ trong đợt rà soát này.

Luồng hiện tại: lưu nháp LSX → giao nguyên liệu → hoàn thành LSX (ghi công nợ tiền công thợ ngoài nếu có) → sang Kho / Nhập thành phẩm → nhập từng món → đối chiếu vàng/ghi nợ vàng riêng → lưu phiếu nhập. Hoàn thành LSX chưa tự tăng tồn thành phẩm.

## Cần ưu tiên

### 1. P1 — Nợ vàng có thể đã ghi dù chưa nhập kho; ghi nhiều loại bị dở dang

`ProductionFinishedReceiptDialogViewModel.RecordDebtAsync` ghi từng loại vàng bằng từng lần gọi service, trước và độc lập với `SaveAsync`. Mỗi `RecordWorkerGoldDebtAsync` commit transaction riêng. Nếu loại đầu thành công, loại sau lỗi: dữ liệu đã ghi một phần nhưng `DebtRecorded` vẫn false; bấm lại bị chặn trùng ở loại đầu nên không tiếp tục được loại sau. Nếu ghi nợ xong rồi hủy form/lưu nhập kho lỗi, nợ vẫn tồn tại. Sửa sản phẩm sau khi ghi nợ cũng không cập nhật khoản đã ghi.

Đề xuất: nút đối chiếu chỉ chuẩn bị dữ liệu; lưu phiếu nhập cùng toàn bộ đối chiếu/nợ trong một transaction, có khóa và kiểm tra trạng thái. Nếu nghiệp vụ cần ghi độc lập, phải có chứng từ đối chiếu riêng, trạng thái rõ ràng và cơ chế tiếp tục/đảo ghi, thay vì cờ trong bộ nhớ.

Nguồn: [ghi nợ trên form](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialogViewModel.cs), [service nhập kho và ghi nợ](../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs).

### 2. P1 — Đổi LSX giữ nguyên sản phẩm của lệnh trước

`SelectedOrder` chỉ gọi `LoadDetailAsync`; hàm này thay thông tin lệnh, thợ, hình và đối chiếu nhưng không xóa/cất riêng `Products`. `SaveAsync` dùng toàn bộ Products với ID lệnh đang chọn. Nhập món cho A rồi đổi sang B có thể lưu các món đó vào B.

Đề xuất: khi có sản phẩm hoặc dữ liệu đang nhập thì hỏi xác nhận đổi lệnh; xóa draft cũ hoặc lưu draft tách theo lệnh. Khóa bộ chọn lệnh trong lúc ghi sổ. Không tự chọn lệnh đầu tiên mà chưa thể hiện rõ cho người dùng.

Nguồn: [SelectedOrder, LoadDetailAsync và SaveAsync](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialogViewModel.cs).

### 3. P1 — Form vẫn cho đóng và sửa trong lúc đang ghi sổ

`SaveCommand` có chặn IsBusy nhưng `CancelCommand`, `AddCommand` không có điều kiện này; code-behind không chặn Closing khi đang lưu. Request đã chụp danh sách ở thời điểm bấm lưu, nên chỉnh tiếp có thể khác dữ liệu thực sự được ghi; đóng form không có nghĩa transaction đã hủy.

Đề xuất: khóa vùng nhập và tất cả thao tác thay đổi khi đang lưu; chặn đóng đến khi biết kết quả; thể hiện trạng thái đang ghi và số phiếu khi thành công. Với lỗi mất kết nối lúc commit, tra cứu lại kết quả theo mã yêu cầu trước khi cho thử lại, nhất là nhập không gắn LSX.

Nguồn: [command và SaveAsync](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialogViewModel.cs), [code-behind](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialog.xaml.cs).

### 4. P1 — Thiếu kiểm tra quan hệ trọng lượng, số lượng và dữ liệu tiền

UI kiểm tra vàng > 0 và tổng/đá không âm; backend `ValidateManualFinishedProductItem` chỉ kiểm tra trọng lượng không âm, chưa ràng buộc vàng + đá = tổng hoặc ít nhất vàng/đá không vượt tổng. Số lượng sai/0/âm được thay bằng 1. `TryParseMoney` bỏ mọi ký tự không phải số nên `-100` thành 100, chuỗi chữ có số có thể thành một số hợp lệ, chuỗi không hợp lệ có thể thành 0 mà không báo.

Đề xuất: báo lỗi từng ô, không tự biến dữ liệu sai thành 1/0; kiểm tra trọng lượng với sai số làm tròn được thống nhất, đồng thời ở Core/backend. Phân biệt trống với số 0 và lỗi định dạng.

Nguồn: [AddOrUpdateProduct và các parser](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialogViewModel.cs), [ValidateManualFinishedProductItem](../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs).

### 5. P1 — Một số dữ liệu hiển thị trên form chưa được lưu vào bản ghi thành phẩm

Request có giá bán, công bán, nhà cung cấp, model, ghi chú món; nhưng INSERT FINISHED_PRODUCTS của nhánh nhập tay hiện chỉ lưu một phần, không lưu các trường này. StoneWeight cũng không có cột được ghi riêng trong câu INSERT này; các chứng từ có thể suy ra từ tổng/vàng nhưng không giữ nguyên toàn bộ đầu vào. Người dùng có thể tưởng các giá trị đó đã được lưu lâu dài.

Đề xuất: đối chiếu từng ô nhập với nơi lưu và nơi đọc lại; lưu đầy đủ trường có mục đích nghiệp vụ, hoặc bỏ/đánh dấu rõ trường chỉ dùng tạm. Kiểm tra vòng nhập → đóng → mở lại, không chỉ kiểm tra thông báo lưu thành công.

Nguồn: [ManualFinishedProductItem và PostManualFinishedReceiptAsync](../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs).

## UX và khả năng phục hồi

### 6. P2 — Tùy chọn dùng chung mã cho nhiều món bị backend từ chối

`GenerateSeparateCodes = false` làm nhiều dòng có cùng ProductCode; backend chặn mọi mã trùng trong cùng phiếu. Số lượng > 1 với tùy chọn này không lưu được.

Đề xuất: nếu tồn quản lý từng serial thì mỗi món luôn có mã riêng; mã mẫu/model dùng trường khác. Nếu muốn chung mã SKU, cần tách SKU khỏi serial thay vì bỏ chặn trùng.

### 7. P2 — Bất đồng bộ đổi lệnh có thể hiển thị đối chiếu sai lệnh

`LoadDetailAsync` chỉ kiểm tra SelectedOrder sau await lấy chi tiết; sau await lấy vàng/đá đã giao không kiểm tra lại. Đổi nhanh A → B lúc tải dữ liệu có thể để phản hồi A ghi đè phần đối chiếu B.

Đề xuất: cancellation token và revision cho toàn bộ chuỗi tải, kiểm tra trước mọi lần cập nhật UI; có trạng thái tải chi tiết và khóa lưu/đối chiếu trong lúc tải.

### 8. P2 — In phiếu nhập mới có nút nhưng chưa thực hiện

`PrintReceiptCommand` chỉ hiện “Chức năng in phiếu đang phát triển”. In tem trước lưu còn dùng mã tạm; rủi ro đã in rồi lưu thất bại vì trùng mã.

Đề xuất: lưu thành công → mở phiếu đã ghi sổ → in phiếu/in tem từ bản ghi thực tế. Phân biệt in thử với in tem chính thức, hỗ trợ in lại khi máy in lỗi mà không nhập kho thêm.

### 9. P2 — Đường đi hoàn thành → nhập kho còn rời rạc

Hoàn thành ở danh sách chỉ hiện thông báo hướng dẫn rồi gọi LoadAsync (hàm này lại xóa StatusMessage), không có nút đi thẳng đến phiếu nhập đúng LSX. Cần tự sang Kho và chọn lại lệnh.

Đề xuất: trạng thái rõ “Hoàn thành — chờ nhập kho” / “Đã nhập kho”, nút “Nhập kho” mang theo ID LSX và kho dự kiến; sau lưu có liên kết phiếu nhập. Nên giữ thông báo thành công sau tải lại. Chưa cần bỏ ranh giới hai bước nếu nghiệp vụ vẫn cần kiểm nhận riêng.

Nguồn các mục 6–8: [form nhập thành phẩm](../src/Modules/Kazuko.Modules.Warehouse/ProductionFinishedReceiptDialogViewModel.cs), [backend](../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs). Mục 9: [ProductionViewModel](../src/Modules/Kazuko.Modules.Production/ProductionViewModel.cs).

## Bổ sung sau khi rà lại đầu đến cuối

### 10. P1 — Bản nháp đã có ID không tự lưu thay đổi trước khi giao NVL

`PostMaterialIssueAsync` trên form chỉ gọi `SaveDraftInternalAsync` nếu chưa có `_id`. Với nháp đã lưu: sửa thợ/mẫu/số lượng/tiền công rồi bấm giao NVL có thể ghi theo thợ và dòng kế hoạch cũ trong DB. Backend tạo QC units và snapshot tiền công từ các dòng đã lưu. Sau ghi sổ, LoadDetailAsync nạp lại dữ liệu cũ, form chuyển sang chỉ đọc; thay đổi chưa lưu bị mất.

Đề xuất: luôn lưu bản nháp hiện tại trước khi ghi sổ, giữ snapshot dòng giao NVL; hoặc chặn ghi sổ nếu còn thay đổi và hiển thị rõ bước lưu. Xử lý xung đột phiên bản trước khi xuất tồn. Phần lưu nháp và giao NVL hiện là hai transaction; nếu giữ như vậy thì thông báo rõ trường hợp nháp đã lưu nhưng giao NVL chưa thành công.

Nguồn: `ProductionOrderDialogViewModel.cs:1503`, nhánh `_id` tại 1534; backend `PostMaterialIssueAsync`.

### 11. P1 — File thiết kế/ảnh không rollback cùng DB

`SyncAttachmentsAsync` gọi xóa file thật hoặc FinalizeAsync trước audit/commit. `LocalProductionAttachmentStore.FinalizeAsync` dùng File.Move, không giữ staging. Nếu ghi SQL/audit/commit sau đó thất bại: DB rollback nhưng file cũ đã mất, hoặc file mới đã chuyển chỗ trong khi draft vẫn giữ đường dẫn staging cũ. Nhấn lưu lại có thể báo không tìm thấy staging. Các đường dẫn lưu là local AppData của máy tạo nên khả năng xem file từ máy khác cũng cần kiểm thử khi dùng DB chung.

Đề xuất: giữ staging đến khi transaction thành công; copy file mới với cơ chế dọn bù khi rollback, hoãn xóa file cũ đến sau commit. Có hàng đợi dọn file và xử lý sự cố sau commit; cân nhắc kho file dùng chung nếu triển khai nhiều máy.

Nguồn: `FirebirdProductionApplicationService.cs:1995`, `LocalProductionAttachmentStore.cs:FinalizeAsync`.

### 12. P1 — Có thể chốt nhập kho thiếu/thừa món và chốt đơn chờ giao

`PostManualFinishedReceiptAsync` không so số món/mẫu thực nhập với PRODUCTION_ORDER_LINES. Request chỉ có ID LSX và danh sách món, không có ID dòng mẫu để đối chiếu. Sau lưu bất kỳ số món dương, FINISHED_RECEIPT_DOCUMENT_ID đã được gắn, không cho nhập tiếp lệnh; đơn CUSTOM được chuyển READY. Ví dụ LSX 10 món nhưng nhập 1 món: nhánh này vẫn có thể chốt phiếu cho lệnh. Tiền công thợ ngoài đã ghi ở CompleteAsync lại dựa trên số lượng kế hoạch.

Đề xuất: nếu nghiệp vụ nhập toàn bộ một lần, kiểm tra đủ từng mẫu trước commit và hiển thị kế hoạch/thực nhập/còn thiếu. Nếu cần nhập từng đợt, phải đổi mô hình sang số đã nhận/còn lại, cho nhiều phiếu và chỉ READY khi đủ, có quyết định xử lý hụt/hỏng. Không chỉ nới điều kiện “đã có phiếu”.

Nguồn: `FirebirdProductionApplicationService.cs:1207`, `MarkCraftOrderReadyAsync:1844`, `LoadLaborTotalAsync:2471`, `ProductionContracts.cs:PostManualFinishedReceiptRequest`.

### 13. P1 — Loại vàng nhập kho chưa dùng catalog thống nhất

GoldPurityOptions là mảng cố định gồm cả 750/18K và 9999/24K; ComboBox cho nhập chữ tự do. Backend nhánh nhập tay chỉ lưu text GOLD_PURITY, không xác minh loại vàng active. Đối chiếu chỉ trim/uppercase, không quy đổi alias nên giao 750 nhưng nhập 18K thành hai dòng khác nhau; có thể báo nợ vàng sai và khó tra giá kho. Trái quy tắc GOLD_TYPES là danh mục duy nhất tại docs/06 §8.2.

Đề xuất: chọn bằng ID GOLD_TYPES active, lưu mã chuẩn; alias chỉ để tìm kiếm/hiển thị. Kiểm tra lại ở backend và migrate dữ liệu alias lịch sử theo bảng ánh xạ đã duyệt.

Nguồn: `ProductionFinishedReceiptDialogViewModel.cs:28`, `NormalizePurity`, `ProductionFinishedReceiptDialog.xaml` mục Loại vàng; backend INSERT FINISHED_PRODUCTS.

### 14. P1 — Lỗi đọc vàng đã giao có thể bị hiểu thành không có vàng giao

`GetIssuedGoldByPurityAsync` bắt mọi FbException rồi trả danh sách rỗng với chú thích hỗ trợ schema cũ. Vì vậy lỗi SQL/schema/Firebird không được truyền lên thông báo ReconcileMessage của form; form có thể dựng đối chiếu với lượng giao 0. Đó là dữ liệu “không biết”, không phải số 0. Query đá cũng có mẫu tương tự.

Đề xuất: chỉ xử lý tương thích đúng mã lỗi thiếu bảng nếu thực sự cần; các lỗi còn lại báo không tải được. Khóa ghi nợ/hoàn tất đối chiếu khi dữ liệu nguồn chưa tải thành công, có nút thử lại. Không dùng rỗng thay trạng thái lỗi.

Nguồn: `FirebirdProductionQueryRepository.cs:706` và catch tại 736.

### 15. P2 — Dữ liệu đang gõ/chỉnh món chưa được đưa vào bảng có thể bị bỏ qua khi lưu

`SaveAsync` chỉ lấy Products.ToList(), không kiểm tra đang sửa món (`IsEditing`) hoặc các ô còn dữ liệu chưa bấm Thêm/Cập nhật. Có sẵn một món trong bảng, gõ món tiếp theo rồi bấm lưu phiếu: chỉ món cũ được lưu. Lưu thành công đóng form; món đang gõ mất. ClearForm cũng xóa FormNotes, còn SaveAsync dùng chính FormNotes làm ghi chú phiếu, dễ lẫn ghi chú từng món với phiếu.

Đề xuất: tách ghi chú phiếu/món; trước khi lưu phải cập nhật món đang sửa hoặc yêu cầu hoàn thành/hủy phần đang nhập. Hiển thị số món sẽ ghi sổ, lưu nháp nhập kho và xác nhận bỏ thay đổi khi đóng; form LSX cũng cần kiểm tra thay đổi chưa lưu.

Nguồn: `ProductionFinishedReceiptDialogViewModel.cs:1041`, `AddOrUpdateProduct`, `ClearForm`.

### 16. P1 — Giá vốn bỏ trống thành 0, chưa đối chiếu chi phí LSX

Nhánh nhập tay dùng `item.CostPrice ?? 0`; công nhập có thể lớn hơn giá vốn, nhưng materialCost bị Math.Max về 0, TOTAL_COST vẫn giữ costPrice nên tổng cấu phần không khớp. Không phân bổ giá nguyên liệu đã giao hoặc công nợ tiền công của LSX vào các món. Đây không phải bằng chứng rằng phải tự tính một công thức cụ thể: cần chốt nghiệp vụ giá vốn nhập tay hay phân bổ từ LSX, và quy tắc khi nhận thiếu/hỏng.

Đề xuất: hiển thị giá vốn dự kiến từ snapshot nguyên liệu/công, cảnh báo chênh lệch; nếu cho sửa thì yêu cầu quyền/lý do và bảo toàn phương trình tổng chi phí. Không âm thầm cho tồn kho giá 0 khi thiếu thông tin.

Nguồn: `FirebirdProductionApplicationService.cs:1318`, INSERT FINISHED_PRODUCTS và InsertDocumentLineAsync ngay sau đó.

## Những bảo vệ đã có, cần giữ

- Phiếu nhập, thành phẩm, movement, gắn phiếu vào LSX và audit cùng transaction; lỗi rollback.
- Nhập gắn LSX chỉ cho lệnh COMPLETED chưa có phiếu nhập; UPDATE cuối kiểm tra ROW_VERSION và FINISHED_RECEIPT_DOCUMENT_ID IS NULL để chống hai phiên cùng nhập một lệnh.
- CompleteAsync có kiểm tra phiên bản/trạng thái và xử lý lệnh đã hoàn thành; công nợ tiền công nằm cùng transaction hoàn thành.

## Thứ tự đề nghị

1. Bịt sai dữ liệu và chịu lỗi: mục 1–5, 7, 10–14, 16; thêm kiểm thử lỗi giữa chừng, đổi lệnh nhanh, đóng form lúc lưu, thử lại sau commit không rõ kết quả. Chốt quy tắc nhận thiếu/nhận từng đợt và tính giá vốn trước khi sửa các mục 12/16.
2. Hoàn thiện thao tác: mục 6, 8, 9, 15; in dựa trên chứng từ đã lưu, thêm đường dẫn trực tiếp và bản nháp nhập kho.
3. Kiểm thử trên bản DB riêng: hai máy cùng nhập một LSX, lỗi audit/DB giữa transaction, lỗi in, mở lại chứng từ đối chiếu dữ liệu. Không dùng database đang vận hành cho thử lỗi.
