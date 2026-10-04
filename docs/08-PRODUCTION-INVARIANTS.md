# Invariant nghiệp vụ Sản xuất

Tài liệu quyết định cho module `Kazuko.Production`. Mọi implementation phải tuân thủ `docs/07-PRODUCTION-MODULE-IMPLEMENTATION-PLAN.md` và tài liệu này. Nếu mâu thuẫn với code thì cập nhật tài liệu cùng commit.

## 1. Phạm vi lệnh

- Nguồn lệnh: `STOCK` (không gắn đơn bán) hoặc `SALES_ORDER` (đúng một `SALES_ORDERS.ID`).
- Một đơn bán chỉ gắn tối đa **một** lệnh sản xuất (kể cả đã hủy). Unique filtered index trên `SALES_ORDER_ID`.
- Chỉ chọn đơn bán đã xác nhận; không `DRAFT` / `CANCELLED`.
- Hình thức: `IN_HOUSE` (1–nhiều `HR_EMPLOYEES`) **hoặc** `OUTSOURCED` (đúng một `EXTERNAL_WORKERS`). Không trộn.
- Không lập BOM dự kiến, không ngày dự kiến hoàn thành, không quy trình công đoạn.

## 2. Trạng thái lệnh (header)

| DB `STATUS_CODE` | UI hiển thị |
|------------------|-------------|
| `DRAFT` | Bản nháp / đang soạn (không phải cột trạng thái chính) |
| `IN_PROGRESS` | Đang làm |
| `COMPLETED` | Hoàn thành |
| `Hủy` | Hủy |

Không thêm trạng thái nghiệp vụ khác trên header. Sau ghi sổ giao NVL: `DRAFT` → `IN_PROGRESS`.

## 3. Giao / nhận nguyên liệu

- Mỗi lệnh tối đa **một** phiếu giao NVL active và **một** phiếu nhận hàng chính active (unique filtered index + kiểm tra trạng thái).
- Giao: toàn bộ NVL trong một transaction; thiếu tồn → rollback toàn bộ.
- Giá vàng kho và giá vốn đá/phụ kiện **snapshot lúc xuất**; QC/hoàn tất không đọc lại giá hiện hành.
- Cân bằng từng dòng balance trước hoàn tất:

  `đã giao = vào thành phẩm + trả lại + phế liệu thu hồi + hao hụt`

- Không dùng `EXTERNAL_WORKER_HOLDINGS` cho lệnh nội bộ mới.

## 4. QC và thành phẩm

- Đơn vị QC tạo đúng `PLANNED_QUANTITY` mỗi dòng mẫu **trong transaction nhận hàng chính**, chưa có serial.
- QC cấp món: `PENDING` / `REWORK_REQUIRED` / `PASSED` (UI: Chưa QC / Không đạt / Đạt).
- Fail: không `FINISHED_PRODUCTS`, không serial; lịch sử attempt bất biến.
- Hoàn tất chỉ khi mọi unit `PASSED`, số unit = tổng planned, balance cân bằng, phân bổ component không vượt `QUANTITY_IN_PRODUCT`.
- Trong **một** transaction hoàn tất: sinh serial, tạo đúng một `FINISHED_PRODUCTS` (quantity 1) / movement `IN` mỗi unit, gắn FK, phiếu nhập TP; `OUTSOURCED` tạo tối đa một `PAYABLE_DEBTS` (`DEBT_KIND=LABOR`, `ORIGIN_DOCUMENT_TYPE=PRODUCTION_ORDER`); `IN_HOUSE` không tạo công nợ.
- Idempotent: lệnh đã `COMPLETED` trả kết quả cũ, không tạo lại product/debt.
- Thất bại giữa chừng → rollback toàn bộ (không để serial/tồn/công nợ dở).

## 5. Giá thành bất biến

```text
Tổng = vàng thực tế (theo snapshot dòng xuất, phân bổ từng lô) 
      + đá/phụ kiện (qty × unit cost snapshot)
      + tiền công mỗi món (LABOR_COST_SNAPSHOT từ dòng mẫu)
```

`FINISHED_PRODUCTS.MATERIAL_COST`, `LABOR_COST`, `NON_GOLD_MATERIAL_COST`, `TOTAL_COST`, `GOLD_WEIGHT` phản ánh snapshot trên.

## 6. Hủy

- `DRAFT`: hủy trực tiếp.
- `IN_PROGRESS`: chỉ khi chưa nhập kho TP / chưa công nợ và NVL đã đối soát về 0.
- `COMPLETED`: không hủy bằng command thường (reveral ngoài scope đầu).

## 7. Phục hồi / migration

- Migration lõi **45**; execution/QC **46**; completion guards **47**. Không sửa checksum 30–44.
- Sau migration 45, trước 46: chạy `database/verification/report_production_order_id_orphans.sql` trên bản copy DB.
- Preflight FK `PRODUCTION_ORDER_ID` legacy (stock / finished / craft output): orphan → dừng migration, báo cáo; không tự xóa/nối sai. Xử lý thủ công rồi mới apply 46–47.
- Mỗi lệnh tối đa một chứng từ active loại `PRODUCTION_MATERIAL_ISSUE` / `PRODUCTION_RETURN_RECEIPT` / `PRODUCTION_FINISHED_RECEIPT` (unique filtered index).
- Mỗi lệnh thợ ngoài tối đa một `PAYABLE_DEBTS` với `ORIGIN_DOCUMENT_TYPE = 'PRODUCTION_ORDER'`.
- Recovery production: restore `.fbk` trước migration; không DROP thô khi đã có chứng từ.
