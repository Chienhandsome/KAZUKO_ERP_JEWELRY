# Invariant nghiệp vụ Nhập / Xuất kho

Tài liệu quyết định cho module Kho và các domain liên quan (công nợ phải trả, phiếu chi, thợ ngoài). Mọi implementation phải tuân thủ; nếu mâu thuẫn với code thì cập nhật tài liệu cùng commit.

## 1. Phân loại tồn kho

| Nhóm | Nhập thủ công | Tăng tồn | Xuất |
|------|---------------|----------|------|
| NVL (`MATERIALS`: GOLD / STONE / ACCESSORY) | Có (NCC hoặc thu mua vàng cũ) | Khi chứng từ `RECEIPT` POSTED | Có (thợ ngoài, sản xuất, điều chỉnh) |
| Thành phẩm (`FINISHED_PRODUCTS`) | **Cấm** | Chỉ từ hoàn thành lệnh chế tác / phiếu nhập chế tác hợp lệ | Chỉ theo đơn đặt chế tác đã xác nhận |

- Không tạo `STOCK_DOCUMENTS` loại `RECEIPT` với `PURPOSE_CODE = FINISHED_RECEIPT` nếu thiếu `PRODUCTION_ORDER_ID` hợp lệ.
- Điều chỉnh tồn thành phẩm dùng `STOCK_ADJUSTMENT` (quyền + audit), không dùng nhập kho thường.

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

Σ allocation = `PaidAmountVnd`. Mỗi allocation tạo một `CASH_VOUCHERS` (phiếu chi) khi POST, gắn `SOURCE_DOCUMENT_*` về phiếu nhập.

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
- `LABOR_AMOUNT_VND` → công nợ phải trả **tiền công** cho thợ (không gồm giá trị NVL đã giao).

## 8. Xuất thành phẩm theo đơn đặt chế tác

- Nguồn đơn: `SALES_ORDERS` ở pipeline đã xác nhận (không `DRAFT` / `CANCELLED`).
- Chỉ xuất phần đã hoàn thành (`FINISHED_PRODUCTS` gắn đơn, trạng thái tồn hợp lệ).
- Một đơn tối đa **một** phiếu `FINISHED_GOODS_ISSUE` còn hiệu lực (DRAFT hoặc POSTED).
- Không xuất thủ công không có đơn; không vượt tồn / phần hoàn thành.
- Khi POST: đánh dấu đơn đã xuất. Khi hủy đúng quy trình: hoàn tồn TP, bỏ đánh dấu, audit đầy đủ.

## 9. Transaction và đồng thời

- Post nhập NVL / thanh toán công nợ: tồn + công nợ + phiếu chi trong **cùng** transaction; lỗi bất kỳ → rollback toàn bộ.
- Ghi sổ dùng optimistic lock (`STATUS_CODE = 'DRAFT'` trên UPDATE); hai session không được post cùng chứng từ / cùng nguồn FG hai lần.

## 10. Quyền (permission keys)

| Key | Mục đích |
|-----|----------|
| `warehouse.material.read` | Đọc tồn / chứng từ |
| `warehouse.receipt.write` | Nháp / ghi sổ nhập NVL |
| `warehouse.receipt.post` | Ghi sổ nhập |
| `warehouse.craft.outsource` | Xuất / thu hồi thợ ngoài |
| `warehouse.craft.settle.override` | Thu hồi vượt lượng (điều chỉnh) |
| `warehouse.finished.issue` | Xuất thành phẩm theo đơn |
| `warehouse.document.reverse` | Hủy / đảo chứng từ đã ghi sổ |
| `debt.payable.read` / `debt.payable.pay` | Xem / thanh toán công nợ phải trả |
| `finance.fund.read` / `finance.bank.read` | Chọn quỹ / TKNH |
| `warehouse.worker.manage` | Danh mục thợ ngoài |
