# Invariant nghiệp vụ Nhập / Xuất kho

Tài liệu quyết định cho module Kho và các domain liên quan (công nợ phải trả, phiếu chi, thợ ngoài). Mọi implementation phải tuân thủ; nếu mâu thuẫn với code thì cập nhật tài liệu cùng commit.

## 1. Phân loại tồn kho

| Nhóm | Nhập thủ công | Tăng tồn | Xuất |
|------|---------------|----------|------|
| NVL (`MATERIALS`: GOLD / STONE / ACCESSORY) | Có (NCC hoặc thu mua vàng cũ) | Khi chứng từ `RECEIPT` POSTED | Có (thợ ngoài, sản xuất, điều chỉnh) |
| Thành phẩm (`FINISHED_PRODUCTS`) | **Cấm** | Chỉ từ hoàn thành lệnh chế tác / phiếu nhập chế tác hợp lệ | Chỉ theo đơn đặt chế tác đã xác nhận |

- Không tạo `STOCK_DOCUMENTS` loại `RECEIPT` với `PURPOSE_CODE = FINISHED_RECEIPT` nếu thiếu `PRODUCTION_ORDER_ID` hợp lệ.
- Điều chỉnh tồn thành phẩm dùng `STOCK_ADJUSTMENT` (quyền + audit), không dùng nhập kho thường.

### 1.1. Thành phẩm là cá thể duy nhất

- Mỗi dòng `FINISHED_PRODUCTS` là đúng **một món**; `CODE`, `BARCODE_VALUE` và `QR_VALUE` duy nhất trong công ty (khi tạo thành phẩm từ sản xuất/chế tác, mặc định gán cả ba bằng mã serial).
- Không lưu tồn thành phẩm bằng một bản ghi có số lượng lớn hơn 1. Tổng số món dùng `COUNT(*)`.
- Vị trí vật lý nằm ở `LOCATION_ID`; vai trò kho nằm ở `LOCATIONS.INVENTORY_SCOPE`.
- Kho thành phẩm dùng scope `FINISHED_GOODS`; Kho bán hàng dùng scope `SALES_FLOOR`.
- Một món chỉ có một `LOCATION_ID` hiện hành. Chuyển kho là cập nhật vị trí, không sao chép bản ghi.
- `FINISHED_PRODUCT_DOCUMENT_LINES` lưu từng serial trên chứng từ; một serial không được lặp trong cùng chứng từ.
- `ROW_VERSION` bắt buộc tăng mỗi lần chuyển/xuất để phát hiện hai session cùng thao tác một món.

## 2. Đơn vị chuẩn

- Vàng / kim loại quý (`MATERIAL_TYPE = GOLD`): đơn vị tồn chuẩn **`CHI`**.
  - 1 lượng (`LUONG`) = 10 chỉ.
  - 1 chỉ = 3,75 gram.
- Đá / ngọc: `VIEN` hoặc `CARAT` (đã xác minh).
- Phụ kiện: `CAI` (hoặc gram nếu danh mục đã chuẩn hóa).
- Không trộn đơn vị trên cùng một dòng tồn/chứng từ nếu chưa quy đổi về đơn vị chuẩn.
- Migration đơn vị ghi audit trước/sau; dữ liệu không xác định được đơn vị vào `UNIT_CONVERSION_EXCEPTIONS` — **không đoán**.

## 3. Tiền tệ và giá trị chứng từ

- Công nợ và thanh toán ghi nhận bằng **VNĐ**.
- Mỗi dòng nhập NVL lưu: số lượng đơn vị chuẩn, đơn giá/đơn vị (vàng = đơn giá/chỉ), tỷ giá áp dụng, thành tiền VNĐ.
- Giá trị chứng từ **chốt lúc ghi sổ**; thay đổi tỷ giá sau đó không làm đổi chứng từ cũ.

## 4. Trạng thái chứng từ kho

| Status | Tồn / công nợ / quỹ |
|--------|---------------------|
| `DRAFT` | Không thay đổi |
| `POSTED` | Phát sinh nghiệp vụ trong một transaction |
| `CANCELLED` | Đảo (reversal) + audit; không xóa lịch sử |

Sau POSTED không sửa trực tiếp số liệu tài chính/tồn. Điều chỉnh bằng hủy/đảo có phân quyền.

### Hủy / đảo

- Mỗi chứng từ nguồn chỉ đảo **một lần** (`DOCUMENT_REVERSALS` unique + `STATUS_CODE = CANCELLED`).
- Chặn hủy khi còn nghiệp vụ phụ thuộc POSTED (ví dụ phiếu nhập chế tác từ phiếu xuất thợ; công nợ đã thanh toán; thành phẩm đã bán).
- Trong **một** transaction: đảo movement kho, hủy phiếu thu/chi liên quan, hoàn/hủy debt application hoặc settlement, ghi audit.
- Credit NCC/khách (`PARTY_CREDITS`) là số dư giữ lại để cấn — **không** ghi nhận thành tiền đã thu/đã chi và không tạo `CASH_VOUCHERS`.

