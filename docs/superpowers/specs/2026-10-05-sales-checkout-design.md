# Thiết kế chức năng bán hàng tại quầy

**Ngày:** 2026-10-05

**Trạng thái:** Đã duyệt thiết kế trong hội thoại

**Phạm vi:** Module `Kazuko.Sales` trên WPF/.NET 8 và Firebird

## 1. Mục tiêu

Hoàn thiện mock UI Bán hàng thành một vertical slice có thể ghi sổ an toàn cho ba luồng:

1. bán thành phẩm có sẵn tại kho bán hàng;
2. bán thành phẩm kèm thu đổi vàng cũ;
3. chỉ mua vàng cũ từ khách.

Một lần hoàn tất phải ghi đồng bộ chứng từ bán, biến động thành phẩm, nhập kho vàng cũ, thu/chi tiền, công nợ và audit trong cùng một Firebird transaction. Không được để lại trạng thái ghi sổ dở dang.

## 2. Phạm vi và phần chưa làm

### Trong phạm vi

- Tra cứu hoặc quét mã thành phẩm đang có thể bán tại kho bán hàng của chi nhánh.
- Tính giá bán từ snapshot bảng giá, tiền công, tiền đá và giảm giá.
- Thu đổi hoặc mua vàng cũ, tự tính trọng lượng quy đổi và giá trị mua.
- Thanh toán hỗn hợp bằng tiền mặt, chuyển khoản và thẻ.
- Bán thiếu có kiểm soát và tự tạo khoản phải thu.
- Thu phần chênh hoặc chi phần chênh cho khách.
- Lịch sử chứng từ chỉ đọc, điều hướng Trước/Sau và in lại.
- Xem trước và in phiếu A4.
- Phân quyền, audit, idempotency và kiểm soát cạnh tranh khi bán cùng sản phẩm.

### Chưa trong phạm vi

- Trả hàng, bù trừ sau bán, hủy hoặc đảo chứng từ đã ghi sổ.
- Giữ đơn/gọi lại đơn và lưu nháp.
- Hóa đơn điện tử.
- Đơn đặt chế tác hoặc sản phẩm chưa có sẵn; luồng này sẽ tích hợp module Sản xuất sau.
- Khổ in A5, K80 và K58.

Tab hóa đơn điện tử hiện có trong mock UI được ẩn cho tới khi có contract tích hợp riêng. Không để control trông như hoạt động nhưng không ghi dữ liệu.

## 3. Các quyết định nghiệp vụ

### 3.1 Khách hàng và nhân viên

- Bán thông thường có thể dùng khách lẻ.
- Thu đổi và mua vàng cũ bắt buộc gắn khách hàng có hồ sơ; tối thiểu có họ tên và số điện thoại. Không được dùng khách lẻ.
- Nhân viên bán hàng mặc định là người đang đăng nhập.
- Người có quyền có thể chọn nhân viên bán hàng khác. Hệ thống luôn lưu riêng `SALESPERSON_ID` và `CREATED_BY`/người thao tác để audit.

### 3.2 Thành phẩm bán ra

- Chỉ nhận thành phẩm thuộc đúng chi nhánh, nằm trong kho có phạm vi bán hàng và có trạng thái cho phép bán.
- Một thành phẩm chỉ xuất hiện một lần trong checkout.
- Khi ghi sổ, service phải cập nhật trạng thái thành phẩm bằng điều kiện trạng thái hiện tại. Nếu số dòng cập nhật không bằng số sản phẩm yêu cầu, transaction thất bại với thông báo sản phẩm vừa được giao dịch ở máy khác.
- Thành phẩm chuyển sang `SOLD` và liên kết về đơn bán/dòng đơn bán trong cùng transaction.

### 3.3 Giá bán và điều chỉnh giá

- Khi thêm sản phẩm, lấy giá hiện hành từ Bảng giá và lưu snapshot. Thay đổi bảng giá sau đó không làm đổi dòng đã thêm.
- Dòng bán lưu cả giá nguồn và giá áp dụng.
- Chỉ người có quyền mới được sửa giá vàng, tiền công hoặc tiền đá.
- Mọi điều chỉnh phải có lý do và được audit với giá trị trước/sau, người sửa và thời điểm.
- Service tải lại dữ liệu nguồn cần thiết và kiểm tra quyền ngay trước khi commit; không tin dữ liệu quyền hoặc tổng tiền do UI gửi lên.

