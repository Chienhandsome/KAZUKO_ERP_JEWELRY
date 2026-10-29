# GĐ2 — Rà code dòng tiền, công nợ và đường đảo

Ngày: **28/10/2026**. Code khảo sát: **f0133f79bee38397530cd0270298617c5fc00958**.

**Cập nhật sau kiểm chứng GĐ3:** xem [kết quả runtime](2026-10-28-money-flow-gd3-runtime-checks.md). Thu theo đơn bằng nhiều phương thức bị `CK_ORDER_PAYMENT_METHOD` chặn và rollback; MF-01 được tái hiện ở receivable mixed và hai bank cùng phương thức, không coi giao dịch đơn mixed đã rollback là tiền bị thiếu số dư. GĐ3 phát hiện thêm MF-11–MF-13. Nhóm unit trước đây timeout đã chạy lại ngoài sandbox: 33 passed, 0 failed, 0 skipped.

Tiếp nối [bản đồ GĐ1](2026-10-28-money-flow-map.md). Đã rà tĩnh cả 9 nhóm G2-01–G2-09, ưu tiên posting → hủy → công nợ khách, sau đó kho/sản xuất/HR. Báo cáo này ghi **10 lỗi xác nhận từ code/schema trong repository**, cùng các giới hạn và ca cần kiểm chứng runtime. “Xác nhận từ code” không có nghĩa đã tái hiện trên Firebird hoặc đã tìm thấy giao dịch thật bị ảnh hưởng.

Không truy vấn DB, không sửa code nghiệp vụ, migration hoặc dữ liệu. Các số tiền ví dụ dưới đây là phản ví dụ tính từ code, không phải số liệu vận hành. Chưa thực hiện GĐ3/GĐ4, chưa xác định thất thoát thực tế. P1/P2 là mức ưu tiên xử lý: P1 có thể làm sai tiền/công nợ hoặc chặn phương thức thanh toán đang được hỗ trợ; P2 ảnh hưởng truy vết hoặc quy trình phụ thuộc.

## 1. Kết quả cần xử lý

| ID | Ưu tiên | Kết luận | Loại ảnh hưởng |
| --- | --- | --- | --- |
| MF-01 | P1 | Dashboard bỏ sót hoặc gán sai tiền của phiếu bulk theo quỹ/tài khoản | Sai số dư báo cáo; allocations vẫn giữ số tiền |
| MF-02 | P1 | Hủy riêng phiếu nghiệp vụ ngoài HR không hoàn tác nguồn | Phiếu tiền và sổ nguồn mất đồng bộ |
| MF-03 | P1 | Nợ cùng đơn được biểu diễn hai lần và thu qua hai sổ độc lập | Cộng trùng phải thu; có thể ghi thu hai lần cùng nghĩa vụ |
| MF-04 | P1 | Đường thu theo đơn không trừ bù trừ vàng cũ | Hiện/cho thu vượt phần còn nợ thực theo checkout |
| MF-05 | P1 | Đảo settlement theo mã tham chiếu bỏ sót payment có reference riêng | Phiếu hủy nhưng đơn vẫn được coi đã thanh toán |
| MF-06 | P1 | Migration 74 loại CARD khỏi CASH_VOUCHERS | Phiếu CARD đơn phương thức bị DB từ chối trên schema theo migrations |
| MF-07 | P1 | Các helper tất toán theo ID thiếu kiểm tra scope đích | Có thể ghi tiền tại chi nhánh hiện tại cho nợ/đơn chi nhánh khác |
| MF-08 | P2 | Hủy phiếu chung không ghi audit nghiệp vụ | Thiếu nhật ký theo yêu cầu dự án |
| MF-09 | P1 | Phiếu thủ công bỏ lựa chọn quỹ/ngân hàng; UI gửi MIXED mà service từ chối | Ghi sai nơi giữ tiền hoặc không lưu được phiếu nhiều phương thức |
| MF-10 | P2 | Đảo settlement trả nợ không vô hiệu dấu vết payment mà guard đảo kho vẫn đếm | Đã đảo tiền nhưng phiếu nhập có thể tiếp tục bị chặn đảo |

### MF-01 — Số dư từ header không phù hợp phiếu bulk

