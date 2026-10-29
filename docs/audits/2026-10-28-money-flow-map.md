# GĐ1 — Bản đồ dòng tiền và cơ sở đối soát

Ngày khảo sát: **28/10/2026**. Mốc code: **f0133f7**.

Phạm vi: đọc code và tài liệu để xác định điểm phát sinh tiền, công nợ, tồn kho; lập quy tắc đối soát và danh sách cần kiểm tra tiếp. **Chưa truy vấn database, chưa chạy tình huống lỗi/đồng thời, chưa sửa nghiệp vụ hay dữ liệu.** Các mục G2 bên dưới là rủi ro cần kiểm chứng, không phải kết luận đã thất thoát tiền thực tế.

## 1. Ba lớp dữ liệu cần phân biệt

| Lớp | Nguồn | Ý nghĩa khi đối soát |
| --- | --- | --- |
| Chứng từ nghiệp vụ | `SALES_CHECKOUTS`, `SALES_ORDERS`, `STOCK_DOCUMENTS`, `PRODUCTION_ORDERS`, bảng nhân sự | Giải thích vì sao thu/chi hoặc phát sinh nghĩa vụ. Giá trị chứng từ không đồng nghĩa tiền đã thu/chi. |
| Thanh toán/phân bổ | `FINANCIAL_SETTLEMENTS`, `FINANCIAL_SETTLEMENT_ALLOCATIONS`, `FINANCIAL_SETTLEMENT_APPLICATIONS`; các dòng thanh toán đơn, kho, công nợ | Settlement nối tiền với đích thanh toán. Allocation chia theo phương thức/quỹ/tài khoản; application chia theo đơn hoặc khoản nợ. Một số luồng ghi phiếu trực tiếp, không qua settlement. |
| Phiếu tiền | `CASH_VOUCHERS` | Dòng tổng thu/chi đang được dashboard sử dụng. Chỉ `POSTED` được cộng vào báo cáo hiện tại; `IN` tăng, `OUT` giảm. |

Không cộng tiền settlement + allocation + phiếu như ba khoản thu độc lập. Không cộng tổng giá trị kho, giá vốn hoặc nợ vàng vào số dư tiền.

Nguồn: [settlement service](../../src/Kazuko.Infrastructure/Finance/FirebirdFinancialSettlementService.cs), [voucher posting](../../src/Kazuko.Infrastructure/Finance/FirebirdFinancialVoucherPostingService.cs), [dashboard](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceDashboardRepository.cs).

### Hai kiểu ghi phiếu cùng tồn tại

1. `PostInTransactionAsync`: một settlement, nhiều allocations; mỗi allocation có một phiếu. Ví dụ thu khi bán sỉ hoặc nhận cọc.
2. `PostBulkSettlementInTransactionAsync`: một settlement, nhiều allocations/applications nhưng **một phiếu tổng**. Mọi allocation cùng trỏ về phiếu đó. Dùng trong sổ công nợ/thu trả gộp, kể cả một số thao tác thanh toán từng dòng.

Phiếu bulk dùng `MIXED` nếu có nhiều phương thức. Nếu cùng phương thức, header lấy quỹ/tài khoản từ allocation đầu tiên. Nếu nhiều phương thức, header không gắn quỹ/tài khoản. Vì vậy, đối soát từng quỹ/tài khoản phải kiểm tra **chi tiết allocations**, kể cả trường hợp cùng phương thức nhưng khác tài khoản; không chỉ kiểm tra chuỗi `MIXED`.

`CASH` yêu cầu quỹ; `BANK_TRANSFER` và `CARD` yêu cầu tài khoản ngân hàng. App hiện coi tiền thẻ vào tài khoản được chọn; chưa thể suy ra thời điểm ngân hàng thực nhận hoặc phí thẻ chỉ từ quy tắc này. `MIXED` là cách biểu diễn phiếu tổng, không phải phương thức nhập của một allocation. Nguồn: [validator](../../src/Kazuko.Core/Finance/PaymentAllocationValidator.cs).

## 2. Danh mục luồng và điểm ghi sổ

Trong bảng: **IN/OUT** là tiền thực ghi phiếu; **0** là thao tác đó không tự ghi tiền. “Cùng transaction” mô tả cấu trúc code đã đọc, chưa xác nhận bằng thử rollback hoặc mất kết nối.

