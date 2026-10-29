# GĐ3 — Kiểm chứng dòng tiền trên DB restore riêng

Ngày **28/10/2026**, code nghiệp vụ **f0133f79bee38397530cd0270298617c5fc00958**. Tiếp nối [GĐ2](2026-10-28-money-flow-gd2-code-review.md).

**Đã tiếp tục GĐ4 theo yêu cầu sau đó:** xem [đối soát chỉ đọc DATA.FDB](2026-10-28-money-flow-gd4-data-reconciliation.md). Kết quả GĐ3 dưới đây thuộc DB thử; không dùng số liệu tái hiện lỗi ở đây làm số thiệt hại của dữ liệu vận hành.

Đã thực hiện các nhóm T01–T15 ở mức đại diện, gồm **26 kịch bản trong harness mới**, bộ nhập TP, HR, audit-fault hiện có và **33 unit tests**. Harness cuối chạy trên một DB restore sạch riêng, exit code 0, 26 kết quả `OBSERVED`, không có `ERROR`. `OBSERVED` nghĩa là kết quả đo khớp điều kiện kiểm chứng, **bao gồm tái hiện lỗi**, không có nghĩa hệ thống không còn lỗi.

Kết quả: tái hiện 10 mục MF-01–MF-10 với điều kiện dưới đây; phát hiện thêm MF-11–MF-13 và xác nhận khoảng trống idempotency của thu nợ. Chưa sửa code nghiệp vụ, migration gốc hay dữ liệu vận hành. **Không mở/truy vấn/ghi thử DATA.FDB; chưa thực hiện GĐ4 hoặc đo thiệt hại thực tế.**

## 1. Môi trường và dấu vết

- Firebird server localhost:3050, runtime đi kèm **5.0.4.1812 x64**, .NET 8; credentials lấy từ môi trường, không ghi vào source/log.
- Backup nguồn: `D:\Working\artifacts\production-receipt\before91_20261026_220252.fbk`, SHA-256 `9989432FCCF524030A7DFD3953918A76C8B670F247C7701E9EF6083B4B1DE509`.
- Đây là snapshot **26/10**, không phải snapshot dữ liệu ngày 28/10. Chỉ dùng tái hiện hành vi code/schema; không suy số lượng lỗi đang có từ số liệu bản thử.
- [DB sạch chạy harness cuối](D:/Working/artifacts/money-flow-gd3/test91_moneyflow_clean_20261028.fdb): restore bằng gbak `-c`, không overwrite; schema nguồn 90 → áp migration91 bằng `Invoke-KazukoMigration.ps1` chuẩn có snapshot/checksum/rollback.
- [DB thử ban đầu và bộ nhập TP](D:/Working/artifacts/money-flow-gd3/test91_moneyflow_20261028.fdb): schema91. [DB HR/audit riêng](D:/Working/artifacts/money-flow-gd3/test89_hr_moneyflow_20261028.fdb): restore cùng backup, schema90 (tên test89 theo guard của bộ kiểm tra, **không phải mô tả schema thực**).
- Kiểm tra sau cùng trên cả ba DB: **0 fault triggers, 0 fault exceptions** còn lại của GĐ3/bộ audit/nhập TP. Không cần dừng ERP đang chạy.

Harness tại [Program.cs](../../tests/Kazuko.MoneyFlow.IntegrationChecks/Program.cs), hướng dẫn tại [README](../../tests/Kazuko.MoneyFlow.IntegrationChecks/README.md). Guard chỉ nhận file có thật `test91_moneyflow_*.fdb` ngay trong thư mục `D:\Working\artifacts\money-flow-gd3`, chặn reparse point trên file/thư mục đó; ép connection của **process thử** về localhost/đúng path. Không lưu profile của app. Fixture thêm khách, đơn, tài khoản và sản phẩm thử bằng SQL trên DB riêng; các thao tác cần kiểm chứng gọi service production. MF-03/04/05 dùng checkout production, không chỉ dựng các bảng thanh toán bằng tay. MF-09 gọi ViewModel trên STA với service capture rồi đưa request thật vào backend, không điều khiển UI app vận hành.

Native Firebird/testhost không nạp được DLL trong sandbox, nên các lệnh thử được chạy ngoài sandbox với path DB thử cụ thể. NuGet restore dùng cache local sau lỗi DNS; các build phục vụ kiểm tra cuối đều **0 warnings / 0 errors**. Không thay đổi setting máy để chữa lỗi môi trường.