### 3.4 Vàng cũ

Đơn vị trọng lượng là **chỉ**. Công thức chuẩn cho mỗi dòng:

```text
Trọng lượng vàng = Tổng trọng lượng - Trọng lượng đá - Hao hụt/tạp chất
Trọng lượng quy đổi = Trọng lượng vàng x Phần trăm giá trị / 100
Thành tiền = Trọng lượng quy đổi x Giá mua mỗi chỉ
```

- Trọng lượng làm tròn tối đa 4 chữ số thập phân.
- Tiền làm tròn tới đồng theo `MidpointRounding.AwayFromZero`.
- Tổng trọng lượng, trọng lượng đá và hao hụt phải không âm.
- Tổng của trọng lượng đá và hao hụt không được vượt tổng trọng lượng.
- Phần trăm giá trị nằm trong khoảng 0–100.
- Trọng lượng vàng, trọng lượng quy đổi và thành tiền là trường chỉ đọc trên UI và luôn được tính lại ở service.
- Giá mua lấy snapshot từ Bảng giá. Chỉ người có quyền mới được sửa và phải nhập lý do.
- Số lượng món là thông tin mô tả/lô; không nhân thêm vào thành tiền vì tổng trọng lượng đã là trọng lượng của cả dòng.
- Mỗi dòng vàng cũ tạo một dòng nhập kho/lô truy xuất riêng, liên kết với checkout, khách hàng và dòng phiếu kho.
- Kho đích mặc định là kho nguyên liệu/vàng cũ của chi nhánh. Người có quyền có thể chọn kho đích hợp lệ khác.

### 3.5 Số tiền ròng, thanh toán và công nợ

```text
Tổng bán = tổng dòng thành phẩm - giảm giá
Tổng mua vàng cũ = tổng dòng vàng cũ
Số tiền ròng = Tổng bán - Tổng mua vàng cũ
```

- Số tiền ròng dương: khách phải trả cửa hàng.
- Số tiền ròng bằng 0: hai nghĩa vụ được bù trừ hoàn toàn, không tạo phiếu thu/chi.
- Số tiền ròng âm: cửa hàng trả phần chênh cho khách.
- Một checkout có nhiều dòng tiền mặt, chuyển khoản hoặc thẻ. Mỗi dòng chọn quỹ tiền hoặc tài khoản ngân hàng thích hợp; chuyển khoản/thẻ yêu cầu số tham chiếu khi cấu hình nghiệp vụ yêu cầu.
- Tổng phân bổ tiền thực cộng phần công nợ phải đúng bằng số tiền ròng dương.
- Chỉ khách hàng có hồ sơ và người có `sales.debt.approve` mới được bán thiếu. Phần thiếu tạo khoản phải thu loại `SALES`.
- Khách lẻ không được phát sinh công nợ.
- Với số tiền ròng âm và với giao dịch chỉ mua vàng cũ, đợt này phải phân bổ chi đủ trước khi hoàn tất; chưa hỗ trợ cửa hàng nợ tiền mua vàng cũ.
- Giá trị bán và giá trị mua vàng cũ được lưu gộp riêng. Việc bù trừ có bản ghi liên kết riêng và không được giả làm một phiếu thu/chi tiền mặt.

## 4. Kiến trúc

### 4.1 Thành phần

`Kazuko.Core.Sales` bổ sung các contract WPF-neutral:

- `SalesCheckoutDraft`, `SalesLineDraft`, `OldGoldLineDraft`;
- tái sử dụng `PaymentAllocationDraft` sau khi bổ sung phương thức thẻ;
- `SalesCheckoutResult`, DTO tra cứu sản phẩm/khách/nhân viên/lịch sử;
- `SalesCheckoutValidator` và các hằng permission;
- `ISalesCheckoutApplicationService`;
- `ISalesQueryRepository`;
- `ISalesAuthorizationService`;
- contract lấy dữ liệu in A4.

`Kazuko.Infrastructure.Sales` hiện thực truy vấn Firebird, ghi sổ checkout, kiểm tra quyền và lấy dữ liệu in.

`Kazuko.Modules.Sales` giữ View, ViewModel, dialog thanh toán và preview/in. Module không chạy SQL và không tham chiếu module nghiệp vụ khác.