| ID | Thao tác / chứng từ nguồn | Tiền | Công nợ và tồn kho | Ranh giới và bằng chứng |
| --- | --- | --- | --- | --- |
| F01 | Tạo phiếu thu hoặc chi thủ công | IN hoặc OUT | Chưa thấy cập nhật nợ/đơn/kho trong service tạo phiếu; loại thu/chi là nhãn phân loại | `CreateManualVoucherAsync` ghi `CASH_VOUCHERS`; không mặc định coi phiếu nhập tay là trả nợ. [Nguồn](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs). |
| F02 | Checkout bán vàng mới, có thể thu đổi vàng cũ | Thu chênh lệch, chi chênh lệch hoặc 0 | Xuất serial vàng mới; nhập vàng cũ; ghi bù trừ; phần khách nợ tạo receivable | `PostAsync` bao checkout, đơn bán, xuất/nhập, offset, phiếu và nợ trong một transaction. Có request ID/hash để nhận diện gửi lại. [Nguồn](../../src/Kazuko.Infrastructure/Sales/FirebirdSalesCheckoutApplicationService.cs), [chi tiết](../../src/Kazuko.Infrastructure/Sales/SalesCheckoutPostingSupport.cs). |
| F03 | Chỉ mua vàng cũ trong checkout | OUT | Tăng kho vàng cũ; khoản chi được nối qua `STOCK_DOCUMENT_PAYMENTS` | Cùng transaction F02. Nhánh net âm yêu cầu phân bổ đủ phần chi, không cho tạo nợ phải thu. [Validator](../../src/Kazuko.Core/Sales/SalesCheckoutValidator.cs). |
| F04 | Bán sỉ | IN theo số trả ngay, có thể 0 | Xuất thành phẩm; phần khách còn nợ được ghi `RECEIVABLE_DEBTS`; đồng thời có `ORDER_PAYMENTS` cho phần thu | `PostAsync` ghi đơn, xuất kho, settlement thu và receivable trong cùng transaction. [Nguồn](../../src/Kazuko.Infrastructure/Sales/FirebirdWholesaleSalesApplicationService.cs). |
| F05 | Đơn đặt chế tác + cọc | IN | Cọc vào `ORDER_PAYMENTS`; tạo đơn CUSTOM và dòng mẫu, chưa xuất thành phẩm | `CreateWithDepositAsync` tạo đơn và thu cọc trong cùng transaction; theo tài liệu cọc phải > 0 và không vượt tổng đơn. [Code](../../src/Kazuko.Infrastructure/Sales/FirebirdCraftOrderApplicationService.cs), [quy tắc](../10-CRAFT-CUSTOM-ORDER.md). |
| F06 | Thu tiếp / giao đơn đặt chế tác | IN theo số thu thêm, có thể 0 | Cập nhật thanh toán và phần còn nợ; khi giao xuất đủ serial thuộc đơn | `CollectRemainingAsync`, `DeliverAsync`, `PostRemainingSettlementAsync`. Giao hàng, thu tiền và tạo nợ nằm trong transaction giao; phải kiểm tra tiếp đường thu sau giao. [Nguồn](../../src/Kazuko.Infrastructure/Sales/FirebirdCraftOrderApplicationService.cs). |
| F07 | Thu nợ khách hàng | IN | Nhánh khách trong workbench phân bổ vào đơn bán và thêm `ORDER_PAYMENTS`; tồn kho không đổi | `CollectCustomerPaymentAsync` khóa/đọc lại đơn, dùng settlement bulk. Cần đối chiếu với `RECEIVABLE_DEBTS` cùng nguồn; xem G2-03. [Nguồn](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs). |
| F08 | Nhập mua nguyên liệu từ NCC | OUT phần trả ngay, có thể 0 | Tăng kho; phần chưa trả tạo `PAYABLE_DEBTS` | `CreateDraftAndPostAsync` ghi kho, `STOCK_DOCUMENT_PAYMENTS`, phiếu chi trực tiếp và payable cùng transaction. Đây là đường không bắt buộc có settlement. [Nguồn](../../src/Kazuko.Infrastructure/Warehouse/FirebirdMaterialReceiptService.cs). |
| F09 | Trả khoản phải trả NCC / khách / thợ ngoài | OUT | Tăng đã trả, giảm còn nợ, ghi lịch sử thanh toán; không tự thay đổi kho | Có đường từng khoản `PayAsync` với phiếu trực tiếp, và đường workbench dùng settlement bulk. `PayPayablePartyFifoAsync` phân bổ FIFO theo đối tượng; `SettleSingleLedgerEntryAsync`/`SettleDebtAllocationsAsync` phục vụ chọn khoản. [Repository](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceRepositories.cs), [workbench](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs). |
| F10 | Thu khoản phải thu NCC / bồi thường / receivable riêng | IN | Giảm receivable, ghi collection; không tự thay đổi kho | `CollectSupplierPaymentAsync` dùng bulk; repository receivable dùng settlement thường và `RECEIVABLE_DEBT_COLLECTIONS`. Kiểm tra chính xác đích áp dụng và đảo ở GĐ2. [Workbench](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs), [repository](../../src/Kazuko.Infrastructure/Finance/FirebirdReceivableDebtRepository.cs). |
| F11 | Giao NVL cho LSX, trả NVL, QC và hoàn thành LSX | 0 tại các bước này | Giao/trả thay đổi kho và theo dõi NVL. Hoàn thành lệnh thợ ngoài có tiền công > 0 tạo payable `LABOR`, nguồn `PRODUCTION_ORDER`; chưa chi tiền, chưa tăng kho TP chỉ vì hoàn thành | Chi tiền công đi qua F09. Tách nợ tiền công khỏi NVL đang giao thợ. [Nguồn](../../src/Kazuko.Infrastructure/Production/FirebirdProductionApplicationService.cs). |
| F12 | Nhập TP từ LSX và đối chiếu vàng | 0 | Tăng TP, snapshot giá trị; có thể tạo nợ vàng theo loại vàng/thợ, đơn vị **chỉ** | Phiếu nhập, món và đối chiếu nằm trong transaction nhập; không chuyển nợ vàng thành VND tự động. Có request ID/hash. [Code đối chiếu](../../src/Kazuko.Infrastructure/Production/FirebirdProductionReceiptSettlement.cs), [workflow](../production-receipt-workflow.md). |
| F13 | Thợ trả nợ vàng | 0 | Ghi `WORKER_GOLD_DEBT_PAYMENTS`, tăng trọng lượng đã trả và giảm còn nợ của khoản được chọn | `RecordWorkerGoldRepaymentAsync` khóa khoản nợ. **Không áp FIFO** cho nợ vàng; cần kiểm tra liên kết phiếu nhập vật chất ở GĐ2, không mặc định ghi nợ = đã tăng tồn. [Nguồn](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs). |
| F14 | Luồng gia công ngoài cũ | Giao NVL: 0; trả hàng: OUT nếu thanh toán công ngay | Trả hàng có thể tạo payable tiền công, receivable bồi thường và cập nhật vật tư giữ tại thợ | `CreateAndPostIssueAsync` nêu rõ không tạo tiền/nợ VND cho giá trị NVL giao. `CreateAndPostReturnAsync` có phiếu chi công. Cần xác nhận khả năng truy cập UI hiện hành, tránh nhầm với F11/F12. [Nguồn](../../src/Kazuko.Infrastructure/Warehouse/FirebirdOutsourcedCraftService.cs). |
| F15 | Khen thưởng / kỷ luật | CASH: thưởng OUT, kỷ luật IN; PAYROLL/NONE: 0 lúc ghi nhận | PAYROLL đưa vào kỳ lương; CASH không cộng/trừ lại vào lương | `CreateAsync`/`CancelAsync` là chủ sở hữu phiếu `HR_AWARD`. [Code](../../src/Kazuko.Infrastructure/HumanResources/FirebirdEmployeeMoneyService.cs), [quy tắc](../employee-money-payroll.md). |
| F16 | Tính / chốt / chi lương | Tính/chốt: 0; chi: OUT | Ghi `HR_PAYROLL_PAYMENTS` và phiếu `HR_PAYROLL`; không được tính phiếu thưởng CASH lần nữa | `PaySalaryAsync` ghi payment + voucher cùng transaction; theo quy tắc hiện tại chi đủ một lần mỗi NV/kỳ, phải chốt trước. Hủy chi tại nhân sự. [Code](../../src/Kazuko.Infrastructure/HumanResources/FirebirdEmployeeMoneyService.cs), [quy tắc](../employee-money-payroll.md). |
| F17 | Tạo credit đối tác | 0 | `PARTY_CREDITS`: original, used, remaining; loại hold do trả hàng hoặc điều chỉnh | `CreateAsync` chủ động không tạo phiếu tiền. Chưa thấy consumer sử dụng credit ngoài service tạo khi tìm trong Infrastructure; chưa coi đây là quy trình trả hàng/hoàn tiền hoàn chỉnh. [Nguồn](../../src/Kazuko.Infrastructure/Finance/FirebirdPartyCreditService.cs). |
| F18 | Chuyển kho / tiếp nhận hàng bán / kiểm kê | 0 | Chuyển kho thay đổi vị trí; kiểm kê hiện đóng `COUNTED`, chưa điều chỉnh tồn | Kiểm kê không phải khoản chi cho phần thiếu kho. Giá trị chênh lệch kiểm kê chưa trở thành phiếu thu/chi. [Kiểm kê](../../src/Kazuko.Infrastructure/Warehouse/FirebirdStocktakeService.cs), [quy tắc kho](../06-WAREHOUSE-STOCK-INVARIANTS.md), [phạm vi kiểm kê](../stocktake-counting.md). |