## 2. Lỗi đã tái hiện và điều chỉnh kết luận GĐ2

Các ID bản ghi dưới đây lấy từ **lần chạy sạch cuối**, chỉ có ý nghĩa trong DB test tương ứng.

| Mục | Thao tác và số đo thực | Kết luận runtime |
| --- | --- | --- |
| MF-01 / T01 | Thu receivable 10.000.000, CASH 4.000.000 + BANK 6.000.000; settlement18, debt1. Voucher hiệu lực DISTINCT và allocations đều 10.000.000; tổng biến động KPI quỹ+bank **0** | Bỏ sót số dư đối với phiếu MIXED đã commit được xác nhận qua nhánh receivable |
| MF-01 / T02 | Thu đơn32 bằng hai allocation BANK: 4.000.000 vào bank3, 6.000.000 vào bank4; settlement20 | Header ghi toàn bộ 10.000.000 vào bank3; phân bổ theo bank sai. Đây là API bulk, UI hiện chỉ một bank |
| MF-02 / T05 | Thu đơn33 5.000, hủy voucher41; settlement21 còn POSTED, ORDER_PAYMENTS còn hiệu lực 5.000, voucher hiệu lực=0 | Mất đồng bộ owner sau hủy phiếu chung |
| MF-02 / T05 bổ sung | Trả payable18 bằng repository trực tiếp rồi hủy voucher65 | Payable vẫn PAID. Xác nhận cả đường không qua settlement |
| MF-03 / T06 hai thứ tự | Checkout đơn34/35: bán 10.000.000, thu 4.000.000, nợ6.000.000. Tổng phải thu trên query **12.000.000**. Thu theo đơn rồi receivable và ngược lại | Cả hai thứ tự đều ghi tổng tiền **16.000.000 cho đơn10.000.000**. Đảo từng settlement chạy được; vẫn cần sửa nguồn số dư/loại trùng, không chỉ query |
| MF-04 / T07 | Checkout21: bán10.000.000, bù vàng cũ7.000.000, thu3.000.000, nợ0. Query khoản mở đưa ra7.000.000; service nhận thu tiếp toàn bộ | Thu vượt phần chưa thanh toán được tái hiện bằng production checkout → production debt service |
| MF-05 / T08 | Đảo settlement có payment reference null / chuỗi rỗng / BANK-GD3 | Null: payment được void. Rỗng hoặc mã bank riêng: payment3.000 vẫn hiệu lực, voucher hiệu lực=0. API đảo chưa thấy caller UI trực tiếp |
| MF-06 / T04 | Manual CARD và checkout CARD đều vi phạm `CK_CV_METHOD`; metadata thực cho CASH/BANK_TRANSFER/MIXED | Tái hiện lỗi schema, cả hai transaction rollback, không có giao dịch tiền một phần |
| MF-07 / T09 | Phiên branch1 thu đơn40 của branch3; bổ sung thu receivable5 và trả payable19 của branch4 | Cả ba loại nguồn được cập nhật, settlement/voucher ghi branch1. Chưa thử tenant khác công ty; không mở rộng kết luận đó thành dữ liệu thật bị truy cập |
| MF-08 / T11 | Cài trigger luôn làm AUDIT_LOG insert lỗi. Tạo phiếu mới bị rollback; hủy voucher55 vẫn thành công | Hủy phiếu chung không đi qua audit bắt buộc được xác nhận thực tế |
| MF-09 / T03 | ViewModel chọn bank4 và gửi request, backend ghi bank3; request MIXED bị service từ chối | Bỏ lựa chọn bank được tái hiện xuyên qua contract UI/backend; chưa chạy thêm biến thể quỹ thứ hai bằng UI thật |
| MF-10 / T10 | Phiếu nhập98 → trả payable16 → đảo settlement34 → đảo phiếu nhập | Đã hoàn số dư nợ nhưng bị chặn bởi payment lịch sử với lỗi yêu cầu đảo thanh toán trước. API đảo, chưa thấy caller UI trực tiếp |

### Điều chỉnh quan trọng về MF-01: nhánh đơn mixed bị chặn trước commit