## 5. Nhập NVL

### Nguồn (`SOURCE_TYPE`)

- `SUPPLIER`: bắt buộc `SUPPLIER_ID`; `CUSTOMER_ID` NULL. Cho phép tạo nhanh NCC qua `ISupplierEditorService`.
- `OLD_GOLD_CUSTOMER`: bắt buộc `CUSTOMER_ID`; `SUPPLIER_ID` NULL. Cho phép tạo nhanh KH qua `ICustomerEditorService`. Không dùng tên người bán tự do.

Mỗi phiếu đúng **một** đối tác. Không ghi sổ khi thiếu đối tác.

### Tổng tiền

- `TotalAmountVnd` = tổng thành tiền dòng.
- `PaidAmountVnd` ∈ [0, TotalAmountVnd].
- `DebtAmountVnd` = Total − Paid.

### Thanh toán nhiều phương thức

Collection `STOCK_DOCUMENT_PAYMENTS` (không hai field cứng):

- `PaymentMethod`: `CASH` | `BANK_TRANSFER`
- `AmountVnd`, `CashFundId` (bắt buộc nếu CASH), `BankAccountId` (bắt buộc nếu BANK_TRANSFER), `ReferenceNumber`

Σ allocation = `PaidAmountVnd`. Mỗi allocation tạo một `CASH_VOUCHERS` (phiếu chi) khi POST, gắn `SOURCE_DOCUMENT_*` về phiếu nhập và `SOURCE_ALLOCATION_*` về dòng `STOCK_DOCUMENT_PAYMENTS` (idempotency DB-level).

Thu/chi công nợ đi qua `FINANCIAL_SETTLEMENTS`: một lần nhận/trả tiền có thể nhiều phương thức (mỗi allocation → một phiếu) và cấn nhiều công nợ qua applications mà không nhân bản phiếu. Settlement + voucher + cập nhật công nợ cùng transaction; POSTED chỉ hủy/đảo.

### Công nợ

- `DebtAmountVnd > 0` → tạo `PAYABLE_DEBTS` (NCC hoặc khách bán vàng cũ).
- `DebtAmountVnd = 0` → không tạo dư nợ mở; vẫn lưu lịch sử thanh toán/phiếu chi.
- Thanh toán công nợ sau này trong module Công nợ; mỗi lần thanh toán có thể chia nhiều phương thức và tạo phiếu chi tương ứng.

## 6. Xuất NVL cho thợ ngoài

- Danh mục riêng `EXTERNAL_WORKERS` (không dùng HR / SUPPLIERS).
- Loại chứng từ `OUTSOURCED_CRAFT_ISSUE`: chọn thợ, ngày giao, ngày dự kiến thu hồi, người giao/nhận, dòng NVL, chỉ dẫn, lệnh chế tác tùy chọn.
- Khi POST: giảm tồn NVL + ghi `EXTERNAL_WORKER_HOLDINGS`.
- **Không** tạo công nợ giá trị NVL; **không** tạo phiếu thu/chi từ giá trị NVL giao.
- NVL chưa tiêu hao hoàn toàn đến khi quyết toán.

## 7. Nhập kho chế tác (`CRAFT_RETURN_RECEIPT`)

- Liên kết bắt buộc với phiếu xuất thợ.
- Không nhập vượt lượng chưa quyết toán nếu không có quyền điều chỉnh.
- Ghi: thành phẩm/BTP nhận lại, NVL dư, phế liệu, hao hụt thực tế / cho phép / chênh lệch.
- Công thức theo từng NVL: đã giao − cấu thành − dư − phế = hao; so với định mức → chênh lệch.
- Đây **không** phải nhập thành phẩm thủ công.
- Khi có `QuantityInProduct` > 0 phải có `CraftOutputLineDraft` gắn `ProductionOrderId` hoặc `SalesOrderId`; tạo/cập nhật `FINISHED_PRODUCTS`, ghi `STOCK_CRAFT_OUTPUT_LINES` và `FINISHED_PRODUCT_MOVEMENTS` (IN), tính giá vốn = NVL cấu thành + tiền công phân bổ.
- `LABOR_AMOUNT_VND` → công nợ phải trả **tiền công** cho thợ (không gồm giá trị NVL đã giao).

## 8. Xuất thành phẩm theo đơn đặt chế tác

- Nguồn đơn: `SALES_ORDERS` ở pipeline đã xác nhận (không `DRAFT` / `CANCELLED`).
- Chỉ xuất phần đã hoàn thành (`FINISHED_PRODUCTS` gắn đơn, trạng thái tồn hợp lệ).
- Một đơn tối đa **một** phiếu `FINISHED_GOODS_ISSUE` còn hiệu lực (DRAFT hoặc POSTED).
- Không xuất thủ công không có đơn; không vượt tồn / phần hoàn thành.
- Khi POST: đặt `STATUS_CODE = SOLD`, `LOCATION_ID = NULL`, ghi dòng chứng từ và movement; món đã bán không còn trong query tồn active.
- Không xóa `FINISHED_PRODUCTS` đã bán; hóa đơn/chứng từ/movement giữ lịch sử serial phục vụ báo cáo, bảo hành và kiểm toán.
- Khi hủy đúng quy trình: hoàn đúng vị trí và trạng thái trước xuất, bỏ đánh dấu đơn, tăng `ROW_VERSION`, ghi movement đảo và audit đầy đủ.