Không suy ra từ F17 rằng app đã hỗ trợ đầy đủ hoàn tiền bán hàng, ứng trước hoặc bù trừ credit. Chưa xác lập được một luồng chuyển tiền giữa quỹ/ngân hàng và số dư đầu kỳ có chủ sở hữu riêng trong các điểm ghi đã đọc; đưa vào G2-07 để kiểm tra độ phủ thay vì hướng dẫn dùng hai phiếu nhập tay.

## 3. Đường hủy và tác động ngược

| Đường hủy | Hành vi đọc được | Điều phải kiểm chứng |
| --- | --- | --- |
| Phiếu thủ công | `CancelVoucherAsync` chuyển phiếu sang `CANCELLED` | Chỉ hủy phiếu không đủ đảo nợ/đơn nếu phiếu phát sinh từ nghiệp vụ khác. Service chỉ thấy chặn rõ `HR_AWARD`/`HR_PAYROLL`; UI gọi service này từ danh sách phiếu. |
| Settlement | `ReverseSettlementAsync` hoàn tác applications theo đích payable/receivable/order; hủy phiếu liên kết qua allocations; hủy settlement và ghi reversal | Dòng chi tiết, số dư nợ và trạng thái phiếu phải cùng đổi; bulk có nhiều allocation chung một voucher, không được hoàn tác hai lần. |
| Chứng từ kho | `ReverseStockDocumentAsync` kiểm tra phụ thuộc, đảo movement, hủy phiếu liên kết, hủy nợ nguồn và cập nhật trạng thái liên quan | Kho có nợ đã thanh toán, checkout có bù trừ, đơn đã giao phải được chặn/đảo đầy đủ. Không suy ra hủy phiếu kho là hủy toàn bộ checkout. |
| Nhập TP từ LSX | Service đảo kho chung **chặn** loại phiếu này vì chưa có nhánh đảo đầy đủ liên kết LSX/nợ vàng/trạng thái món | Đây là giới hạn có chủ ý hiện tại; không dùng hủy phiếu tiền để thay thế đảo nhập TP. |
| Thưởng/phạt và lương | Hủy từ service nhân sự để cập nhật nguồn cùng phiếu; chặn hủy riêng từ service phiếu chung | Thử hủy → trả lại trạng thái → chi lại; lịch sử giữ lại nhưng tiền hiệu lực chỉ tính một lần. |

