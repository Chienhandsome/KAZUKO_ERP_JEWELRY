# GĐ4 — Đối soát dòng tiền trên DATA.FDB

Ngày **28/10/2026**. Snapshot kết luận: **15:45:31 +07:00**, Firebird transaction **5815**, connection492; code **f0133f79bee38397530cd0270298617c5fc00958**, schema thực **92**.

Tiếp nối [GĐ1](2026-10-28-money-flow-map.md), [GĐ2](2026-10-28-money-flow-gd2-code-review.md), [GĐ3](2026-10-28-money-flow-gd3-runtime-checks.md). Phạm vi là `D:\Working\KAZUKO_ERP_JEWELRY\data\DATA.FDB`, mọi công ty/chi nhánh và toàn lịch sử còn trong các bảng được đọc; số liệu hiện có tiền đều thuộc company1/branch1. Không dùng DB GĐ3 làm dữ liệu vận hành.

## 1. Kết luận

**Có một chứng từ hiện hữu chịu lỗi MF-04:** đơn `MB26/00003`, ID29, đã thanh toán/bù trừ đủ, nhưng query của đường thu theo đơn vẫn đưa ra khoản có thể thu thêm **31.200.000 VND**. Chưa thấy giao dịch thu thừa khoản này được ghi trong snapshot.

Các phép đối soát tiền, settlement, liên kết phiếu, nguồn nợ, chi lương và sổ trả nợ vàng bên dưới **chưa phát hiện chênh lệch đã ghi sổ**. Hai lỗi constraint CARD và ORDER_PAYMENTS/MIXED vẫn hiện hữu trên schema92; không thể đếm các lần người dùng gặp lỗi từ dữ liệu đã rollback.

Đây không phải kết luận “không còn lỗi” hoặc “không thất thoát tiền thật”: 13 lỗi GĐ2/GĐ3 vẫn là rủi ro code đã kiểm chứng, nhưng nhiều nhánh chưa có dữ liệu tương ứng trong DATA.FDB. Chưa có kiểm quỹ, sao kê ngân hàng hay chứng từ bên ngoài để xác nhận tiền vật chất. Không cộng số dư tiền, công nợ, trọng lượng vàng hoặc giá trị kho thành một tổng thiệt hại.

## 2. Bằng chứng cho đơn29 — khoản đề nghị thu sai, chưa có thu thừa

| Thành phần | Giá trị / liên kết |
| --- | --- |
| Đơn bán | `MB26/00003`, SALES_ORDERS.ID29, RETAIL/COMPLETED |
| Checkout | SALES_CHECKOUTS.ID18, TRADE_IN/POSTED, ngày21/10/2026 |
| Tổng bán sau giảm giá | 31.300.000 VND, giảm giá0 |
| Mua vàng cũ / bù trừ | 31.200.000 VND; phiếu kho90, SALES_CHECKOUT_OFFSETS liên kết checkout18 → đơn29 |
| Tiền chênh đã thu | 100.000 VND qua settlement17 và payment của đơn; phiếu tiền còn POSTED |
| Nợ thực theo nguồn | `31.300.000 − 31.200.000 − 100.000 = 0` |
| Khoản query thu theo đơn đưa ra | `31.300.000 − 100.000 = 31.200.000`, vì bỏ bù trừ |
| Dữ liệu thu vượt hiện có | Không thấy payment/application bổ sung cho khoản31.200.000 này |

Đã chạy một SELECT độc lập chép công thức của backend và thu được `ID29, GRAND_TOTAL31300000, BACKEND_OFFERED31200000, OFFSET_AMOUNT31200000`. Không gọi thao tác thu và không tạo phiếu để kiểm chứng trên DATA.FDB. Khả năng service nhận thu phần bù trừ đã được tái hiện riêng ở GĐ3.

**Ưu tiên sửa:** query danh sách khoản mở, metadata ledger và kiểm tra ghi sau khóa phải cùng trừ offset hiệu lực. Chưa có căn cứ để xóa/sửa payment100.000, xóa offset31.200.000 hoặc lập phiếu hoàn tiền. Tại thời điểm này đây là **một khoản đề nghị thu dư31,2 triệu**, không phải khoản thất thoát/thu thừa đã xác nhận.

## 3. Các phương trình đã khép trên snapshot

Đơn vị tiền trong bảng là **VND**, không rút gọn thành triệu/tỷ để tránh sai số. Các số dư là số ghi sổ trong DB, không phải số tiền đã kiểm đếm tại quầy/ngân hàng.