GĐ2 nêu đường mixed từ dialog tất toán → helper bulk theo đơn. Khi thử runtime, phần ghi voucher/allocations chạy trước, nhưng thêm `ORDER_PAYMENTS` với `PAYMENT_METHOD='MIXED'` vi phạm `CK_ORDER_PAYMENT_METHOD`; cả transaction rollback. Vì vậy **không dùng ca thu theo đơn mixed này để khẳng định một phiếu đã commit bị mất số dư**. Số dư sai MF-01 đã được xác nhận bằng nhánh **receivable mixed** và **đơn cùng phương thức khác tài khoản**. Đây là ví dụ cần phân biệt đường code đi tới với giao dịch thực commit được.

### MF-11 mới — Tất toán một đơn bằng nhiều phương thức bị constraint từ chối (P1)

Nhánh [ResolveOrderPaymentMethod](../../src/Kazuko.Infrastructure/Finance/FirebirdDebtWorkbenchRepository.cs#L1917) trả MIXED khi có nhiều phương thức. [Migration51](../../database/migrations/0051__sales_checkout_core.sql#L305) cho ORDER_PAYMENTS các mã CASH/BANK/BANK_TRANSFER/CARD/OTHER, không có MIXED. Ca T01 bổ sung trên đơn31 thu4.000 CASH +6.000 BANK bị `CK_ORDER_PAYMENT_METHOD` từ chối, rollback sạch. UI cho nhập được nhưng nghiệp vụ không hoàn tất. Không tự sửa constraint hoặc đổi MIXED thành một phương thức bất kỳ trong GĐ3.

### MF-12 mới — Đảo nguồn gia công cũ khi công nợ đã thu/chi một phần (P2, API legacy)

Ca T15: xuất thợ108 một đơn vị NVL, nhận lại109 gồm trả NVL0,8, mất0,2 thợ bồi thường1.000 và nợ công1.000. Thu bồi thường500 và trả công500 qua workbench. Sau đó `ReverseStockDocumentAsync(109)` **thành công**, nhưng payable17 và receivable4 vẫn PARTIAL, mỗi khoản còn500; tiền500 thu/chi vẫn có hiệu lực.

Guard [EnsureNoOpenDependentsAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L281) chỉ kiểm tra payment nợ nguồn đối với RECEIPT; nhánh CRAFT_RETURN_RECEIPT chỉ kiểm tra sản phẩm SOLD. Hai hàm [đảo nợ nguồn](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L427) chỉ hủy nợ OPEN chưa paid/collected. Kết quả nguồn kho bị hủy nhưng công nợ và thanh toán phụ thuộc còn hiệu lực.

Chưa tìm thấy UI production gọi API ghi gia công cũ hoặc đảo settlement trực tiếp. Giữ mức ưu tiên theo phạm vi legacy, không gọi đây là ca người dùng hiện đang thao tác qua một nút đã được xác minh.

### MF-13 mới — Đảo nhận lại NVL legacy không hoàn tồn đã cộng (P2, API legacy)

Cùng ca T15, service nhận lại cộng0,8 vào STOCK_BALANCES qua [ApplyMaterialBalanceChangeAsync](../../src/Kazuko.Infrastructure/Warehouse/FirebirdOutsourcedCraftService.cs#L401), nhưng không có stock movement tương ứng cho phần NVL trả/phế liệu này. Đảo nguồn trả holding về trạng thái trước nhưng [ReverseStockMovementsAsync](../../src/Kazuko.Infrastructure/Finance/FirebirdDocumentReversalService.cs#L319) không có movement đó để đảo. Đo tồn trước/sau đảo109: **delta0**, thay vì **-0,8** cho phần nhận lại. Không quy giá trị0,8 NVL thành tiền thất thoát; đơn vị/trọng lượng được kiểm riêng.

### Khoảng trống retry thu nợ đã kiểm chứng

Gửi **cùng một request** thu2.000 vào đơn43 hai lần sau lần đầu đã commit → hai ORDER_PAYMENTS, tổng4.000. Nếu client mất phản hồi rồi gửi lại, API không có request ID để phân biệt retry với khoản thu mới. Đây là bằng chứng thiếu dedupe ở cấp use case; chưa mô phỏng đứt TCP đúng lúc commit và không khẳng định UI hiện tự retry.

Hai phiên đồng thời thu hết đơn44 trị giá5.000: một thành công, một gặp Firebird concurrent update conflict; chỉ một khoản thu commit. **Khóa chống thu vượt do race đang có tác dụng trong ca này nhưng không giải quyết retry tuần tự khi vẫn còn nợ.**

## 3. Các cơ chế đã qua kiểm chứng

| Cơ chế | Bằng chứng |
| --- | --- |
| Checkout idempotency | Cùng request/hash trả CheckoutId25 cũ và AlreadyPosted=true; đổi payload cùng ID bị từ chối |
| Race checkout | Hai request khác ID bán cùng một serial: một commit, một concurrent update conflict |
| Audit rollback checkout | Trigger AUDIT_LOG lỗi → số lượng đơn/checkout/payment/settlement/voucher/kho/audit và trạng thái/version/vị trí sản phẩm thử không đổi |
| Audit rollback manual/workbench | Tạo phiếu hoặc thu nợ đã tới audit lỗi → không còn biến động được commit; hủy phiếu chung là ngoại lệ MF-08 |
| Nhập TP LSX | Bộ hiện có qua validation đủ món/dòng, audit rollback kho+nợ, request/hash/replay, serial chính thức, snapshot, scope đọc và race một LSX chỉ một receipt |
| HR thưởng/lương | CASH/NONE không nhập lại vào lương; PAYROLL cùng điều chỉnh tay tính lại đúng; chốt/hash stale/đủ số tiền; invalid fund không để lại PENDING/voucher; chi lặp trả phiếu cũ; hủy/chi lại, race chỉ một payment POSTED; chặn hủy riêng voucher HR và kiểm scope/quyền |
| HR audit | Bộ audit hiện có xác nhận award/payment/voucher/revision rollback cùng transaction khi audit lỗi |
| Unit calculator/validator | Chạy lại ngoài sandbox: **33 passed, 0 failed, 0 skipped**. Kết quả này thay thế trạng thái timeout môi trường ở GĐ2, không chứng minh các lỗi SQL đã sửa |

HR test có cả render Light/Dark/font90–150% của bộ có sẵn, không coi là thử toàn ứng dụng trên thiết bị thật. Chưa build toàn solution; các project phục vụ chạy được build và không có warning/error.

### Vàng: nợ dương / không nợ / vàng thừa

Harness tạo ba LSX riêng, mỗi lệnh giao2,5 chỉ, ghi nhập đủ một món:

| LSX / phiếu | Vàng trong món | Hao hụt xác nhận | Kết quả |
| --- | --- | --- | --- |
| 20 / 103 | 2,5 chỉ | 0 | Không tạo nợ, giữ snapshot |
| 21 / 105 | 3 chỉ | 0 | Vàng thừa0,5 chỉ, không tạo nợ; snapshot vẫn giữ GoldSettlements và món |
| 22 / 107 | 1 chỉ | 0,2 chỉ | Tạo nợ1,3 chỉ; trả0,3 rồi1 chỉ vào đúng khoản, dư về0 |

Trong ca nợ dương, so toàn bộ STOCK_BALANCES và số voucher trước/sau hai lần trả: **không đổi**. Xác nhận ghi trả nợ vàng hiện chỉ giảm sổ nợ, chưa tạo nhập vật chất hoặc tiền. Không tự áp FIFO, không chuyển vàng sang VND, không sửa hao hụt đã báo. Vàng thừa không mất snapshot; chưa có kết luận một sổ credit vàng thừa riêng phải phát sinh theo quy tắc nào.

## 4. Ma trận T01–T15 và giới hạn còn lại

| Nhóm | Đã chạy | Biến thể chưa chạy / giới hạn |
| --- | --- | --- |
| T01 | Receivable mixed đã commit; đơn mixed bị chặn/rollback | Không coi số dư0 của giao dịch đã rollback là MF-01 |
| T02 | API hai bank cùng phương thức | Hai quỹ; UI nhập nhiều bank chưa có |
| T03 | ViewModel chọn bank thứ hai → backend; service từ chối MIXED | Quỹ thứ hai, thao tác màn hình app thật |
| T04 | Metadata constraint, manual CARD và checkout CARD rollback | CARD qua payroll, nâng DB có CARD cũ từ51→74 chưa thử |
| T05 | Hủy phiếu thu settlement và payable trực tiếp; HR generic cancel bị chặn | Mọi source kho/HR category khác chưa vét cạn |
| T06 | Checkout tạo nợ, thu hai sổ cả hai thứ tự rồi đảo | Bán sỉ và CUSTOM còn cần regression tương ứng khi sửa owner chung |
| T07 | Trade-in production net dương đã trả đủ, thu vượt7tr | Net0/net âm có trong calculator/unit; chưa tái hiện toàn bộ runtime cho hai nhánh đó |
| T08 | Reference null/rỗng/mã bank riêng, đảo settlement | Dữ liệu legacy thiếu liên kết ID chưa thử |
| T09 | SalesOrder/ReceivableDebt/PayableDebt khác chi nhánh | Khác công ty chưa thử runtime |
| T10 | Nhập NVL → trả → đảo settlement → guard đảo nhập | Thanh toán cũ không settlement chưa có API đảo đầy đủ được thử |
| T11 | Audit fault manual/workbench/checkout; HR và owner từ bộ audit | Fault tại mọi câu SQL/commit, disk full hoặc DB engine crash chưa thử |
| T12 | Checkout replay/hash/race; debt retry tuần tự/race; HR/nhập TP retry/race | Không ngắt mạng vật lý/kill process lúc commit; bán sỉ/CUSTOM tạo mới/nợ vàng retry chưa thử |
| T13 | Bộ EmployeeMoney và Audit hiện có qua trên restore riêng | Phạm vi company và dashboard branch đọc theo code; chưa đối chiếu toàn bộ báo cáo thật |
| T14 | Nhập nợ dương/0/vàng thừa; snapshot; trả từng khoản một phần/đủ | Ca FIFO phân biệt nhiều khoản và mọi ngưỡng0,0001 chưa thử; không tự kết luận mất vàng |
| T15 | API legacy trả NVL, payable/receivable đã thu/chi một phần rồi đảo | Output serial đã bán/chuyển kho chưa thử; chưa tìm thấy caller UI ghi legacy |

Ma trận này là phạm vi thực thi, **không tuyên bố mọi biến thể trong kế hoạch GĐ2 đã chạy**. Đã đủ bằng chứng runtime để ưu tiên sửa các lỗi trên; những biến thể còn lại phải đưa vào regression khi triển khai bản sửa. GĐ4 vẫn là bước riêng để đo ảnh hưởng trên dữ liệu vận hành bằng truy vấn chỉ đọc.

## 5. Kết quả gốc và chạy lại

- [JSON lần chạy sạch: 26 kịch bản](D:/Working/artifacts/money-flow-gd3/G3_CF1E0122.results.json).
- [Log harness sạch](D:/Working/artifacts/money-flow-gd3/harness-clean.log).
- [Log nhập TP](D:/Working/artifacts/money-flow-gd3/production-run.log), [HR](D:/Working/artifacts/money-flow-gd3/hr-run.log), [audit-fault](D:/Working/artifacts/money-flow-gd3/audit-run.log), [unit](D:/Working/artifacts/money-flow-gd3/unit-run.log).
- Các lần thử đầu được giữ riêng trong cùng thư mục. Run1 có một `ERROR` do bất ngờ constraint MIXED của ORDER_PAYMENTS; **không dùng run1 làm bằng chứng hoàn tất T01**. Run sạch cuối đã tách ca MF-11, kiểm chứng MF-01 bằng nhánh commit được và chạy đủ26 ca.
- Giữ DB thử để tra bằng ID trong JSON; dữ liệu cố tình sai chỉ ở DB thử. Không copy ngược DB hoặc chạy script seed/fault lên vận hành.

Lệnh harness (DB phải restore riêng trước, schema91, path qua guard):

```powershell
dotnet build tests/Kazuko.MoneyFlow.IntegrationChecks/Kazuko.MoneyFlow.IntegrationChecks.csproj --no-restore -p:BuildInParallel=false
dotnet tests/Kazuko.MoneyFlow.IntegrationChecks/bin/Debug/net8.0-windows/Kazuko.MoneyFlow.IntegrationChecks.dll D:\Working\artifacts\money-flow-gd3\test91_moneyflow_clean_20261028.fdb
```

Chạy lại toàn bộ sẽ thêm fixture mới; để so một baseline sạch nên restore sang **file mới**, không overwrite một DB đang có kết quả cần giữ. Có thể chọn ca bằng `--case=T08` hoặc nhiều `--case=`. Đây là **harness tái hiện**, một số assertions cố ý mong quan sát lỗi hiện tại; sau khi sửa nghiệp vụ phải đổi thành expectation đúng trước khi dùng làm regression gate.

Ưu tiên tiếp theo: GĐ4 chỉ đọc xác định ảnh hưởng, đặc biệt liên kết đơn–receivable, phiếu bị hủy riêng, allocations lệch nơi giữ tiền và công nợ nguồn đã hủy. Chưa có chỉ dẫn sửa hàng loạt hoặc bù tiền tự động.