Nguồn: [hủy phiếu](../../src/Kazuko.Infrastructure/Finance/FirebirdFinanceVoucherService.cs), [đảo chứng từ](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs), [hủy nhân sự](../../src/Kazuko.Infrastructure/HumanResources/FirebirdEmployeeMoneyService.cs).

## 4. Phương trình đối soát dùng cho các giai đoạn sau

### R01 — Tiền tổng và từng nơi giữ tiền

- Thu kỳ = tổng `AMOUNT_VND` của phiếu `POSTED`, `IN`, đúng công ty/chi nhánh và ngày chứng từ trong `[từ ngày, đến ngày kế tiếp)`.
- Chi kỳ tương tự với `OUT`. Biến động tiền = Thu − Chi.
- Cuối kỳ = Đầu kỳ + Biến động kỳ. Đầu kỳ phải lấy từ cùng cơ sở ghi sổ trước ngày bắt đầu; nếu có số dư khởi tạo thì phải xác định cách biểu diễn và không cộng hai lần.
- Một phiếu có settlement allocations: tổng allocation phải bằng tiền phiếu (kiểu thường là allocation tương ứng; kiểu bulk là tổng các allocation trỏ cùng phiếu). Khi chia theo quỹ/tài khoản, dùng số tiền của allocation đúng nơi giữ tiền.
- Phiếu trực tiếp/thủ công không có settlement allocations: dùng quỹ/tài khoản và số tiền trên phiếu. Không cộng thêm header của phiếu đã được chia qua allocations.
- Tổng biến động tất cả quỹ + tài khoản phải bằng biến động tổng, nếu mọi phiếu hiệu lực đều được gắn nơi giữ tiền hợp lệ. Khoản không phân loại phải hiện thành ngoại lệ, không biến mất khi join.