| Phép đối soát | Số đo | Kết quả |
| --- | --- | --- |
| Phiếu thu POSTED | 7 phiếu, 2.903.190.000 | Tất cả CASH, company1/branch1 |
| Phiếu chi POSTED | 11 phiếu, 1.103.133.333 | Tất cả CASH, company1/branch1 |
| Biến động tiền từ phiếu | Thu − Chi = **1.800.056.667** | SQL tổng độc lập khớp tính từ từng voucher |
| Nơi giữ tiền | Quỹ ID5:1.800.056.667; bank ID3:0 | Header và allocations khớp, chênh0; không có tiền không phân nơi giữ |
| Settlement | 7 POSTED, 7 allocations, 7 applications | Header = allocations = applications = voucher DISTINCT POSTED, không lệch |
| Đơn bán | 5 đơn, tổng2.934.390.000 | Payments2.903.190.000 + offset31.200.000 = tổng đơn; còn nợ0 |
| Khoản phải thu riêng | RECEIVABLE_DEBTS0, collections0 | Chưa có bản ghi gây cộng trùng MF-03 trong snapshot |
| Nhập mua / mua vàng cũ | 15 phiếu RECEIPT | Tổng nguồn = trả tiền + nợ gốc + bù trừ phi tiền mặt, chênh0 ở cả15 |
| Phải trả tiền mua | 12 khoản OPEN, gốc/còn47.555.678.000, đã trả0 | Phương trình gốc = đã trả + còn nợ khớp; đây là nghĩa vụ ghi sổ, không phải tiền mất |
| Phải trả tiền công | 2 khoản OPEN, gốc/còn5.500.000, đã trả0 | Phương trình sổ nợ khớp; khách trả đủ không làm mất nghĩa vụ trả công |
| Chi lương | 5 khoản POSTED, tổng28.333.333 | Payment = voucher POSTED = thực nhận snapshot; không có chi hiệu lực trùng NV/kỳ |
| Thưởng/phạt | 0 khoản trong HR_EMPLOYEE_AWARDS | Không có dữ liệu để định lượng ảnh hưởng nhánh này |
| Nợ vàng / trả vàng | 3 khoản, 4 lần trả, tổng gốc15 chỉ / đã trả15 chỉ / còn0 | 9999:10 chỉ; 999:5 chỉ. Tổng payment khớp WEIGHT_PAID_CHI từng khoản |
| Credit | 0 PARTY_CREDITS | Không phát sinh chênh trong tập này |

Trong 15 phiếu nhập, phiếu90 là vàng cũ của checkout18: tổng31.200.000 = **offset31.200.000**, không có tiền chi và không tạo payable. Nếu chỉ so tổng phiếu kho với cash + payable sẽ báo sai một khoản31,2 triệu; bộ đối soát đã tách offset phi tiền mặt này.

Phiếu nhập57 ghi46.100.000.000 VND, toàn bộ vào payable; đây là phần lớn số phải trả tiền mua. Số này khớp nguồn trong DB nhưng **chưa có xác nhận nghĩa vụ với đối tác**. Không coi tính khớp nội bộ là đã chứng minh giá mua, tồn vật chất hoặc chứng từ bên ngoài đúng.

Các chi tiết từng đơn, từng phiếu nhập và số dư theo loại nằm trong JSON ở mục7. Dữ liệu xuất dùng ID/mã chứng từ, không xuất tên, điện thoại, số tài khoản hay nội dung nhật ký cá nhân.

## 4. Ánh xạ 13 lỗi code sang dữ liệu hiện có

“0 bản ghi” dưới đây nghĩa là không thấy dấu hiệu theo phép kiểm trên snapshot, không phải lỗi đã được sửa.