`SalesModule` lấy dependency từ `IServiceProvider` và tạo ViewModel bằng constructor injection. Các service được đăng ký ở composition root hiện tại theo đúng hướng dependency của dự án.

### 4.2 Ranh giới transaction

`FirebirdSalesCheckoutApplicationService` mở đúng một connection và transaction cho toàn checkout. Luồng chuẩn:

1. kiểm tra idempotency key;
2. kiểm tra session/chi nhánh/quyền;
3. validate lại khách hàng, nhân viên, kho, quỹ/tài khoản và toàn bộ draft;
4. tải lại thành phẩm, snapshot giá và kiểm tra trạng thái;
5. tạo checkout header và mã chứng từ;
6. tạo đơn bán/dòng bán nếu có;
7. cập nhật có điều kiện các thành phẩm sang `SOLD` và ghi biến động xuất kho;
8. tạo phiếu nhập kho/dòng nhập/lô và cập nhật tồn vàng cũ nếu có;
9. tạo bản ghi bù trừ giữa nghĩa vụ bán và mua vàng cũ;
10. ghi settlement, phiếu thu/chi và liên kết dòng thanh toán thực;
11. tạo khoản phải thu còn lại nếu có;
12. ghi audit;
13. commit.

Các helper kho/tài chính dùng trong checkout phải nhận connection và transaction hiện hành. Không gọi tuần tự các public service tự mở transaction riêng. Những helper dùng chung được tách ở Infrastructure, không tạo dependency giữa `Kazuko.Modules.Sales` và module Kho/Tài chính/Công nợ.

Khi lỗi hoặc bị hủy trước commit, rollback toàn bộ. UI giữ nguyên dữ liệu để người dùng sửa và thử lại.

## 5. Mô hình dữ liệu

Migration mới chỉ tiến về phía trước; không sửa migration đã chạy.

### 5.1 `SALES_CHECKOUTS`

Bảng gốc thống nhất cho lịch sử màn hình bán hàng:

- khóa công ty/chi nhánh và mã chứng từ;
- `CHECKOUT_TYPE`: `SALE`, `TRADE_IN`, `OLD_GOLD_PURCHASE`;
- khách hàng, nhân viên bán hàng, ngày chứng từ;
- tổng bán, tổng vàng cũ, giảm giá và số tiền ròng có dấu;
- liên kết nullable tới `SALES_ORDERS` và phiếu nhập vàng cũ;
- trạng thái `POSTED` trong phạm vi hiện tại;
- `CLIENT_REQUEST_ID` duy nhất theo chi nhánh để chống bấm Thanh toán hai lần;
- người tạo và thời gian tạo. Checkout đã `POSTED` là bất biến nên không cần row version để sửa.

Mã dùng sequence theo chi nhánh và năm:

- `MByy/xxxxx` cho `SALE` và `TRADE_IN`;
- `MVCyy/xxxxx` cho `OLD_GOLD_PURCHASE`.

Các phiếu kho và tài chính phụ vẫn có mã riêng và liên kết về checkout.

### 5.2 Dòng bán

Tiếp tục dùng `SALES_ORDERS` và `SALES_ORDER_ITEMS`. Migration bổ sung liên kết/snapshot còn thiếu gồm:

- liên kết thành phẩm ổn định hoặc constraint tương đương qua `FINISHED_PRODUCTS`;
- giá vàng nguồn, giá vàng áp dụng;
- tiền công nguồn, tiền công áp dụng, tiền đá nguồn và tiền đá áp dụng;
- cờ điều chỉnh và lý do điều chỉnh.

`GOLD_UNIT_PRICE`, `LABOR_AMOUNT`, `STONE_AMOUNT` và tổng tiền trên dòng là giá trị đã áp dụng tại thời điểm giao dịch và không bị thay đổi bởi bảng giá tương lai.

### 5.3 `SALES_OLD_GOLD_LINES`

Mỗi dòng lưu:

- checkout, số dòng, material/loại vàng và dòng phiếu kho;
- số lượng món;
- tổng trọng lượng, trọng lượng đá, hao hụt/tạp chất, trọng lượng vàng;
- phần trăm giá trị và trọng lượng quy đổi;
- giá mua nguồn, giá mua áp dụng, thành tiền;
- cờ/lý do điều chỉnh giá và ghi chú.