Dashboard hiện dùng tổng phiếu cho Thu/Chi tháng và lọc `CASH_FUND_ID`/`BANK_ACCOUNT_ID` trên **header phiếu** cho số dư; chưa thể coi hai cách tính luôn khớp với bulk. Đây là cơ sở kiểm tra G2-01, chưa phải đề xuất thay query ngay.

### R02 — Settlement và dấu vết thanh toán

- Với mỗi settlement hiệu lực: `TOTAL_AMOUNT_VND = Σ allocations = Σ applications = Σ phiếu hiệu lực DISTINCT được liên kết`.
- Applications chỉ nhận `SALES_ORDER`, `PAYABLE_DEBT`, `RECEIVABLE_DEBT`; tổng và số tiền dương được `ValidateDraft` kiểm tra. Caller chịu cập nhật đích, service settlement không tự thay toàn bộ sổ nợ.
- Allocation phải thuộc đúng công ty/chi nhánh qua settlement và quỹ/tài khoản; đích phải cùng đối tượng và đúng chiều thu/chi.
- Phiếu trực tiếp được kiểm tra qua nguồn riêng: `STOCK_DOCUMENT_PAYMENTS`, `PAYABLE_DEBT_PAYMENT_ALLOCATIONS`, nhân sự hoặc phiếu thủ công; thiếu settlement tự nó không phải lỗi.
- Một nguồn thanh toán hiệu lực chỉ được ghi tiền một lần. Phải phân biệt retry cùng request với chủ động tạo giao dịch mới. Khóa idempotency được kiểm tra theo từng luồng, không mặc định bao phủ toàn app.

### R03 — Bán lẻ và đổi vàng

Theo [calculator](../../src/Kazuko.Core/Sales/SalesCheckoutCalculator.cs):

```text
Tiền bán ròng = Tổng dòng bán − Giảm giá
Chênh lệch = Tiền bán ròng − Tiền mua vàng cũ
Bù trừ = min(Tiền bán ròng, Tiền mua vàng cũ)
Nếu chênh lệch > 0: Thu ngay + Khách còn nợ = Chênh lệch
Nếu chênh lệch < 0: Chi ngay = −Chênh lệch; không tạo nợ phải thu
Nếu chênh lệch = 0: Không phát sinh phiếu tiền cho chênh lệch
```

`SALES_CHECKOUT_OFFSETS` là bù trừ phi tiền mặt. Không thêm nó vào doanh số thu tiền hoặc tạo thêm phiếu chi tương ứng. VND được calculator làm tròn từng khoản bằng `AwayFromZero`; phải dùng snapshot đã ghi thay vì tính lại bằng bảng giá hôm nay.

### R04 — Đơn bán, cọc và công nợ VND

