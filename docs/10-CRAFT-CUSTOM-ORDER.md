# Đơn đặt chế tác (CUSTOM) và gắn lệnh sản xuất

Tài liệu quyết định cho luồng khách đặt mẫu chưa có trong kho. Bổ sung `docs/07-PRODUCTION-MODULE-IMPLEMENTATION-PLAN.md`, `docs/08-PRODUCTION-INVARIANTS.md` và phần “chưa trong phạm vi” của checkout bán.

## 1. Quyết định đã chốt

- Phương án **C**: chứng từ **đơn đặt** trên `SALES_ORDERS` (`ORDER_TYPE = 'CUSTOM'`), thu **đặt cọc** bắt buộc (> 0), rồi gắn lệnh sản xuất nguồn `SALES_ORDER`.
- UI loại sản xuất: **Chế tác theo đơn** (DB vẫn `ORDER_SOURCE = 'SALES_ORDER'`).
- Tạo và đặt cọc đơn đặt tại **Bán hàng → Đơn đặt**; sau đó tạo lệnh SX, chọn đơn đã đủ điều kiện trong combobox. Form lệnh SX không thu tiền và không chọn phương thức thanh toán.
- **Báo giá đủ từ đầu:** mỗi mẫu = `(TL vàng × giá vàng/chỉ + tiền công + đá) × SL`. Cọc và phần còn theo `GRAND_TOTAL` này (không chỉ tiền công).
- Sau hoàn tất SX: đơn đặt → `READY`; thành phẩm chỉ vào kho khi ghi phiếu nhập kho riêng cho toàn bộ lệnh.
- **Giao hàng + thu phần còn**: tab **Đơn đặt** (Bán hàng).
- Tab **Giao NVL**: hiển thị **NVL cần cấp tối thiểu** (vàng theo Σ TL ước tính × SL từng mẫu) — gợi ý, không bắt buộc khớp định mức.

## 2. Trạng thái đơn đặt (`SALES_ORDERS.STATUS_CODE`)

| Mã | Ý nghĩa |
|---|---|
| `DEPOSITED` | Đã thu cọc; đủ điều kiện lưu/gắn lệnh SX |
| `IN_PRODUCTION` | Đã gắn lệnh sản xuất (kể cả nháp) |
| `READY` | SX hoàn tất, chờ giao / tất toán |
| `COMPLETED` | Đã giao (xuất TP) và chốt thanh toán phần còn |
| `PARTIALLY_DELIVERED` | Dự phòng (chưa dùng trong phase hiện tại) |
| `DRAFT` / `CANCELLED` | Không dùng cho SX |

## 3. Đặt cọc từ lệnh SX

Trong một transaction khi bấm **Đặt cọc**:

1. Sinh mã `DOCUMENT_TYPE = 'CRAFT_ORDER'` (prefix `DH`).
2. Insert `SALES_ORDERS` `CUSTOM` + khách + `GRAND_TOTAL` = tổng báo giá đủ (vàng + công + đá).
3. Insert `SALES_ORDER_ITEMS` từ dòng mẫu (`FINISHED_PRODUCT_ID` null) gồm TL ước tính, giá vàng/chỉ, công, đá.
4. Thu cọc qua settlement `RECEIPT` + `ORDER_PAYMENTS`.
5. Cọc > 0 và ≤ tổng báo giá; mặc định UI ≈ 30% tổng báo giá (sửa được).

Dòng mẫu lệnh (`PRODUCTION_ORDER_LINES`) lưu thêm TL vàng ước tính, giá vàng/chỉ, tiền đá/món để báo giá và gợi ý cấp NVL.

## 4. Lệnh sản xuất

- Lưu nháp nguồn `SALES_ORDER` bắt buộc đã có `SALES_ORDER_ID` (đã đặt cọc).
- Lưu nháp → đơn `IN_PRODUCTION`. Hoàn tất lệnh → đơn `READY`.
- Shortcut Bán hàng: `IProductionOrderEditorService.ShowCreate(SALES_ORDER)`.

## 5. Giao NVL — NVL tối thiểu

- Trên tab **Giao NVL**, panel **Cần cấp tối thiểu**: gộp theo loại vàng từ mẫu = `Σ(EstimatedWeightChi × PlannedQuantity)`.
- Hiển thị tồn hiện tại nếu map được NVL kho; nút **Thêm vào phiếu** (tuỳ chọn) đổ gợi ý vào dòng giao.
- Không chặn ghi sổ nếu số giao khác gợi ý (chưa có định mức BOM chính thức).

## 6. Giao hàng và tất toán (Bán hàng — tab Đơn đặt)

```text
Chọn khách → Thêm mẫu (TL vàng + giá + công + đá) → Đặt cọc
  → Lưu lệnh SX → Giao NVL (xem gợi ý tối thiểu) → QC → Hoàn tất
  → FINISHED_PRODUCTS → Đơn READY
  → Bán hàng → Đơn đặt → Giao hàng (+ thu phần còn)
  → COMPLETED
```

## 7. Permission

- `sales.craft_order.create` — tạo đơn đặt + thu cọc + giao/thu phần còn.
- `warehouse.finished.issue` — xuất TP.
- `production.order.create` — mở/tạo lệnh SX từ shortcut.

## 8. Ranh giới module

- Báo giá / cọc / giao đơn: `Kazuko.Core/Sales` + Infrastructure Sales; dòng mẫu SX lưu quote fields.
- Gợi ý NVL: UI module Sản xuất (không tạo bảng BOM).