Database có check constraint cho giá trị không âm, phần trăm 0–100, quan hệ trọng lượng hợp lệ và unique `(CHECKOUT_ID, LINE_NUMBER)`.

### 5.4 Bù trừ và dòng tiền

- `SALES_CHECKOUT_OFFSETS` lưu số tiền đã cấn giữa đơn bán và phiếu mua vàng cũ. Bản ghi này không sinh `CASH_VOUCHERS`.
- Tiền thực dùng `FINANCIAL_SETTLEMENTS`, allocations và `CASH_VOUCHERS` hiện có.
- Bổ sung `CARD` vào allow-list phương thức thanh toán và constraint liên quan; thẻ liên kết tài khoản nhận tiền phù hợp.
- `ORDER_PAYMENTS` tiếp tục phản ánh khoản thanh toán thực của đơn bán và được liên kết với settlement/voucher để các truy vấn khách hàng hiện có không đếm đôi.
- Truy vấn tổng đã thanh toán/còn nợ phải tính rõ tiền thực, bù trừ vàng cũ và khoản phải thu; không suy diễn bù trừ thành tiền mặt.

## 6. Hành vi giao diện

- Giữ bố cục mock hiện tại với hai workspace **Bán & thu đổi** và **Mua vàng cũ**.
- Dữ liệu mock hard-code được thay bằng truy vấn async có loading, empty state và lỗi thân thiện.
- Nhập/quét mã sản phẩm thêm đúng một thành phẩm có thể bán. Dòng trùng bị từ chối.
- Có ít nhất một dòng vàng cũ trong workspace bán thì checkout là `TRADE_IN`; nếu không là `SALE`.
- Form vàng cũ tự tính ba trường dẫn xuất ngay khi dữ liệu đầu vào thay đổi.
- Nút **Thanh toán** mở dialog tóm tắt tổng gộp, số ròng, các dòng thanh toán và công nợ.
- Trong lúc ghi sổ, disable lệnh gây lặp và hiển thị trạng thái xử lý. `CLIENT_REQUEST_ID` vẫn là hàng rào cuối ở database/service.
- Thành công: reset workspace nhập liệu và mở preview A4.
- Thất bại: không reset; focus tới lỗi có thể sửa khi xác định được.
- Trước/Sau tải các checkout đã `POSTED` theo thứ tự ngày/id, ở chế độ chỉ đọc và có lệnh in lại.
- Không hỗ trợ sửa hoặc xóa chứng từ đã ghi sổ trong phạm vi này.

## 7. In A4

- Tạo document in từ DTO bất biến tải lại theo checkout id; không chụp bitmap của cửa sổ.
- Preview thể hiện thông tin cửa hàng/chi nhánh, mã/ngày, khách hàng, nhân viên bán hàng, dòng thành phẩm, dòng vàng cũ, tổng gộp, phần bù trừ, dòng thanh toán, công nợ và chữ ký khi mẫu yêu cầu.
- Sau khi ghi sổ, preview có **In** và **Đóng**.
- Lịch sử cho phép in lại đúng dữ liệu snapshot đã ghi, không dùng giá hiện hành.
- Chỉ hỗ trợ A4 trong giai đoạn này. Renderer phải tách khỏi ViewModel để có thể thêm A5/K80/K58 sau mà không đổi contract checkout.

## 8. Quyền và audit

Các mã quyền:

- `sales.checkout.post`
- `sales.price.override`
- `sales.debt.approve`
- `sales.salesperson.change`
- `sales.history.read`
- `sales.receipt.print`

WPF hiện có hằng permission nhưng chưa có evaluator dùng chung đầy đủ. Phạm vi này bổ sung `ISalesAuthorizationService` đọc `ROLE_PERMISSIONS`; `ADMIN` được xử lý theo quy ước hiện có. UI dùng quyền để enable/disable, còn application service luôn kiểm tra lại trước khi ghi.

Audit tối thiểu:

- checkout đã ghi với các id chứng từ con và tổng gộp;
- override giá với trước/sau và lý do;
- bán thiếu và người phê duyệt;
- thay nhân viên bán hàng;
- lỗi cạnh tranh thành phẩm ở log kỹ thuật, không ghi dữ liệu nhạy cảm quá mức.