- Số đã thanh toán đơn = tổng `ORDER_PAYMENTS` chưa void, `REFUND` mang dấu âm; bù trừ checkout theo nguồn còn hiệu lực được xét riêng.
- Còn nợ đơn = tổng đơn − thanh toán hiệu lực − bù trừ hiệu lực. Cọc là một phần thanh toán; không cộng lại cọc khi giao.
- Với khoản nợ riêng: Gốc = Đã thanh toán/thu hiệu lực + Còn nợ, khi không có nghiệp vụ điều chỉnh/credit riêng. Bản ghi hủy không được tiếp tục tính vào số dư mở.
- Nếu receivable chỉ là bản biểu diễn phần còn nợ của một đơn, không cộng cả “còn nợ đơn” và receivable đó vào tổng phải thu. Trước khi loại trùng phải chứng minh liên kết nguồn, không loại theo số tiền giống nhau.
- Phải so sánh cả hướng đơn → khoản nợ và khoản nợ → đơn sau thu/hủy. Các khoản bồi thường không có đơn vẫn phải giữ lại.
- Tổng nghĩa vụ mua NVL = trả ngay + nợ ban đầu. Sau trả nợ: tổng trả ngay + trả sau hiệu lực + còn nợ = nghĩa vụ gốc, nếu không có điều chỉnh/trả hàng.
- FIFO là chính sách của từng lệnh phân bổ theo đối tác, không áp đặt cho mọi khoản nợ; nợ vàng chọn khoản cụ thể như F13.

### R05 — Tiền lương và thưởng/phạt

- Thực nhận = Lương + Thưởng điều chỉnh + Thưởng PAYROLL − Khấu trừ điều chỉnh − Kỷ luật PAYROLL.
- Khen thưởng/kỷ luật CASH chỉ xuất hiện ở dòng tiền tức thời; NONE không phát sinh tiền; cả hai không cộng lại vào thực nhận.
- Tổng chi lương hiệu lực = tổng `HR_PAYROLL_PAYMENTS` POSTED có phiếu POSTED tương ứng. So từng NV/kỳ, không chỉ so tổng tháng; nguồn `HR_PAYROLL` không lẫn phiếu chi thủ công nhãn “lương”.
- Chốt/tính lại không phải chi lương. Hủy payment và voucher phải đồng bộ; không có hai payment hiệu lực cho cùng NV/kỳ theo quy tắc hiện tại.

### R06 — Vàng, tồn kho và giá trị tiền

- Đối chiếu nhập TP theo từng loại vàng: Vàng giao ròng = Vàng giao − Vàng trả NVL − Phế liệu thu hồi. Chênh lệch = Vàng giao ròng − Vàng trong TP nhập. Nợ vàng dự kiến = Chênh lệch − Hao hụt được xác nhận, theo ngưỡng code.
- Sổ nợ vàng: Gốc chỉ = Đã trả chỉ + Còn nợ chỉ, theo từng khoản/thợ/loại vàng; không quy đổi VND nếu chưa có chứng từ và quy tắc riêng.
- Code hiện ghi audit `GOLD_LOSS_ADJUSTED` nếu hao hụt nhập khác hao hụt đã báo; không coi hai giá trị tự đồng bộ. Nhánh nợ ≤ 0,0001 chỉ không tạo khoản nợ; việc lưu vàng thừa phải đối chiếu snapshot/ledger riêng.
- Movement kho phải đối soát số lượng/trọng lượng độc lập với tiền. Giá vốn, giá bán dự kiến, tiền công chưa trả, thiếu kiểm kê đều không tự là phiếu tiền.

### R07 — Hủy, thời gian và phạm vi

- Đối soát sau hủy phải khôi phục đủ tiền/nợ/kho thuộc giao dịch, hoặc chặn thao tác khi có phụ thuộc. Kiểm tra “nguồn hiệu lực nhưng phiếu hủy”, “nguồn hủy nhưng phiếu còn hiệu lực” theo đúng owner.
- Mọi phép so sánh dùng cùng company, branch, trạng thái, đơn vị và khoảng ngày. Phân biệt `VOUCHER_DATE`, ngày settlement, ngày đơn và `CREATED_AT`.
- Báo cáo hiện loại phiếu CANCELLED ra khỏi tổng; cần xác định kỳ vọng báo cáo lịch sử sau hủy trước khi coi chênh lệch giữa hai lần chạy là thất thoát.

## 5. Hàng đợi GĐ2 theo mức ưu tiên

Mức dưới đây là **ưu tiên kiểm tra**, chưa phải mức lỗi đã xác nhận.