### 8.1. Chuyển Kho thành phẩm sang Kho bán hàng

- Chỉ chuyển món đang `IN_STOCK` tại location scope `FINISHED_GOODS`.
- Kho đích phải có scope `SALES_FLOOR`.
- POST trong một transaction: tạo phiếu `TRANSFER`, tạo một dòng/serial, cập nhật location, đổi trạng thái sang `DISPLAYED`, ghi hai movement OUT/IN và audit.
- Sau commit, query Kho thành phẩm không còn món đó; query Kho bán hàng mới hiển thị món đó.
- Query Kho bán hàng chỉ lấy `DISPLAYED` tại `SALES_FLOOR`; `SOLD` tuyệt đối không xuất hiện trong danh sách active.

### 8.2. Danh mục loại vàng (`GOLD_TYPES`) và bảng giá

- `GOLD_TYPES` là nguồn sự thật duy nhất cho loại/tuổi vàng (mã `750`, `999`…). Toàn hệ thống chọn loại vàng từ catalog này (ComboBox), không nhập tay mã lệch.
- Mỗi `GOLD_TYPES` active có đúng một dòng `GOLD_PRICES`, một dòng `WAREHOUSE_GOLD_PRICES` và một NVL `MATERIALS` (`MATERIAL_TYPE = GOLD`) gắn `GOLD_TYPE_ID`.
- Khi CRUD loại vàng: bắt buộc nhập giá bán lẻ (mua/bán) và giá kho (`ISSUE_PRICE_PER_CHI`) — tất cả > 0 — trong cùng transaction tạo/cập nhật bốn bản ghi trên.
- `WAREHOUSE_GOLD_PRICES` vẫn là bảng giá nội bộ xuất kho; `GOLD_PRICES` là bảng giá bán lẻ. Đổi một bên không tự đổi bên kia, nhưng cả hai phải luôn tồn tại cho mọi loại vàng active.
- Xuất NVL vàng dùng `ISSUE_PRICE_PER_CHI` resolve theo `GOLD_TYPE_ID` (fallback CODE); đá/phụ kiện dùng giá vốn tồn kho hiện hành.
- Giá trị xuất thành phẩm = `GOLD_WEIGHT × ISSUE_PRICE_PER_CHI + LABOR_COST + NON_GOLD_MATERIAL_COST`.
- Khi POST phải chốt ID bảng giá, giá/chỉ, trọng lượng vàng, tiền công và chi phí phi vàng trên dòng chứng từ. Thay đổi bảng giá sau này không được làm đổi chứng từ cũ.
- Loại vàng thiếu dòng giá hoặc giá ≤ 0 phải chặn ghi sổ với thông báo cấu hình bảng giá.
- “Vàng cũ” là **nguồn phiếu** (thu mua từ khách), không phải tên loại vàng trong danh mục.

## 9. Transaction và đồng thời

- Post nhập NVL / thanh toán công nợ: tồn + công nợ + phiếu chi trong **cùng** transaction; lỗi bất kỳ → rollback toàn bộ.
- Ghi sổ dùng optimistic lock (`STATUS_CODE = 'DRAFT'` trên UPDATE); hai session không được post cùng chứng từ / cùng nguồn FG hai lần.

## 10. Quyền (permission keys)

| Key | Mục đích |
|-----|----------|
| `warehouse.material.read` | Đọc tồn / chứng từ |
| `warehouse.receipt.write` | Nháp / ghi sổ nhập NVL |
| `warehouse.receipt.post` | Ghi sổ nhập |
| `warehouse.issue.write` / `warehouse.issue.post` | Lập / ghi sổ xuất NVL |
| `warehouse.transfer.write` / `warehouse.transfer.post` | Lập / ghi sổ chuyển kho |
| `warehouse.craft.outsource` | Xuất / thu hồi thợ ngoài |
| `warehouse.craft.settle.override` | Thu hồi vượt lượng (điều chỉnh) |
| `warehouse.finished.issue` | Xuất thành phẩm theo đơn |
| `warehouse.document.reverse` | Hủy / đảo chứng từ đã ghi sổ |
| `debt.payable.read` / `debt.payable.pay` | Xem / thanh toán công nợ phải trả |
| `finance.fund.read` / `finance.bank.read` | Chọn quỹ / TKNH |
| `warehouse.worker.manage` | Danh mục thợ ngoài |
| `priceboard.goldtype.write` | CRUD danh mục loại vàng thống nhất (`GOLD_TYPES`) |