| Mục | Kết quả trên DATA.FDB | Phạm vi kết luận |
| --- | --- | --- |
| MF-01: bulk sai phân bổ quỹ/bank | 0 voucher header khác chi tiết; toàn bộ18 phiếu là CASH; 7 settlement mỗi cái chỉ1 allocation | Chưa có dữ liệu mixed/nhiều tài khoản kích hoạt lỗi; số dư khớp không phủ định lỗi bulk |
| MF-02: hủy riêng phiếu nguồn | 0 voucher CANCELLED, 0 settlement CANCELLED | Không thấy nguồn hiệu lực/phiếu hủy; chưa có thao tác hủy để đối chiếu lịch sử |
| MF-03: cộng/thu trùng đơn–receivable | RECEIVABLE_DEBTS0; 0 overlap; 0 overcollection candidate | 5 đơn đều đủ tiền/bù trừ; chưa định lượng thiệt hại do nhánh này |
| MF-04: bỏ offset khi thu đơn | **1 đơn ID29, đề nghị thu dư31.200.000** | Có ảnh hưởng dữ liệu hiện tại; chưa có thu vượt đã ghi |
| MF-05: đảo sót payment reference riêng | 0 payment active gắn settlement/voucher hủy | Không có bản ghi đảo nguồn trong snapshot |
| MF-06: CARD bị constraint chặn | `CK_CV_METHOD` vẫn chỉ CASH/BANK_TRANSFER/MIXED | Lỗi cấu hình/schema còn tồn tại; không có CARD đã commit, không đếm được số lần thất bại |
| MF-07: tất toán sai scope nguồn | 0 application lệch company/branch đích; 0 voucher lệch scope quỹ/bank | Đã kiểm actual links, không suy từ UI. Nhánh thiếu guard vẫn chưa được sửa |
| MF-08: hủy thiếu audit | Không có voucher hủy | Chưa có bản ghi cần xác minh audit hủy. Không báo “thiếu audit” chỉ vì audit nằm ở owner khác |
| MF-09: bỏ bank/quỹ được chọn, manual MIXED | 0 voucher nguồn MANUAL | Không có ảnh hưởng đã ghi trong nhóm này; ý định tài khoản từng chọn không được lưu nên không thể dựng lại chỉ từ DB |
| MF-10: payment lịch sử chặn đảo kho | PAYABLE_DEBT_PAYMENTS0; DOCUMENT_REVERSALS0 | Không có chuỗi trả → đảo settlement → đảo nhập để đo ảnh hưởng |
| MF-11: MIXED bị ORDER_PAYMENTS chặn | `CK_ORDER_PAYMENT_METHOD` vẫn không cho MIXED | Lỗi schema/API còn tồn tại; giao dịch rollback không để lại dòng payment để đếm |
| MF-12: đảo nguồn legacy giữ nợ đã thu/chi | STOCK_CRAFT_SETTLEMENT_LINES0; 0 nợ nguồn đã hủy | Không thấy dữ liệu nhánh legacy nhận lại kích hoạt lỗi |
| MF-13: đảo legacy không hoàn tồn nhận lại | 0 dòng quyết toán legacy nhận lại | Không có trọng lượng/số lượng lịch sử để lượng hóa lỗi này |
| Khoảng trống retry thu nợ | 0 candidate payment cùng đơn/số tiền/phương thức/thời điểm | Heuristic không chứng minh ý định; không có request ID nên cũng không thể chứng minh mọi giao dịch là không retry |

Đã kiểm thêm: phiếu tiền gắn nguồn hủy, phiếu direct bị hủy riêng, nợ nguồn kho đã hủy, tổng tiền direct payment/allocations với voucher, số dư collection hiệu lực với sổ receivable, số dư credit và payment vàng. Không thấy ngoại lệ trong tập đọc.

## 5. Cách đọc và kiểm chứng độ tin cậy

- Công cụ [ReadOnlyAudit](../../tools/Kazuko.MoneyFlow.ReadOnlyAudit/Program.cs) **không gọi application service**, không seed, không tạo trigger, không migration; chỉ SELECT. Guard nhận đúng DATA.FDB của workspace hoặc DB GĐ3 khi có `--test`, không lưu connection profile.
- Firebird bắt đầu transaction `Read | Concurrency | Wait`; `MON$TRANSACTIONS.MON$READ_ONLY` của CURRENT_TRANSACTION được kiểm tra **=1** trước khi đọc. Tất cả dữ liệu của một lần chạy nằm trong cùng snapshot; kết thúc rollback transaction đọc.
- Không cộng settlement + allocation + voucher thành ba khoản tiền. Tổng voucher dùng ID DISTINCT; chia quỹ dùng allocation nếu có, header cho phiếu trực tiếp. Công nợ khách dùng ID nguồn đơn; offset tách riêng. Khi xem ứng dụng nhiều đích, không nhân cả tổng voucher cho từng đơn.
- Đã chạy bộ đối soát trên DB GĐ3 có lỗi biết trước và kiểm **10 điều kiện oracle**, đều true: phát hiện đúng2 voucher phân bổ sai, settlement21 bị hủy riêng phiếu, thu vượt của đơn36,2 payment chưa void sau đảo,3 nguồn sai chi nhánh,2 khoản nợ legacy và1 dòng tồn legacy. Nhờ đó kết quả0 trên DATA.FDB không phải vì bộ kiểm tra rỗng/không chạy.
- **19 checksum migrations74–92 đều khớp file local**. Schema92 chỉ bổ sung idempotency tạo LSX, không sửa hai constraint tiền đang lỗi. Chưa kiểm checksum migrations1–73 trong lượt này.
- Một lần đọc bổ sung gặp sai tên cột checksum (`CHECKSUM` thay vì `CHECKSUM_VALUE`); lần đó không được coi hoàn tất. Đã chỉnh theo pipeline, kiểm lại trên DB thử rồi chạy snapshot kết luận đầy đủ. Kết quả dùng cho báo cáo là `live-complete/results.json`, `metadata.errors=[]`.
- Build công cụ:0 warnings/0 errors. Không build hay sửa ứng dụng nghiệp vụ.