**Chuỗi bằng chứng:** [bulk posting](../../src/Kazuko.Infrastructure/Finance/FirebirdFinancialVoucherPostingService.cs#L186) lấy `MIXED` nếu nhiều phương thức và để fund/bank null; nếu cùng phương thức thì lấy fund/bank của allocation đầu. [Settlement bulk](../../src/Kazuko.Infrastructure/Finance/FirebirdFinancialSettlementService.cs#L138) liên kết mọi allocation vào cùng một voucher. [KPI](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceDashboardRepository.cs#L73) chỉ tính tiền có fund/bank trên header; [số dư từng nơi](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceDashboardRepository.cs#L207) join trực tiếp header, không đọc allocations.

- Thu 10 triệu, tiền mặt 4 triệu + chuyển khoản 6 triệu trên **cùng một dòng nợ**: voucher tổng IN 10 triệu, MIXED/null/null. Tổng thu tăng 10 triệu nhưng KPI quỹ/ngân hàng và số dư từng nơi không tăng.
- Hai allocation chuyển khoản A=4 triệu, B=6 triệu: header ghi cả 10 triệu vào A, B không tăng. [Validator](../../src/Kazuko.Core/Finance/PaymentAllocationValidator.cs#L7) kiểm tra từng dòng nhưng không buộc cùng phương thức phải cùng tài khoản. Đây là đầu vào hợp lệ của API bulk; dialog tất toán hiện chỉ chọn một quỹ và một tài khoản nên chưa xác lập đường UI tạo hai ngân hàng trong một request.
- Trường hợp mixed **đi được từ UI hiện tại**: [CustomerViewModel](../../src/Modules/Kazuko.Modules.Customers/CustomerViewModel.cs#L598) / [DebtViewModel](../../src/Modules/Kazuko.Modules.Debt/DebtViewModel.cs#L858) → `SettleDebtAllocationsAsync` → [chia phương thức theo dòng](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1183) → helper bulk. Nếu tiền mặt và ngân hàng rơi vào hai dòng nợ khác nhau thì mỗi phiếu có thể đơn phương thức; không mặc định mọi request mixed đều sinh phiếu MIXED.

**Hướng sửa để xem xét:** tính phân bổ nơi giữ tiền từ allocation của voucher liên kết; dùng header cho voucher không có allocation settlement. Tổng thu/chi vẫn đếm voucher một lần, không nhân theo số allocations. Cần giữ tiền ở quỹ/tài khoản ngừng hoạt động trong đối soát hoặc hiện ngoại lệ: danh sách hiện lọc `IS_ACTIVE=TRUE`, còn KPI không áp filter này.

### MF-02 — Hủy riêng voucher để lại công nợ/thanh toán nguồn hiệu lực

[VoucherListViewModel](../../src/Modules/Kazuko.Modules.Finance/VoucherListViewModel.cs#L220) cho hủy mọi detail POSTED. [CancelVoucherAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs#L153) chỉ chặn `HR_AWARD`/`HR_PAYROLL`; sau đó chỉ đổi `CASH_VOUCHERS`, không đảo settlement, ORDER_PAYMENTS, collection, payable hoặc stock payment.

Ví dụ thu nợ 5 triệu → ORDER_PAYMENTS và settlement ghi nhận thu → hủy phiếu từ tab Thu: dashboard loại 5 triệu nhưng đơn vẫn trừ nợ. Trả payable gặp tình trạng tương tự theo chiều chi. Đây là lỗi đồng bộ sổ, chưa phải bằng chứng tiền thật bị lấy mất.

Quyền hủy **có được kiểm tra**: [XAML](../../src/Modules/Kazuko.Modules.Finance/VoucherListView.xaml#L179) và [BackendAccess](../../src/Kazuko.Infrastructure/UserManagement/BackendAccess.cs#L125) yêu cầu `FIN_REVERSE/Delete`. Vấn đề là quyền đó đang cho thực hiện sai phạm vi owner, không phải thiếu toàn bộ phân quyền.

**Hướng sửa:** chỉ cho hủy riêng nguồn thủ công hợp lệ; nguồn liên kết phải chuyển về owner đảo đầy đủ hoặc bị chặn với hướng dẫn. Không tự gọi đảo kho/checkout chỉ dựa vào một phiếu tổng vì có nhiều phụ thuộc.

### MF-03 — Cùng nghĩa vụ bán hàng bị cộng và thu ở hai sổ

Bán sỉ tạo `RECEIVABLE_DEBTS` nguồn `SALES_ORDER`, số gốc bằng phần chưa trả: [code](../../src/Kazuko.Infrastructure/Sales/FirebirdWholesaleSalesApplicationService.cs#L307). Checkout [cũng tạo như vậy](../../src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs#L624); đơn CUSTOM [có đường tương tự](../../src/Kazuko.Infrastructure/Sales/FirebirdCraftOrderApplicationService.cs#L671).

[SearchCustomerDebtsAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L78) tính nợ từ tổng đơn trừ payments/offset, rồi [cộng thêm remaining receivable](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L157), không loại `ORIGIN_DOCUMENT_TYPE='SALES_ORDER'`. [Ledger](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L266) thêm dòng đơn và [thêm mọi receivable khách](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L340), cả hai có thể `CanSettle`.

**Phản ví dụ:** đơn bán sỉ 10 triệu, trả 4 triệu, receivable 6 triệu. `RemainingVnd` từ đơn=6 triệu; `ReceivableRemainingVnd`=12 triệu. Ledger cũng có hai nguồn nghĩa vụ cho phần 6 triệu.

- [CollectSingleSalesOrderAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1303) thêm ORDER_PAYMENTS, không giảm receivable nguồn đơn.
- [CollectSingleReceivableDebtAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1374) chỉ cập nhật receivable/collection, không thêm payment vào đơn. [Repository thu receivable riêng](../../src/Kazuko.Infrastructure/Finance/FirebirdReceivableDebtRepository.cs#L20) cũng không đồng bộ đơn.
- Vì hai nguồn vẫn được phép thu riêng, sau khi thu đủ 6 triệu qua một đường, đường còn lại vẫn có thể nhận 6 triệu nữa. Thử cả hai thứ tự và đảo từng settlement ở GĐ3; đây là nguy cơ thu quá nghĩa vụ, không gọi là thất thoát đã xảy ra.

**Hướng sửa:** xác định một nguồn số dư chuẩn; liên kết và đồng bộ bằng ID nguồn đơn trong transaction thu/đảo, rồi loại biểu diễn trùng ở query. Không loại mọi receivable khách vì còn khoản bồi thường độc lập. Không sửa dữ liệu lịch sử chỉ bằng cách xóa dòng có số tiền giống nhau.

### MF-04 — Thu theo đơn bỏ bù trừ checkout

Query tổng quan đã trừ `SALES_CHECKOUT_OFFSETS` trong PaidVnd, nhưng [danh sách khoản mở](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1270), [helper thu từng đơn](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1314) và [thu khách kiểu FIFO](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L718) chỉ trừ ORDER_PAYMENTS. Metadata từng dòng [ledger](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L290) cũng bỏ offset dù có thêm dòng bù trừ riêng cho running balance.

**Phản ví dụ:** bán 10 triệu, mua vàng cũ bù 7 triệu, thu chênh 3 triệu, nợ 0. Query khoản mở và helper thu vẫn coi còn 7 triệu. UI Thu tiền ở danh mục khách hàng và công nợ gọi query/helper này, nên đây không chỉ là lệch giữa hai màn hình đọc.

**Hướng sửa:** dùng thống nhất số dư đơn sau payments và offset còn hiệu lực trong cả query và kiểm tra ghi sau khóa. Không tạo payment tiền mặt giả cho phần bù trừ.

### MF-05 — Đảo settlement bỏ payment có reference ngân hàng riêng

[VoidOrderPaymentsForSettlementAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L529) tìm settlement.Code rồi UPDATE payment theo `REFERENCE_NO=code`; không dùng `FINANCIAL_SETTLEMENT_ID`, cũng không kiểm tra phải void đủ số tiền/dòng.

Trong khi đó [checkout](../../src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs#L591), [bán sỉ](../../src/Kazuko.Infrastructure/Sales/FirebirdWholesaleSalesApplicationService.cs#L558), [CUSTOM](../../src/Kazuko.Infrastructure/Sales/FirebirdCraftOrderApplicationService.cs#L856) lưu `REFERENCE_NO=allocation.Reference ?? settlement.Code`, và có lưu settlement ID thật.

Thu 3 triệu có reference `BANK-123` → đảo settlement: câu void không trúng dòng, nhưng settlement/voucher vẫn bị hủy. Đơn giữ payment chưa void nên vẫn coi đã thu. Với reference null, fallback code thường khớp; với chuỗi rỗng nhưng khác null cũng cần thử. Đây là lỗi của API đảo; chưa thấy UI production gọi trực tiếp `ReverseSettlementAsync`, nên không khẳng định có nút hiện tại dẫn tới ca này.

**Hướng sửa:** ưu tiên liên kết ID thật; fallback reference chỉ cho dữ liệu cũ thiếu ID và có kiểm tra phạm vi/tổng. Không void tất cả payments của đơn khi thiếu mã.

### MF-06 — Migration 74 làm mất hỗ trợ CARD của migration 51

[Migration 51](../../database/migrations/0051__sales_checkout_core.sql#L268) cho CARD và tài khoản ngân hàng. [Migration 74](../../database/migrations/0074__cash_vouchers_mixed_method.sql#L22) DROP/ADD `CK_CV_METHOD` chỉ cho CASH/BANK_TRANSFER/MIXED; constraint target mới cũng không có CARD. Tìm toàn bộ migrations không thấy lần mở lại CARD sau đó.

Validator và posting hiện vẫn nhận CARD; nếu bulk chỉ CARD thì voucherMethod vẫn CARD. Với schema theo repository, insert phiếu CARD sẽ vi phạm constraint và rollback transaction. Cash+card có thể tạo header MIXED nên không gặp chính constraint này; không suy ra mọi giao dịch có thẻ đều lỗi giống nhau.

Nếu DB có sẵn CARD trước khi áp migration74, cần kiểm tra thêm khả năng migration thất bại khi tạo constraint mới. Chưa đọc metadata DB thật, chưa xác nhận schema vận hành đã/đang lệch như thế nào.

**Hướng sửa:** migration mới bổ sung CARD cùng MIXED, theo checksum/pipeline; không sửa nội dung migration74 đã áp dụng.

### MF-07 — Tất toán theo ID chưa ràng buộc scope nguồn

[CollectSingleSalesOrderAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1322) chỉ tìm đơn theo ID/status; [CollectSingleReceivableDebtAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1392) và [PaySinglePayableDebtAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1447) chỉ tìm debt theo ID. Caller `SettleDebtAllocationsAsync` không đọc scope đích trước dispatch. Voucher/settlement lại ghi company/branch của session; kiểm tra quỹ/ngân hàng chỉ xác nhận **nơi giữ tiền**, không xác nhận nguồn nợ thuộc scope đó.

ID đích thuộc chi nhánh B được gửi trong phiên A → có thể cập nhật nghĩa vụ B nhưng phiếu tiền thuộc A. Đường API là đủ để xác nhận thiếu ràng buộc; [ledger khách](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L271) còn đọc đơn theo khách mà không lọc branch, nên danh sách dòng của cùng khách qua nhiều chi nhánh cũng cần thử từ UI. Không khẳng định đã có dữ liệu nhiều chi nhánh thực tế.

**Hướng sửa:** kiểm tra company/branch và đối tác/chiều dự kiến trên bản ghi đích sau khóa. Nếu cho phép thu hộ liên chi nhánh, cần nghiệp vụ/chứng từ riêng được quyết định rõ, không mặc định cho qua bằng ID.

### MF-08 — Hủy phiếu chung thiếu audit bắt buộc

Toàn bộ [CancelVoucherAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs#L132) commit sau UPDATE mà không gọi AuditLogWriter, không ghi DOCUMENT_REVERSALS. Có CANCELLED_BY/AT/REASON trên voucher nhưng không tương đương audit bắt buộc theo `docs/00-PROJECT-REQUIREMENTS.md`.

**Hướng sửa:** audit cùng transaction, lưu trạng thái/nguồn/lý do trước và sau theo mức phù hợp; lỗi audit phải rollback. Không ghi audit sau commit. Việc bổ sung audit không tự sửa lỗi owner MF-02.

### MF-09 — Phiếu thủ công không giữ quỹ/tài khoản được chọn

[Dialog](../../src/Modules/Kazuko.Modules.Finance/CashVoucherDialog.xaml#L151) cho chọn fund/bank và [SaveAsync](../../src/Modules/Kazuko.Modules.Finance/CashVoucherDialogViewModel.cs#L359) kiểm tra đã chọn. Nhưng nhánh tạo phiếu [chỉ gửi ManualVoucherRequest](../../src/Modules/Kazuko.Modules.Finance/CashVoucherDialogViewModel.cs#L380); [contract](../../src/Kazuko.Core/Finance/FinanceDashboardContracts.cs#L28) không mang fund/bank ID. [Service](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs#L70) tự chọn quỹ mặc định hoặc ngân hàng active đầu tiên.

Chọn ngân hàng B trong UI khi A đứng đầu → service ghi A. Tương tự quỹ không mặc định. Đây là sai phân bổ đã có đường UI, độc lập MF-01.

Cùng nhánh UI cho nhập nhiều phương thức rồi [DetermineSingleMethod](../../src/Modules/Kazuko.Modules.Finance/CashVoucherDialogViewModel.cs#L420) trả MIXED, nhưng [service](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs#L45) từ chối MIXED ngay trước transaction. Không được hiểu đây là mất tiền: thao tác báo lỗi và chưa tạo phiếu.

**Hướng sửa:** đưa nơi giữ tiền và phân bổ vào contract/validation; chốt cách thể hiện một hay nhiều phiếu cho manual nhiều phương thức, dùng cơ sở báo cáo nhất quán. Có thể chặn rõ UI nếu chưa hỗ trợ; không âm thầm ghi tổng vào tài khoản mặc định.

### MF-10 — Đã đảo trả nợ vẫn còn bị chặn đảo phiếu nhập

[ApplyPayableDebtPaymentAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1515) thêm `PAYABLE_DEBT_PAYMENTS`, chỉ ghi settlement.Code trong NOTES. [UndoPayableApplicationAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L477) hoàn lại số paid/remaining nhưng không vô hiệu payment đó. [EnsureNoOpenDependentsAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L281) lại chặn phiếu RECEIPT nếu tồn tại **bất kỳ** payment join với nợ nguồn chưa CANCELLED, không xét payment có hiệu lực hay settlement đã đảo.

Nhập NVL còn nợ → trả nợ qua workbench → đảo settlement → nợ trở lại OPEN, paid=0 nhưng payment lịch sử vẫn tồn tại → đảo phiếu nhập tiếp tục bị chặn với yêu cầu đảo thanh toán trước. API đảo settlement hiện chưa thấy caller UI trực tiếp, như giới hạn tại MF-05.

**Hướng sửa:** dấu vết thanh toán cần liên kết settlement bằng ID và trạng thái hiệu lực; guard phụ thuộc dùng khoản thanh toán còn hiệu lực. Giữ lịch sử đã đảo thay vì xóa payment để vượt guard.

## 2. Độ phủ 9 nhóm và các điểm chưa kết luận lỗi

| Nhóm GĐ1 | Kết quả rà GĐ2 | Phần còn kiểm chứng |
| --- | --- | --- |
| G2-01 | MF-01, MF-06, MF-09; phân biệt bulk API với dialog một quỹ/một bank | Metadata Firebird hiện hành, đối chiếu số dư thật ở GĐ4 |
| G2-02 | MF-02, MF-08; quyền FIN_REVERSE đã có; HR chặn hủy riêng | Thử hủy từng owner, lỗi audit và rollback |
| G2-03 | MF-03, MF-04; đã theo nguồn đơn → tạo receivable → thu từng sổ → code đảo | Tái hiện cả hai thứ tự thu và đảo, kiểm tra tất cả màn hình số dư |
| G2-04 | MF-05, MF-10; đảo kho không phải đảo checkout tổng hợp | Xem giới hạn hủy checkout/CUSTOM và guard bên dưới |
| G2-05 | MF-07; idempotency có độ phủ khác nhau, không áp toàn app | Retry sau mất phản hồi commit, race theo DB/driver thực |
| G2-06 | Trả nợ vàng chọn khoản và chỉ ghi sổ nợ; snapshot đối chiếu được giữ | Liên kết nhập vật chất, vàng thừa và tác động điều chỉnh hao hụt |
| G2-07 | Chưa thấy nghiệp vụ riêng dùng credit/chuyển quỹ/đầu kỳ/phí thẻ trong phạm vi tìm | Độ phủ tính năng và quy tắc, không gán thành giao dịch bị thất thoát |
| G2-08 | HR có owner, khóa công ty, unique chi hiệu lực, lọc PAYROLL; chịu MF-06 khi dùng CARD | Chi/hủy/chi lại, ngày thực chi, báo cáo toàn công ty so với dashboard chi nhánh |
| G2-09 | Luồng gia công cũ còn đăng ký nhưng UI tìm thấy chỉ đọc holdings | Dữ liệu legacy và caller ngoài UI hiện tại; chưa kết luận trả công trùng |

### Hủy checkout/CUSTOM và kho

- Không tìm thấy use case hủy tổng hợp checkout/CUSTOM trong service bán hàng hoặc caller UI đang khảo sát. Đảo phiếu tiền riêng không thay thế được quy trình này (MF-02).
- `ReverseStockDocumentAsync` có nhánh trả serial về kho/gỡ dấu xuất, nhưng không tự hủy toàn bộ SALES_CHECKOUTS, offsets, đơn và khoản thu. Chưa thấy caller UI gọi trực tiếp đảo settlement; `FirebirdFinishedGoodsIssueService.ReverseAsync` gọi đảo kho chung nhưng chưa tìm thấy invocation từ module UI. Chỉ ghi đây là giới hạn API/độ phủ, chưa coi người dùng đang có nút “Hủy checkout” hoạt động sai.
- Phiếu nhập TP LSX bị [chặn rõ](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L56) trong nhánh đảo kho chung: giữ nguyên giới hạn có chủ ý, không đánh dấu là lỗi mới.
- Guard CraftReturnReceipt kiểm tra sản phẩm SOLD nhưng chưa đầy đủ mọi phụ thuộc công nợ thu/chi. Dữ liệu cũ có thanh toán/bồi thường phải được thử riêng trước khi mở đường đảo qua UI.

### Retry, khóa và transaction

- [Checkout](../../src/Kazuko.Infrastructure/Sales/FirebirdSalesCheckoutApplicationService.cs#L25) dùng client request/hash và có đường đọc lại sau xung đột; [nhập TP](../../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs#L1082) có request/hash/snapshot; HR award có request/hash và payroll dùng chi POSTED duy nhất theo kỳ/NV.
- Bán sỉ, tạo CUSTOM+cọc, tất toán nợ và trả nợ vàng không có khóa request nghiệp vụ tương đương trong DTO đã đọc. Khóa nguồn allocation của voucher chỉ chống ghi lại **cùng allocation ID**; retry tạo settlement/allocation ID mới không được cơ chế đó bao phủ. Chưa gọi đây là chi trùng đã tái hiện; cần thử lost-response/retry trên DB riêng.
- Các transaction owner đã đọc đặt audit trước commit ở checkout/bán sỉ/CUSTOM/nhập mua NVL/sản xuất/HR/workbench. Hủy voucher chung là ngoại lệ MF-08. Transaction và row lock là bằng chứng thiết kế, không thay thế thử fault/race.
- Sau hủy voucher, `TryFindExistingAsync` chỉ tìm POSTED trong khi unique source allocation vẫn giữ cả bản hủy: không mặc định có thể repost cùng allocation ID. Owner HR chi lại tạo payment ID mới; các owner khác cần ca thử riêng.

### Vàng và sản xuất

- [RecordWorkerGoldRepaymentAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1639) khóa khoản nợ đúng công ty/chi nhánh, kiểm tra số trả không vượt dư; ghi payment/đã trả/audit cùng transaction. Không FIFO; không sinh tiền VND. Hàm không tạo stock document/movement và request không có receipt ID: sổ trả nợ chưa chứng minh vàng vật chất đã tăng kho. Đây là khoảng trống liên kết cần kiểm chứng/quyết định, chưa tự đồng bộ.
- [PostReceiptGoldSettlementsAsync](../../src/Kazuko.Infrastructure/Production/FirebirdProductionReceiptSettlement.cs#L18) đọc vàng giao ròng, tính từ món thực nhập và hao hụt; ghi `GOLD_LOSS_ADJUSTED` nếu khác đã báo. Nợ <=0,0001 chỉ thì bỏ tạo nợ, **không đồng nghĩa mất snapshot**: [postedRequest](../../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs#L1270) giữ GoldSettlements cùng danh sách món đã cấp mã trong PRODUCTION_RECEIPT_REQUESTS. Chưa có sổ vàng thừa/credit riêng được xác lập.
- [CompleteAsync](../../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs#L923) tạo payable LABOR cho LSX thợ ngoài, chưa tạo cash voucher. Nhập TP không tự chi công; tiền công đi đường trả payable.

### HR, thời gian và luồng cũ

- [EmployeeMoneyStore](../../src/Kazuko.Infrastructure/HumanResources/EmployeeMoneyStore.cs#L19) chỉ lấy `HANDLING='PAYROLL' AND STATUS='ACTIVE'` cho kỳ lương; CASH/NONE không cộng lần nữa. [Chi lương](../../src/Kazuko.Infrastructure/HumanResources/FirebirdEmployeeMoneyService.cs#L87) kiểm tra CLOSED/revision, trả đủ, payment PENDING→POSTED và voucher cùng transaction, khóa hàng công ty. [Hủy chi](../../src/Kazuko.Infrastructure/HumanResources/FirebirdEmployeeMoneyService.cs#L119) kiểm tra đúng voucher đã xem và đổi cả payment/voucher.
- [Báo cáo lương](../../src/Kazuko.Infrastructure/Reports/FirebirdPayrollReportService.cs#L45) nối payment POSTED và voucher POSTED, theo kỳ và **toàn công ty**. Dashboard Finance theo chi nhánh, tháng/ngày voucher. Không so hai tổng khác phạm vi rồi kết luận mất tiền.
- HR chủ động cập nhật voucher date theo ngày chi trong EmployeeMoneyStore. Posting dùng chung mặc định `CURRENT_TIMESTAMP` cho voucher; bán sỉ/CUSTOM có payment/settlement theo ngày chứng từ. Cần phân biệt ngày phát sinh chứng từ, ngày ghi sổ và ngày thực thu/chi khi thiết kế ca quá khứ; chưa chốt đây là lỗi nếu chưa xác định kỳ vọng nghiệp vụ.
- Tìm `CreateAndPostIssueAsync`/`CreateAndPostReturnAsync` trong `src/Modules` không thấy caller ghi cho luồng gia công cũ. [ExternalWorkerDialogViewModel](../../src/Modules/Kazuko.Modules.Warehouse/ExternalWorkerDialogViewModel.cs#L187) chỉ gọi SearchHoldingsAsync để hiện tóm tắt legacy. Service cũ vẫn được App đăng ký; không đề xuất xóa hoặc mặc định không còn dữ liệu.
- PartyCreditService hiện tạo credit không tiền; chưa thấy consumer dùng credit để tất toán trong Infrastructure/Modules. Chuyển quỹ/số dư khởi tạo/phí và thời gian tiền thẻ thực vào tài khoản chưa có owner độc lập được xác lập trong nhóm Finance đã tìm. Không hướng dẫn dùng hai phiếu tay làm thay thế nghiệp vụ.

## 3. Ca GĐ3 cần chạy trên DB restore riêng

Mọi ca ghi trên bản restore tách biệt; không dùng DATA.FDB. Sau mỗi ca kiểm tra cả dòng tiền hiệu lực, nghĩa vụ còn mở, stock nếu liên quan, audit và rollback. Mã dưới đây là kế hoạch, **chưa chạy**.

| Ca | Thiết lập/thao tác | Điều cần chứng minh |
| --- | --- | --- |
| T01 | Một nợ 10tr, thu 4tr CASH + 6tr BANK trên cùng dòng | Phiếu DISTINCT=10tr; allocation quỹ=4tr, bank=6tr; so dashboard (MF-01) |
| T02 | Gọi bulk với hai bank hoặc hai quỹ cùng phương thức | Phân bổ từng nơi đúng, không gán tổng vào dòng đầu; kiểm tra cả API và giới hạn UI |
| T03 | Phiếu tay chọn quỹ/bank thứ hai; rồi nhập CASH+BANK | Kiểm tra nơi thực ghi và lỗi MIXED (MF-09) |
| T04 | Ghi CARD riêng qua checkout, manual, payroll, tất toán một dòng; kiểm tra metadata 51→74→91 | Constraint thực, lỗi/rollback, tính tương thích dữ liệu CARD cũ (MF-06) |
| T05 | Tạo settlement thu/chi, nhập mua NVL, payable trực tiếp; hủy phiếu ở tab Thu/Chi | So voucher với owner/ứng dụng nợ; HR phải bị chặn (MF-02) |
| T06 | Bán sỉ 10tr, trả 4tr, nợ 6tr; thu 6tr theo đơn rồi receivable và thứ tự ngược | Số dư/ledger, khả năng ghi thu lần hai; đảo từng settlement (MF-03) |
| T07 | Bán 10tr, vàng cũ 7tr, thu 3tr, nợ 0; mở Thu tiền từ Khách hàng/Công nợ | Query/guard không được cho thu thêm 7tr (MF-04) |
| T08 | Payment có reference null, rỗng, mã bank riêng; đảo settlement | Đơn chỉ tính payment hiệu lực; void theo liên kết đúng (MF-05) |
| T09 | Phiên A, truyền ID đơn/payable/receivable chi nhánh B hoặc công ty B | Backend từ chối, không ghi phiếu A hoặc cập nhật nguồn B (MF-07) |
| T10 | Nhập NVL còn nợ → trả nợ → đảo settlement → đảo phiếu nhập | History đã đảo không gây chặn giả; không tính tổng payment lịch sử như tiền hiệu lực (MF-10) |
| T11 | Cho audit fail tại hủy voucher và tại owner posting | Xác định thiếu audit hiện tại; khi sửa phải rollback cả biến động (MF-08) |
| T12 | Retry sau commit mất phản hồi; hai phiên thu/chi cùng khoản; payload đổi với cùng request | Ghi nhận độ phủ idempotency/row lock theo từng owner, không giả định từ helper |
| T13 | HR CASH/PAYROLL/NONE → tính/chốt → chi → hủy → chi lại; dữ liệu nhiều chi nhánh | Không cộng lại CASH; một payment POSTED/NV/kỳ; so đúng ngày/phạm vi |
| T14 | Nhập TP nợ dương/0/vàng thừa, hao hụt khác đã báo; trả nợ vàng một phần/đủ | Snapshot, audit, sổ nợ và tồn vật chất được kiểm riêng; giữ chọn khoản không FIFO |
| T15 | Gia công cũ có payment/bồi thường, thử API đảo sau khi thu/trả nợ và serial đã chuyển/bán | Guard hoặc đảo đầy đủ phụ thuộc; xác định có phải hỗ trợ legacy hay chỉ giữ xem |

GĐ4 sau đó mới đo số lượng/giá trị giao dịch thật bị ảnh hưởng. Đối soát dựa trên liên kết nguồn, voucher DISTINCT và allocations đúng vai trò. Không suy tổng thiệt hại bằng cách cộng các số lệch của báo cáo, công nợ và tồn kho.

## 4. Kiểm chứng trong GĐ2

- Đọc code production, contract, caller UI, quyền backend và migrations; không chỉ dựa mô tả trong test.
- Các test `DebtReversalFlowTests` có phần mirror guard/công thức và log mô phỏng: chúng không kiểm chứng câu UPDATE/SQL hoặc tính atomic thực. Integration tests có nhánh return khi thiếu fixture; không dùng kết quả “pass” đó để xác nhận DB.
- Đã thử chạy nhóm unit có production calculator/validator bằng `dotnet test tests/Kazuko.Infrastructure.Tests/Kazuko.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~SalesCheckoutCalculatorTests|FullyQualifiedName~SalesCheckoutValidatorTests|FullyQualifiedName~FinancialVoucherValidatorTests" --logger "console;verbosity=minimal" -p:BuildInParallel=false`. Core, Infrastructure và project test build thành công; **test run bị abort vì vstest không kết nối được testhost sau 90 giây**. Chưa có kết quả pass/fail của test, không coi build thành công là test đã qua. Không chạy integration/fault/race trong GĐ2.
- Không build toàn solution vì chỉ thêm báo cáo; build/test giới hạn không thay thế gate cho một bản sửa nghiệp vụ sau này.

## 5. Thứ tự khắc phục đề xuất sau kiểm chứng

1. Chặn hủy sai owner, thu vượt/thu trùng và sai scope (MF-02/03/04/07); chọn nguồn công nợ chuẩn trước khi sửa query.
2. Sửa biểu diễn nơi giữ tiền và hợp đồng phiếu tay (MF-01/09); bổ sung migration CARD theo pipeline (MF-06).
3. Hoàn thiện đảo theo ID và lịch sử hiệu lực, audit (MF-05/08/10).
4. Chạy lại các ca GĐ3 trên DB riêng; chỉ sau đó lập phương án đối soát/sửa dữ liệu lịch sử có phạm vi, backup và bằng chứng. Báo cáo này không cấp một lệnh sửa dữ liệu hàng loạt.