| ID | Ưu tiên | Bằng chứng / điểm chưa khép kín | Kiểm chứng để có kết luận |
| --- | --- | --- | --- |
| G2-01 | Cao | Bulk để trống nơi giữ tiền nếu nhiều phương thức, hoặc lấy allocation đầu nếu cùng phương thức; dashboard số dư đọc header | Đọc hết query báo cáo; thử cash + bank, hai bank, hai quỹ. So tổng phiếu với allocations và từng số dư; kiểm tra có ràng buộc đầu vào chặn nhiều tài khoản cùng phương thức không. |
| G2-02 | Cao | Hủy phiếu chung chỉ chặn rõ nguồn HR; UI danh sách gọi `CancelVoucherAsync` | Lập bảng quyền hủy từng source; chứng minh có/không đường hủy riêng phiếu liên kết settlement/kho/nợ làm mất đồng bộ. Đối chiếu với `ReverseSettlementAsync` và `ReverseStockDocumentAsync`. |
| G2-03 | Cao | Workbench khách đọc đơn + ORDER_PAYMENTS/offset, đồng thời có receivable riêng; thu khách cập nhật đơn, thu receivable cập nhật khoản nợ | Theo một đơn còn nợ qua bán → thu từng đường → hủy; xác định bản ghi gốc/biểu diễn và quy tắc loại trùng, không chỉ so con số UI. |
| G2-04 | Cao | Checkout có đơn, kho vàng cũ/mới, offset và tiền; đảo kho chung có phạm vi riêng | Kiểm tra các đường hủy/thay đổi checkout và đơn CUSTOM; đảm bảo không giữ offset hoặc cọc hiệu lực sai sau đảo. Không mặc định hoàn tiền bằng phiếu nhập tay. |
| G2-05 | Cao | Nhiều kiểu posting, nhiều owner cập nhật nợ; helper hỗ trợ idempotency nhưng chưa chứng minh bao phủ từng caller | Rà khóa request/source, unique constraint, kiểm tra row lock, kiểm tra quyền và target scope; lập ca retry/đồng thời cho GĐ3. |
| G2-06 | Trung bình | Nợ vàng, hao hụt, vàng thừa và tồn vật chất là các sổ khác nhau | Xác định đường nhập vật chất khi trả nợ vàng, snapshot vàng thừa, điều chỉnh hao hụt; giữ nguyên quyết định không FIFO của người dùng. |
| G2-07 | Trung bình | Credit có đường tạo nhưng chưa thấy đầy đủ đường dùng; chưa xác lập luồng chuyển quỹ, đầu kỳ, phí/thời gian nhận tiền thẻ | Phân biệt tính năng chưa có với tính năng có nhưng không vào báo cáo; chỉ hỏi người dùng quy tắc khi code/tài liệu chưa đủ bằng chứng. |
| G2-08 | Trung bình | Payroll và award có owner riêng, báo cáo có thể đọc payment và phiếu ở ngày khác nhau | Kiểm tra filter ngày/trạng thái/category và quyền xem; tính → chốt → chi → hủy → chi lại, không tính trùng thưởng CASH. |
| G2-09 | Trung bình | Luồng gia công ngoài cũ và LSX mới cùng tồn tại trong Infrastructure; tiền công phát sinh ở các bước khác nhau | Xác định UI/API còn dùng từng đường và khóa trùng nguồn tiền công/bồi thường. Chưa đề xuất xóa đường cũ khi chưa xác nhận người dùng. |

## 6. Đầu ra và giới hạn GĐ1

- Đã lập 18 nhóm thao tác, tách đường có tiền và đường chỉ thay đổi nghĩa vụ/tồn kho.
- Đã ghi 2 kiểu settlement/phiếu, các đường phiếu trực tiếp, chủ sở hữu hủy và 7 nhóm phương trình đối soát.
- Đã xác định 9 nhóm cần truy tiếp, có bằng chứng code và cách kiểm chứng; chưa thay quyết định nghiệp vụ.
- Chưa có kết luận dữ liệu thật bị lệch bao nhiêu, chưa có số liệu tài khoản/ngân hàng thực tế, chưa sửa query hay service. Không dùng tài liệu này như biên bản kiểm quỹ.

GĐ2 bắt đầu từ **G2-01 → G2-02 → G2-03**, sau đó lần lượt theo nguồn nghiệp vụ. GĐ3 chạy ca lỗi/đồng thời trên bản restore riêng; GĐ4 mới đối soát database và tổng hợp lỗi xác nhận kèm ảnh hưởng, bằng chứng, phương án sửa.