## 9. Xử lý lỗi và bất biến

- Mọi tổng tiền và công thức được tính lại ở Core/Infrastructure bằng `decimal`.
- Không cho số âm ở các thành phần đầu vào; chỉ `NET_AMOUNT` được có dấu.
- Mọi entity tham chiếu phải thuộc đúng công ty/chi nhánh hoặc nằm trong scope được phép.
- Tiền thực không được lớn hơn nghĩa vụ cần thu/chi, ngoại trừ số khách đưa bằng tiền mặt dùng để tính tiền thối; voucher chỉ ghi số thực giữ lại.
- Tổng allocations của settlement phải bằng tiền thực cần thu/chi.
- Mỗi allocation chỉ chọn đúng loại nguồn tiền: tiền mặt cần quỹ; chuyển khoản/thẻ cần tài khoản.
- Không tạo cash voucher cho bù trừ vàng cũ.
- Unique idempotency key bảo đảm cùng yêu cầu chỉ trả lại kết quả cũ hoặc bị từ chối an toàn, không ghi lần hai.
- Timeout, lỗi database, lỗi máy in và lỗi validation có thông báo khác nhau. Lỗi in không rollback checkout đã commit.

## 10. Kiểm thử và tiêu chí hoàn thành

### Unit test

- Công thức vàng cũ và quy tắc làm tròn.
- Tổng bán, tổng mua, số tiền ròng và bù trừ.
- Validation trọng lượng, giá, khách hàng, kho, thanh toán và công nợ.
- Quyền override giá, bán nợ và đổi nhân viên.
- ViewModel: command state, loading/error, giữ form khi lỗi và reset khi thành công.

### Integration test Firebird

- Bán thường thanh toán đủ.
- Bán thường thanh toán hỗn hợp.
- Bán thiếu hợp lệ và từ chối bán thiếu cho khách lẻ/không có quyền.
- Thu đổi còn phải thu khách.
- Thu đổi cân bằng bằng 0.
- Thu đổi phải chi phần chênh cho khách.
- Chỉ mua vàng cũ.
- Override giá có/không có quyền và audit đúng.
- Rollback khi lỗi ở từng chặng kho/tài chính/công nợ.
- Hai transaction tranh cùng một thành phẩm chỉ có một giao dịch thành công.
- Gửi hai lần cùng `CLIENT_REQUEST_ID` không tạo chứng từ trùng.
- Lọc lịch sử theo chi nhánh và tải đúng snapshot để in.

### Kiểm tra UI và build

- Light/Dark; font 90%, 100%, 125%, 150%; DPI 100%, 150%, 200%; cửa sổ tối thiểu và maximized.
- Keyboard navigation, focus lỗi, dữ liệu rỗng, nội dung tiếng Việt dài và trạng thái loading.
- Preview/in A4 với ít/nhiều dòng và ngắt trang.
- `dotnet build KAZUKO_ERP_JEWELRY.sln` không warning/error.
- `dotnet test KAZUKO_ERP_JEWELRY.sln --no-build` thành công.

## 11. Triển khai migration và tương thích

- Migration mới có version tiếp theo, verification script và mô tả rollback dữ liệu.
- Seed sequence, permission và constraint bằng thao tác idempotent khi phù hợp.
- Không sửa dữ liệu lịch sử ngoài các backfill bắt buộc có giá trị xác định rõ.
- Các truy vấn cũ đọc `SALES_ORDERS`/`ORDER_PAYMENTS` phải tiếp tục hoạt động; những truy vấn tổng thanh toán được cập nhật có test chống đếm đôi.
- Module version tăng theo quy ước phát hành sau khi implementation hoàn tất; tài liệu này không tuyên bố pipeline cập nhật module production đã hoàn chỉnh.

## 12. Tiêu chí nghiệm thu nghiệp vụ

Tính năng được coi là hoàn tất khi người dùng có quyền có thể thực hiện cả ba luồng từ UI mock hiện tại, nhận preview A4, và quan sát dữ liệu nhất quán trong Bán hàng, Kho, Tài chính, Công nợ và audit. Mọi lỗi trước commit không được để lại chứng từ hoặc tồn kho/tài chính từng phần; lỗi in sau commit không được làm mất giao dịch.