### Trạng thái file DB trong Git

Trước mở DB ở GĐ4, Git chưa đánh dấu DATA.FDB; sau các kết nối đọc, Git đánh dấu file binary này thay đổi. **Transaction READ ONLY bảo vệ khỏi ghi dữ liệu nghiệp vụ, không bảo đảm byte của file Firebird bất biến**: engine có quản lý transaction/header, đồng thời app khác có thể dùng DB. Lượt này không thực hiện DML/DDL hoặc rollback file trên đĩa. Giữ nguyên DATA.FDB hiện tại, không `git restore` hoặc chép đè file vì có thể làm mất trạng thái/cập nhật khác của DB.

## 6. Giới hạn và việc cần làm trước khi sửa dữ liệu

1. **Không có căn cứ sửa tiền lịch sử cho đơn29.** Ưu tiên sửa tính nợ và guard thu theo đơn, rồi regression trade-in net dương/0/âm trên DB test; không tạo “phiếu bù”31,2 triệu.
2. Sửa owner hủy/scope/nguồn công nợ chuẩn và hai constraint theo thứ tự trong GĐ2/GĐ3. Nếu cần migration mới, giữ nguyên checksum artifact đã áp dụng. Lượt này chưa sửa source nghiệp vụ hoặc chạy migration lên DATA.FDB.
3. Số dư1.800.056.667 là thu–chi còn trong DB, **chưa phải biên bản kiểm quỹ**; thiếu tiền mặt đếm thực, sao kê, đầu kỳ được xác nhận, phí và thời điểm nhận tiền thẻ. Chưa thể kết luận thiếu/thừa quỹ ngoài đời.
4. Trả nợ vàng4 lần/tổng15 chỉ khớp sổ nợ nhưng hàm trả vàng không tự tạo nhập vật chất. Chưa chứng minh có/không phiếu nhập độc lập hoặc vàng đã được nhận ngoài đời; không tự nhập thêm15 chỉ hay quy đổi thành VND.
5. Chưa đối soát toàn bộ BOM/QC/giá vốn/movement vật chất hay khôi phục dữ liệu đã xóa. [Audit hai đơn chế tác ngày19/10](2026-10-19-craft-orders-000001-000002.md) có các vấn đề sản xuất/định giá riêng; kết quả tiền của GĐ4 không đóng hoặc phủ định chúng. Nội dung lịch sử của audit đó chưa được tái xác nhận toàn bộ trong lượt này.
6. Không suy số lần lỗi CARD/MIXED đã xảy ra hoặc ý định quỹ từng chọn từ việc không có bản ghi commit. Muốn đo phần này cần log lỗi/client hoặc chứng từ ngoài DB, trong một phạm vi kiểm tra riêng.

## 7. Đầu ra có thể tra lại

- [Kết quả snapshot kết luận](D:/Working/artifacts/money-flow-gd4/live-complete/results.json): metadata, counts, constraint,16 nhóm kiểm tra, ID/mã/giá trị của ngoại lệ và baseline.
- [SELECT nghiệp vụ đã dùng](D:/Working/artifacts/money-flow-gd4/live-complete/selects.sql) và [log chạy](D:/Working/artifacts/money-flow-gd4/live-complete.log). Các câu đọc metadata/monitoring nằm trong source công cụ, không chạy file SQL này một cách rời rạc rồi coi như cùng snapshot.
- [Oracle validation](D:/Working/artifacts/money-flow-gd4/oracle-validation.json), [kết quả DB thử](D:/Working/artifacts/money-flow-gd4/oracle-final/results.json).
- [Hướng dẫn chạy công cụ](../../tools/Kazuko.MoneyFlow.ReadOnlyAudit/README.md).

GĐ4 kết thúc ở việc xác định ảnh hưởng và bằng chứng: **1 đơn có khoản đề nghị thu dư31,2 triệu; chưa thấy thu vượt đã ghi**, các phương trình đã liệt kê không có lệch nội bộ khác. Không có lệnh sửa hàng loạt, xóa nợ, hoàn tiền hoặc tự ghi thêm vàng trong đầu ra này.
