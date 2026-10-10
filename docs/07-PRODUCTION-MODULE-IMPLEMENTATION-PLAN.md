# Kế hoạch triển khai module Sản xuất

> Cập nhật 2026-10-09: Luồng nghiệp vụ đã đổi thành **tạo lệnh → hoàn thành → nhập kho thành phẩm**. Hoàn thành chỉ chốt QC, đối soát NVL, công nợ thợ ngoài và trạng thái đơn đặt. Nhập kho là thao tác riêng, chọn một lệnh `COMPLETED` chưa nhập, tạo toàn bộ serial/thành phẩm và một phiếu nhập trong một transaction. Form lệnh sản xuất dùng một màn hình cuộn với các phần mở/gập; không dùng chuỗi tab để thực hiện thao tác. Các đoạn kế hoạch cũ bên dưới mô tả nhập kho lúc hoàn tất hoặc UI dạng tab chỉ là lịch sử thiết kế; xem `docs/08-PRODUCTION-INVARIANTS.md` để áp dụng quyết định mới.

Tài liệu này là đặc tả triển khai cho Cursor. Đây là nguồn yêu cầu của module `Kazuko.Production`; không tự suy diễn thêm công đoạn, trạng thái hoặc cách tính giá ngoài nội dung bên dưới.

## 1. Kết quả cần đạt

Xây dựng một module Sản xuất độc lập, hỗ trợ hai nguồn lệnh:

- Sản xuất để nhập kho.
- Sản xuất theo một **đơn đặt** chế tác (`SALES_ORDERS.ORDER_TYPE = 'CUSTOM'`) đã xác nhận và đã thu cọc.

Chi tiết đơn đặt + cọc + giao hàng (Bán hàng tab Đơn đặt): `docs/10-CRAFT-CUSTOM-ORDER.md`.

Một lệnh sản xuất có nhiều mẫu, mỗi mẫu có số lượng lớn hơn hoặc bằng 1. Ví dụ: 5 nhẫn và 10 lắc. Thành phẩm không được quản lý theo số lượng gộp: khi hoàn tất, mỗi món đạt QC phải trở thành một dòng `FINISHED_PRODUCTS` riêng với serial duy nhất.

Module chỉ quản lý thông tin giao nguyên liệu, nhận lại hàng, QC và hoàn tất; không xây dựng quy trình công đoạn.

## 2. Quyết định nghiệp vụ đã chốt

### 2.1. Nguồn lệnh

- `STOCK`: sản xuất để nhập kho, không gắn đơn đặt.
- `SALES_ORDER` (UI: **Theo đơn đặt**): bắt buộc gắn đúng một `SALES_ORDERS.ID` loại `CUSTOM`.
- Một đơn đặt chỉ được gắn với tối đa một lệnh sản xuất, kể cả lệnh đã hủy. Muốn làm lại phải mở lại/khôi phục lệnh cũ, không tạo lệnh thứ hai.
- UI: chọn khách trên tab Thông tin lệnh; thêm mẫu + báo giá + **Đặt cọc** trên tab Mẫu sản phẩm (không dialog tạo đơn riêng). Chi tiết: `docs/10-CRAFT-CUSTOM-ORDER.md`.
- Lưu lệnh nguồn `SALES_ORDER` bắt buộc đã đặt cọc (`SALES_ORDER_ID`). Khi lưu: đơn → `IN_PRODUCTION`. Khi hoàn tất: đơn → `READY`.

### 2.2. Hình thức thực hiện

Mỗi lệnh chỉ dùng một trong hai hình thức:

- `IN_HOUSE`: chọn từ 1 đến nhiều nhân viên đang làm việc trong `HR_EMPLOYEES`; không sinh công nợ tiền công.
- `OUTSOURCED`: chọn đúng một thợ ngoài đang hoạt động trong `EXTERNAL_WORKERS`; khi hoàn tất mới sinh công nợ tiền công.

Không được trộn nhân viên nội bộ và thợ ngoài trong cùng một lệnh.

### 2.3. Mẫu và tiền công

- Một lệnh có 1-nhiều dòng mẫu.
- Mỗi dòng gồm tối thiểu: tên/mã mẫu, loại vàng, số lượng cần làm, tiền công cho **mỗi món**, mô tả và ghi chú.
- Mỗi dòng mẫu có thể đính kèm nhiều **ảnh mẫu** (`CUSTOMER_REQUIREMENT`) và **file thiết kế** (`DESIGN`); liên kết theo `LINE_NUMBER` trên `PRODUCTION_ATTACHMENTS` (ổn định khi replace dòng nháp).
- Nhập/sửa mẫu qua dialog form, không nhập trực tiếp trên DataGrid.
- Không lập BOM dự kiến và không nhập ngày dự kiến hoàn thành.

### 2.4. Giao và nhận nguyên liệu

- Chỉ có một lần giao nguyên liệu chính thức cho mỗi lệnh.
- Người dùng khai toàn bộ nguyên liệu thực giao rồi bấm ghi sổ một lần; không hỗ trợ giao bổ sung/đợt hai trong module mới.
- Không cho ghi sổ nếu tồn kho không đủ. Toàn bộ nghiệp vụ phải thành công hoặc rollback.
- Giá vàng dùng để tính giá thành là giá kho được snapshot tại ngày/giờ xuất nguyên liệu; tuyệt đối không đọc lại giá hiện hành khi QC hoặc hoàn tất.
- Đá/phụ kiện xuất kho phải giữ nguyên giá vốn snapshot của dòng xuất.
- Chỉ có một lần nhận hàng chính từ thợ. Các lần trả món lỗi cho thợ sửa lại được ghi dưới dạng lần làm lại/QC, không tạo thêm phiếu nhận hàng chính.
- Hao hụt nhập thủ công khi nhận/đối soát. Phải thỏa mãn cân bằng cho từng nguyên liệu:

  `đã giao = vào thành phẩm + trả lại + phế liệu thu hồi + hao hụt`

### 2.5. QC, serial và nhập kho

- Sau khi nhận hàng, hệ thống tạo các “đơn vị chờ QC” theo đúng tổng số lượng từng mẫu; đây chưa phải thành phẩm tồn kho và chưa có serial chính thức.
- QC theo từng món, gồm: đạt/không đạt, trọng lượng thực tế, tuổi vàng, đá/phụ kiện cấu thành, nhiều ảnh sản phẩm và ghi chú lỗi.
- Món không đạt: không tạo `FINISHED_PRODUCTS`, không nhập kho; trả lại thợ để sửa và lưu lý do/lịch sử từng lần. Sau đó được QC lại.
- Món đạt: vẫn chỉ là kết quả QC cho tới khi hoàn tất lệnh.
- Chỉ cho hoàn tất khi mọi đơn vị theo số lượng đặt làm đều đã đạt QC và bảng đối soát nguyên liệu đã cân bằng.
- Trong một transaction hoàn tất: sinh serial cho từng món, tạo một `FINISHED_PRODUCTS` cho từng serial, ghi phiếu nhập thành phẩm và movement, cập nhật lệnh hoàn thành; nếu là thợ ngoài thì đồng thời tạo công nợ tiền công.
- Nếu một bước thất bại, không được để lại serial, tồn kho hoặc công nợ một phần.

### 2.6. Giá thành từng serial

Giá thành bất biến tại lúc hoàn tất:

`Tổng giá thành = giá trị vàng thực tế + giá trị đá/phụ kiện thực tế + tiền công mỗi món`

Trong đó:

- Giá trị vàng = tổng trọng lượng vàng thực tế đã quy đổi về đơn vị chuẩn tính giá × giá vàng kho snapshot của dòng xuất tương ứng.
- Nếu một loại vàng được xuất từ nhiều dòng/giá snapshot, phân bổ cho món theo từng dòng nguồn rồi cộng lại; không lấy một giá trung bình không truy vết được.
- Giá trị đá/phụ kiện = tổng số lượng phân bổ cho món × giá vốn snapshot của dòng xuất.
- Tiền công lấy từ dòng mẫu, snapshot sang đơn vị QC/thành phẩm. Chỉ tính cho món đạt.
- `FINISHED_PRODUCTS.MATERIAL_COST`, `LABOR_COST`, `NON_GOLD_MATERIAL_COST`, `TOTAL_COST`, `GOLD_WEIGHT` phải phản ánh đúng các snapshot trên.

### 2.7. Trạng thái

Trạng thái hiển thị cho người dùng chỉ gồm:

- `Đang làm`
- `Hoàn thành`
- `Hủy`

Database được phép có `DRAFT` để lưu form chưa ghi sổ. `DRAFT` không xuất hiện như một trạng thái riêng trên danh sách chính; UI nhóm nó vào bản nháp/đang soạn. Sau khi ghi sổ giao nguyên liệu, lệnh chuyển `IN_PROGRESS`. Không thêm các trạng thái nghiệp vụ khác vào header lệnh.

QC có trạng thái kỹ thuật riêng ở cấp món (`PENDING`, `REWORK_REQUIRED`, `PASSED`) nhưng UI chỉ diễn đạt theo “Chưa QC / Không đạt - cần làm lại / Đạt”.

## 3. Ranh giới kiến trúc

Tạo module mới `Kazuko.Modules.Production`, route `PRODUCTION`, module id `Kazuko.Production`. Không nhét màn hình sản xuất vào `Kazuko.Modules.Warehouse`.

Tuân thủ dependency:

```text
Production View -> Production ViewModel -> IProductionApplicationService
                -> Infrastructure repository/service -> Firebird
```

- Contract, DTO, validator và phép tính thuần đặt trong `Kazuko.Core/Production`.
- SQL chỉ đặt trong `Kazuko.Infrastructure/Production` hoặc helper kho Infrastructure hiện có.
- Module Production chỉ tham chiếu Core và Shared.UI, không tham chiếu trực tiếp module Kho, Nhân sự, Khách hàng hoặc Công nợ.
- App là composition root, đăng ký implementation và module.
- Các transaction giao NVL, nhận hàng và hoàn tất phải nằm trong application service Infrastructure.
- Mọi nút ghi sổ phải chống double-click/idempotent bằng ràng buộc DB và kiểm tra trạng thái có khóa.

Không thay thế/xóa luồng `IOutsourcedCraftService` cũ trong lần triển khai đầu. Giữ khả năng đọc/chứng từ cũ; module mới dùng service sản xuất mới. Chỉ trích xuất helper dùng chung khi có test bảo vệ hành vi cũ.

## 4. Thiết kế database

Schema hiện tại là version 44. Cursor phải bắt đầu từ migration 45, không sửa checksum migration 30-44 đã chạy.

### 4.1. Migration 45 — lõi lệnh sản xuất

Tạo `0045__production_order_core.sql` và verification tương ứng.

#### `PRODUCTION_ORDERS`

Các cột tối thiểu:

| Cột | Ý nghĩa |
|---|---|
| `ID` | Identity PK |
| `COMPANY_ID`, `BRANCH_ID` | Phạm vi dữ liệu |
| `CODE` | Mã lệnh duy nhất theo công ty |
| `ORDER_SOURCE` | `STOCK` hoặc `SALES_ORDER` |
| `SALES_ORDER_ID` | Nullable; bắt buộc/XOR theo nguồn |
| `EXECUTION_TYPE` | `IN_HOUSE` hoặc `OUTSOURCED` |
| `EXTERNAL_WORKER_ID` | Chỉ có khi `OUTSOURCED` |
| `STATUS_CODE` | `DRAFT`, `IN_PROGRESS`, `COMPLETED`, `CANCELLED` |
| `ORDER_DATE` | Ngày lập |
| `SOURCE_LOCATION_ID` | Kho xuất NVL |
| `FINISHED_LOCATION_ID` | Kho thành phẩm nhận hàng |
| `MATERIAL_ISSUE_DOCUMENT_ID` | Phiếu giao NVL duy nhất |
| `RETURN_DOCUMENT_ID` | Phiếu nhận hàng chính duy nhất |
| `FINISHED_RECEIPT_DOCUMENT_ID` | Phiếu nhập thành phẩm duy nhất |
| `DELIVERED_BY_NAME`, `RECEIVED_BY_NAME` | Người giao/nhận |
| `NOTES` | Ghi chú |
| `ROW_VERSION` | Optimistic concurrency |
| audit columns | `CREATED_BY/AT`, `UPDATED_BY/AT`, `COMPLETED_BY/AT`, `CANCELLED_BY/AT` |

Ràng buộc bắt buộc:

- Unique `(COMPANY_ID, CODE)`.
- Unique index trên `SALES_ORDER_ID WHERE SALES_ORDER_ID IS NOT NULL`.
- CHECK nguồn lệnh: `STOCK` thì `SALES_ORDER_ID IS NULL`; `SALES_ORDER` thì khác null.
- CHECK hình thức: `OUTSOURCED` có `EXTERNAL_WORKER_ID`; `IN_HOUSE` không có.
- FK tới company, branch, sales order, external worker, locations và users.

#### `PRODUCTION_ORDER_LINES`

- `ID`, `PRODUCTION_ORDER_ID`, `LINE_NUMBER`.
- `MODEL_CODE` nullable, `MODEL_NAME` bắt buộc.
- `GOLD_TYPE_CODE`, `GOLD_TYPE_NAME` snapshot.
- `PLANNED_QUANTITY` integer > 0.
- `LABOR_COST_PER_UNIT` numeric >= 0.
- `DESCRIPTION`, `NOTES`.
- Unique `(PRODUCTION_ORDER_ID, LINE_NUMBER)`.

Không lưu “quantity” trong `FINISHED_PRODUCTS`; `PLANNED_QUANTITY` chỉ thuộc kế hoạch dòng mẫu.

#### `PRODUCTION_ORDER_WORKERS`

- `PRODUCTION_ORDER_ID`, `EMPLOYEE_ID`, audit timestamp.
- PK/unique trên cặp.
- Chỉ dùng cho lệnh `IN_HOUSE`; rule này kiểm tra trong service vì CHECK không thể nhìn bảng cha.

#### `PRODUCTION_ATTACHMENTS`

- `ID`, `PRODUCTION_ORDER_ID`, `ATTACHMENT_KIND` (`DESIGN`, `CUSTOMER_REQUIREMENT`).
- `LINE_NUMBER` (nullable; migration 48) — gắn đính kèm với dòng mẫu theo số thứ tự; null chỉ còn với dữ liệu cũ cấp lệnh.
- `STORED_PATH`, `ORIGINAL_FILE_NAME`, `MIME_TYPE`, `CAPTION`, `DISPLAY_ORDER`, audit columns.
- File được import qua `IProductionAttachmentStore`: ảnh mẫu (JPG/PNG/WEBP) và file thiết kế (thêm PDF/AI/CDR/DXF/DWG/ZIP…); DB không lưu đường dẫn nguồn tạm của người dùng.

#### Sequence

Seed `DOCUMENT_SEQUENCES.DOCUMENT_TYPE = 'PRODUCTION_ORDER'`, prefix hiển thị đề xuất `LSX`.

### 4.2. Migration 46 — giao nhận, QC và cấu thành

Tạo `0046__production_execution_qc.sql`.

#### Mở rộng chứng từ kho

Tận dụng `STOCK_DOCUMENTS`, `STOCK_DOCUMENT_LINES`, `FINISHED_PRODUCT_DOCUMENT_LINES` và `FINISHED_PRODUCT_MOVEMENTS`; không tạo một ledger kho song song.

- Thêm/đảm bảo FK `STOCK_DOCUMENTS.PRODUCTION_ORDER_ID -> PRODUCTION_ORDERS.ID`.
- Bổ sung document types vào `CK_STOCK_DOCUMENT_TYPE`:
  - `PRODUCTION_MATERIAL_ISSUE`
  - `PRODUCTION_RETURN_RECEIPT`
  - `PRODUCTION_FINISHED_RECEIPT`
- Bổ sung purpose codes:
  - `PRODUCTION_MATERIAL_DELIVERY`
  - `PRODUCTION_RETURN`
  - `PRODUCTION_FINISHED_RECEIPT`
- Unique filtered index bảo đảm mỗi lệnh chỉ có một chứng từ active cho từng loại.
- Seed sequence tương ứng, đề xuất prefix `GX`, `NT`, `NTP`.

Trước khi thêm FK, preflight phải kiểm tra các giá trị `PRODUCTION_ORDER_ID` legacy trong `STOCK_DOCUMENTS`, `FINISHED_PRODUCTS` và `STOCK_CRAFT_OUTPUT_LINES`. Nếu có ID không có bản ghi cha thì dừng migration và xuất báo cáo; không tự xóa hoặc tự nối sai dữ liệu.

#### `PRODUCTION_MATERIAL_BALANCES`

Một dòng cho mỗi dòng NVL đã xuất:

- Liên kết `PRODUCTION_ORDER_ID`, `ISSUE_DOCUMENT_ID`, `ISSUE_LINE_ID`, `MATERIAL_ID`.
- `QUANTITY_ISSUED`, `QUANTITY_IN_PRODUCT`, `QUANTITY_RETURNED`, `QUANTITY_SCRAP`, `QUANTITY_LOSS`.
- Snapshot đơn vị, giá vốn, `WAREHOUSE_GOLD_PRICE_ID`, `WAREHOUSE_GOLD_PRICE_PER_CHI` khi là vàng.
- CHECK các lượng không âm; unique `ISSUE_LINE_ID`.
- Service xác minh phương trình cân bằng trước khi cho hoàn tất.

Không dùng `EXTERNAL_WORKER_HOLDINGS` cho lệnh nội bộ. Bảng legacy vẫn giữ cho chứng từ cũ.

#### `PRODUCTION_QC_UNITS`

Mỗi món vật lý chờ QC là một dòng:

- `ID`, `PRODUCTION_ORDER_ID`, `ORDER_LINE_ID`, `UNIT_NUMBER`.
- `STATUS_CODE`: `PENDING`, `REWORK_REQUIRED`, `PASSED`.
- `ACTUAL_GOLD_WEIGHT`, `ACTUAL_WEIGHT_UNIT_CODE`, `GOLD_PURITY_CODE`.
- `LABOR_COST_SNAPSHOT`.
- `DEFECT_NOTES`, `LAST_QC_AT`, `LAST_QC_BY`.
- `FINISHED_PRODUCT_ID` nullable và unique; chỉ điền trong transaction hoàn tất.
- `ROW_VERSION`.
- Unique `(ORDER_LINE_ID, UNIT_NUMBER)`.

Tạo đủ số dòng QC từ `PLANNED_QUANTITY` trong transaction ghi nhận phiếu nhận hàng chính, không tạo trước lúc nhận.

#### `PRODUCTION_QC_ATTEMPTS`

Lưu lịch sử bất biến mỗi lần QC:

- `QC_UNIT_ID`, `ATTEMPT_NUMBER`, `RESULT_CODE` (`PASS`, `FAIL`).
- Snapshot trọng lượng, tuổi vàng, ghi chú lỗi.
- `INSPECTED_BY`, `INSPECTED_AT`.
- `RETURNED_FOR_REWORK_AT/BY`, `REWORK_RECEIVED_AT/BY`, `REWORK_NOTES`.
- Unique `(QC_UNIT_ID, ATTEMPT_NUMBER)`.

Không update/xóa attempt cũ khi QC lại.

#### `PRODUCTION_UNIT_COMPONENTS`

Phân bổ nguyên liệu thực tế cho từng món:

- `QC_UNIT_ID`, `MATERIAL_BALANCE_ID`, `MATERIAL_ID`, `COMPONENT_KIND` (`GOLD`, `STONE`, `ACCESSORY`, `OTHER`).
- `QUANTITY_USED`, `UNIT_CODE`, `UNIT_COST_SNAPSHOT`, `AMOUNT_VND`.
- `WAREHOUSE_GOLD_PRICE_ID`, `GOLD_PRICE_PER_CHI_SNAPSHOT` nullable.
- Unique phù hợp để một dòng balance không bị nhập trùng ngoài ý muốn; cho phép nhiều dòng nguồn khi một món dùng nhiều lô.
- Tổng `QUANTITY_USED` của mọi unit theo một balance không được vượt `QUANTITY_IN_PRODUCT`; khóa/cộng kiểm tra trong transaction.

#### `PRODUCTION_QC_IMAGES`

- `QC_UNIT_ID`, `QC_ATTEMPT_ID` nullable, `STORED_PATH`, metadata file, `DISPLAY_ORDER`, audit columns.
- Cho phép nhiều ảnh mỗi món và mỗi lần QC.

### 4.3. Migration 47 — hoàn tất và idempotency

Tạo `0047__production_completion_guards.sql`.

- Thêm FK từ `FINISHED_PRODUCTS.PRODUCTION_ORDER_ID` về `PRODUCTION_ORDERS` sau preflight legacy.
- Nếu `FINISHED_PRODUCTS` chưa có liên kết tới unit QC, thêm `PRODUCTION_QC_UNIT_ID` nullable + unique.
- Tạo unique filtered index bảo đảm chỉ một `PAYABLE_DEBTS` với `ORIGIN_DOCUMENT_TYPE = 'PRODUCTION_ORDER'` cho mỗi lệnh.
- Tạo/index các truy vấn danh sách theo `(COMPANY_ID, BRANCH_ID, STATUS_CODE, ORDER_DATE)` và lookup order/worker.
- Thêm verification SQL kiểm tra table, column, constraint, index, sequence và dữ liệu orphan.
- Cập nhật `docs/05-DATABASE-MIGRATIONS.md` và tạo `docs/08-PRODUCTION-INVARIANTS.md` mô tả invariant/recovery.

## 5. Contract và service

Tạo namespace `Kazuko.Core.Production` với các nhóm contract sau:

- `IProductionQueryRepository`: danh sách, chi tiết, đơn bán hợp lệ, nhân viên, thợ ngoài, vị trí kho, NVL có tồn.
- `IProductionApplicationService`: tạo/sửa draft, ghi sổ giao NVL, ghi nhận nhận hàng, lưu QC/làm lại, hoàn tất, hủy.
- `IProductionPrintDataService`: trả DTO bất biến cho 5 mẫu in.
- `IProductionAttachmentStore`: import/copy/xóa best-effort file managed.
- DTO/draft riêng cho từng command; không dùng ViewModel làm input service.
- Validator thuần: source XOR, worker XOR, quantity, labor, material balance, QC completeness, component allocation và cost calculation.

Các use case ghi:

### 5.1. `SaveDraftAsync`

- Cho tạo/sửa header, mẫu, nhân viên và ảnh khi còn `DRAFT`.
- Nếu nguồn là đơn bán, khóa/kiểm tra uniqueness trong DB.
- Sau khi đã ghi sổ giao NVL, không cho đổi nguồn lệnh, loại thực hiện, dòng mẫu hoặc số lượng.

### 5.2. `PostMaterialIssueAsync`

Trong một transaction:

1. Khóa lệnh và kiểm tra `DRAFT`.
2. Kiểm tra worker theo execution type và có ít nhất một dòng mẫu.
3. Khóa tồn từng NVL, kiểm tra đủ toàn bộ.
4. Resolve/snapshot giá vàng kho theo dòng vàng; snapshot average/unit cost cho đá/phụ kiện.
5. Tạo một `STOCK_DOCUMENTS` + lines; cập nhật balances/movements kho theo helper chuẩn hiện có.
6. Tạo `PRODUCTION_MATERIAL_BALANCES`.
7. Cập nhật lệnh `IN_PROGRESS` và issue document id.
8. Ghi audit.

### 5.3. `PostWorkerReturnAsync`

Trong một transaction:

1. Khóa lệnh `IN_PROGRESS`, phải đã giao và chưa nhận chính.
2. Nhập đối soát actual: vào sản phẩm, trả lại, phế liệu, hao hụt; bắt buộc cân bằng từng dòng.
3. Ghi movement nhập kho cho lượng trả lại/phế liệu theo đúng vị trí và quy ước kho hiện hành.
4. Tạo phiếu nhận chính duy nhất.
5. Tạo đủ `PRODUCTION_QC_UNITS` theo số lượng từng mẫu.
6. Chưa tạo thành phẩm, serial, movement thành phẩm hoặc công nợ.

### 5.4. `RecordQcAsync` và `RecordReworkReturnAsync`

- Khóa unit theo `ROW_VERSION`.
- Mỗi lần QC tạo attempt mới; không ghi đè lịch sử.
- Kết quả fail chuyển unit sang `REWORK_REQUIRED`, bắt buộc lý do; không có product id.
- Chỉ unit `REWORK_REQUIRED` mới được ghi trả làm lại/nhận lại rồi QC lại.
- Kết quả pass bắt buộc trọng lượng, tuổi vàng, phân bổ cấu thành và ảnh theo validation đã thống nhất; chuyển `PASSED`.
- Nếu sửa một QC đã pass trước khi hoàn tất: UI bật chế độ **Điều chỉnh QC**, bắt buộc lý do, tạo attempt mới + audit `PRODUCTION_QC_ADJUSTED` / pass|fail; thay cấu thành hiện hành của unit (xóa rồi ghi lại), không update âm thầm attempt cũ. Chỉ khi lệnh còn `IN_PROGRESS`.

### 5.5. `CompleteAsync`

Transaction quan trọng nhất:

1. Khóa lệnh; idempotent nếu đã `COMPLETED` thì trả kết quả cũ, không tạo lại.
2. Xác minh đã giao/nhận, mọi QC unit đều `PASSED`, số unit đúng tổng planned quantity và đối soát NVL cân bằng.
3. Xác minh tổng phân bổ component không vượt actual in-product.
4. Resolve kho thành phẩm hợp lệ có `INVENTORY_SCOPE = 'FINISHED_GOODS'`.
5. Sinh serial bằng sequence hiện có theo từng món; không dùng mã người dùng nhập.
6. Tính cost từ snapshot và tạo một `FINISHED_PRODUCTS` cho mỗi QC unit, `QUANTITY = 1` theo invariant hiện hành, trạng thái `IN_STOCK`, location là kho thành phẩm.
7. Tạo `PRODUCTION_FINISHED_RECEIPT`, `FINISHED_PRODUCT_DOCUMENT_LINES` và một movement `IN` cho từng serial.
8. Gắn `FINISHED_PRODUCT_ID` về QC unit.
9. Với `OUTSOURCED`, tính tiền công bằng tổng `LABOR_COST_SNAPSHOT` của unit đạt. Nếu tổng > 0, tạo một payable debt loại `LABOR`, party `EXTERNAL_WORKER`, origin `PRODUCTION_ORDER`. Không thanh toán tự động.
10. Với `IN_HOUSE`, tuyệt đối không tạo payable debt.
11. Cập nhật lệnh `COMPLETED`, audit và commit.

### 5.6. `CancelAsync`

- `DRAFT`: cho hủy trực tiếp.
- `IN_PROGRESS`: chỉ cho hủy khi chưa có unit nhập kho/công nợ và mọi NVL đã được hoàn trả/đối soát về 0; nếu không thì chặn và chỉ rõ phần chưa xử lý.
- `COMPLETED`: không cho hủy bằng command thường. Việc đảo chứng từ phải là use case reversal riêng, không nằm trong scope đầu tiên.

## 6. Giao diện

### 6.1. Màn hình danh sách

Module tile/sidebar: **SẢN XUẤT**.

- Thẻ tổng quan: Đang làm, Hoàn thành trong kỳ, Lệnh thợ ngoài, Lệnh nội bộ.
- Tìm theo mã lệnh, tên mẫu, mã đơn bán, thợ/nhân viên.
- Lọc: trạng thái hiển thị, nguồn lệnh, hình thức thực hiện, khoảng ngày.
- DataGrid có settings key và column key ổn định theo convention dự án.
- Các action chính: Tạo lệnh, Mở chi tiết, In.

### 6.2. Màn hình chi tiết

Dùng một cửa sổ/detail page có các tab thông tin, không gọi là “công đoạn”:

1. **Thông tin lệnh**: nguồn, đơn bán, hình thức, thợ/nhân viên, kho, ghi chú.
2. **Mẫu sản phẩm**: danh sách chỉ đọc; thêm/sửa qua dialog (quantity, loại vàng, tiền công/món, mô tả, ảnh mẫu + file thiết kế theo từng dòng).
3. **Giao nguyên liệu**: chọn NVL từ tồn kho, số lượng, đơn vị, giá snapshot chỉ đọc, ghi sổ một lần.
4. **Nhận & đối soát**: actual in-product/trả lại/phế liệu/hao hụt, cảnh báo lệch, ghi nhận một lần.
5. **QC từng món**: cây/list nhóm theo mẫu; form QC, cấu thành, gallery nhiều ảnh, lịch sử làm lại.
6. **Tổng kết**: dự kiến serial sẽ tạo, giá thành từng món, tổng tiền công, công nợ sẽ sinh, nút hoàn tất.

Ẩn/disable action theo trạng thái nhưng vẫn hiển thị dữ liệu lịch sử chỉ đọc. Không hiển thị một chuỗi nhiều trạng thái lệnh gây rối.

### 6.3. Quy tắc UI

- Dùng toàn bộ brush/font/style qua `DynamicResource`; không hard-code màu hoặc cỡ chữ.
- Dialog có scroll, Enter lưu/Escape hủy nếu không gây ghi sổ ngoài ý muốn.
- Nút ghi sổ/hoàn tất phải có hộp xác nhận nêu rõ thao tác không thể sửa trực tiếp sau đó.
- Hiển thị đơn vị ngay cạnh mọi trường trọng lượng/số lượng.
- Giá tiền định dạng VND; trọng lượng không làm tròn mất dữ liệu DB.
- Kiểm tra light/dark, font 90-150%, DPI 100-200%, cửa sổ tối thiểu và nội dung tiếng Việt dài.

## 7. Ảnh và file

Tạo implementation lưu file managed dưới `%LocalAppData%\KazukoERP\production-attachments\<order-code>\` theo pattern ảnh nhân viên/khách hàng hiện có, nhưng bọc qua interface để sau này chuyển sang shared storage.

- Allow-list: JPG, JPEG, PNG, WEBP; kiểm tra extension, file tồn tại và kích thước tối đa cấu hình.
- Tên file mới dùng GUID, không tin tên file nguồn.
- Copy vào staging trước; chỉ finalize sau khi DB lưu thành công; lỗi thì cleanup best-effort.
- Chỉ xóa file khi xác nhận nó nằm trong managed root và không còn metadata tham chiếu.
- Không lưu binary ảnh trực tiếp vào Firebird trong scope này.

## 8. In chứng từ

Hiện codebase chưa có print service hoàn chỉnh; nút in ở module Công nợ còn placeholder. Với module Sản xuất, triển khai `ProductionPrintService` trong module WPF, nhận DTO từ Core/Infrastructure và dựng `FlowDocument`/`FixedDocument` để mở `PrintDialog`.

Phải có 5 mẫu in:

1. Phiếu giao nguyên liệu.
2. Phiếu thợ giao trả/biên nhận hàng.
3. Phiếu QC theo từng món và tổng hợp.
4. Phiếu nhập kho thành phẩm, liệt kê từng serial.
5. Biên bản quyết toán vàng/hao hụt.

Mỗi mẫu có mã lệnh, ngày, chi nhánh, người giao/nhận, chi tiết, chữ ký trống và mã chứng từ liên quan. Không đưa logic tính toán vào template; template chỉ render DTO đã chốt.

## 9. Permission và audit

Hiện chỉ có Admin nhưng vẫn đăng ký permission key để không phải phá API sau này:

- `production.order.read`
- `production.order.create`
- `production.order.edit`
- `production.material.issue`
- `production.return.post`
- `production.qc.write`
- `production.order.complete`
- `production.order.cancel`
- `production.document.print`

Admin được cấp toàn bộ theo cơ chế hiện có. Ghi audit cho: tạo/sửa lệnh, ghi giao, nhận, QC pass/fail, trả làm lại, hoàn tất, hủy và in lại chứng từ quan trọng.

## 10. Tích hợp codebase

Cursor cần thêm/cập nhật tối thiểu:

- `src/Kazuko.Core/Production/*`
- `src/Kazuko.Infrastructure/Production/*`
- `src/Modules/Kazuko.Modules.Production/*`
- Project reference trong `Kazuko.App.csproj`, `.sln`, `.slnx`.
- Đăng ký repository/service trong `App.RegisterFoundation()` và `ProductionModule` trong `RegisterModules()`.
- Route/navigation qua `IKazukoModule`; không hard-code menu trong shell.
- Dùng lại helper stock posting, gold pricing snapshot, document sequence, finished product movement và audit đã có; nếu helper đang `internal/private` thì trích xuất ở Infrastructure kèm regression tests.
- Không gọi trực tiếp `FirebirdOutsourcedCraftService` từ ViewModel mới.

## 11. Thứ tự triển khai cho Cursor

### Phase 0 — bảo vệ hiện trạng

1. Đọc `AGENTS.md` và docs 00-06.
2. Chạy `git status`; không sửa/rollback file ngoài scope.
3. Build và test baseline, ghi lại lỗi có sẵn.
4. Inspect metadata DB thật cho các cột legacy `PRODUCTION_ORDER_ID`; không chạy migration trên DB thật khi app còn mở.
5. Backup `data/data.fdb` theo hướng dẫn Embedded Firebird trước lần apply đầu tiên.

### Phase 1 — domain và migration 45

1. Viết invariant doc trước hoặc cùng code.
2. Tạo Core contracts, enums/constants, validators và unit tests.
3. Tạo migration 45 + verification + migration docs.
4. Tạo repository query/command cho draft.
5. Tạo module project, route và màn hình danh sách/detail draft.
6. Build/test.

### Phase 2 — giao nguyên liệu

1. Migration 46 phần stock types/balances.
2. Implement `PostMaterialIssueAsync` với transaction và snapshot giá.
3. UI tab giao NVL + print phiếu giao.
4. Integration tests tồn đủ/không đủ, double-post, rollback và snapshot giá.
5. Build/test.

### Phase 3 — nhận, đối soát và QC

1. Hoàn tất tables QC/attempt/component/image.
2. Implement một lần nhận chính và material balance.
3. Implement QC từng món, fail/rework/re-QC và image store.
4. UI nhận/QC và 3 mẫu in liên quan.
5. Integration/unit tests.
6. Build/test.

### Phase 4 — hoàn tất và công nợ

1. Migration 47 guards/index/FK.
2. Implement transaction hoàn tất end-to-end.
3. Tạo từng serial/thành phẩm/movement, cost snapshot và payable debt.
4. UI tổng kết + xác nhận + phiếu nhập kho.
5. Integration tests idempotency/concurrency/rollback.
6. Build/test.

### Phase 5 — hardening

1. Cancellation guards và audit đầy đủ.
2. Kiểm tra query performance/index với dữ liệu mẫu lớn.
3. Regression test kho, chuyển kho, xuất đơn bán và công nợ cũ.
4. Visual QA theo ma trận UI.
5. Chạy verification SQL trên bản copy DB trước, sau đó mới apply DB thật khi app đã đóng và có backup.

Không gộp tất cả phase thành một thay đổi khổng lồ. Sau mỗi phase, build/test phải xanh trước khi tiếp tục.

## 12. Test bắt buộc

### Unit tests

- XOR nguồn lệnh và execution type.
- Nhiều nhân viên cho in-house, đúng một thợ cho outsourced.
- Tiền công theo mỗi món, không phải theo dòng mẫu.
- Cân bằng giao/nhận/hao hụt.
- Phân bổ component không vượt actual in-product.
- Cost vàng theo snapshot, kể cả nhiều dòng giá.
- Failed QC không tạo serial/product/debt.
- Điều kiện hoàn tất đủ mọi unit.

### Integration tests Firebird

- Một sales order không tạo được hai production order.
- Một lệnh không ghi được hai phiếu giao hoặc hai phiếu nhận.
- Giao thiếu tồn rollback toàn bộ.
- Đổi giá vàng kho sau khi giao không làm đổi cost khi hoàn tất.
- 5 nhẫn + 10 lắc sinh đúng 15 `FINISHED_PRODUCTS`, 15 serial khác nhau, mỗi movement quantity 1.
- Một món fail, làm lại, pass: chỉ sinh một product và giữ đủ hai QC attempts.
- Một món còn fail thì complete bị chặn và không có product/debt mới.
- Outsourced complete sinh đúng một debt bằng tổng labor của unit đạt; gọi complete lần hai không sinh thêm.
- In-house complete không sinh debt.
- Lỗi ở product thứ N rollback toàn bộ products, movements, receipt và debt.
- Concurrency: hai phiên complete/post cùng lúc chỉ một phiên thành công.
- Thành phẩm hoàn tất xuất hiện ở Kho thành phẩm và tuân thủ filter unique-item hiện tại.
- Luồng transfer Kho thành phẩm -> Kho bán hàng và bán ra vẫn hoạt động.

### UI tests thủ công

- Light/dark; font 90/100/125/150%; DPI 100/150/200%.
- Empty/loading/error/long Vietnamese text.
- Keyboard navigation, focus, scroll, double-click protection.
- Gallery nhiều ảnh; ảnh nguồn bị xóa sau import vẫn xem được.
- Mẫu in không cắt cột/dòng khi có 15+ serial.

## 13. Tiêu chí nghiệm thu

Chỉ coi module hoàn tất khi đồng thời đạt:

- Người dùng tạo được cả lệnh nhập kho và lệnh theo đơn bán.
- In-house chọn được nhiều nhân viên; outsourced chọn được một thợ.
- Giao NVL và nhận hàng mỗi loại chỉ ghi sổ một lần.
- QC quản lý từng món, có ảnh/lỗi/lịch sử làm lại.
- Món fail không có serial và không nằm trong tồn kho.
- Hoàn tất sinh đúng một serial/bản ghi/movement cho từng món đạt.
- Giá thành dùng đúng giá kho tại lúc giao NVL, cộng component và tiền công/món.
- Công nợ thợ ngoài chỉ sinh lúc hoàn tất; nội bộ không sinh.
- UI lệnh chỉ trình bày Đang làm/Hoàn thành/Hủy.
- Có đủ 5 mẫu in.
- Migration 45-47 có preflight, verification, rollback notes và ledger checksum đúng.
- `dotnet build KAZUKO_ERP_JEWELRY.sln` và `dotnet test KAZUKO_ERP_JEWELRY.sln --no-build` thành công.
- Không có SQL trong View/ViewModel, không hard-code brush/font size, không phá invariant kho hiện có.

## 14. Những việc ngoài scope

- Lập lịch dự kiến hoặc quy trình nhiều công đoạn.
- BOM dự kiến/định mức hao hụt tự động.
- Chia giao nguyên liệu thành nhiều đợt.
- Nhận thành phẩm nhiều đợt hoặc nhập kho một phần.
- Tính lương nhân viên nội bộ.
- Thanh toán công thợ ngay trong màn hình sản xuất.
- Hủy/đảo một lệnh đã hoàn tất.
- Phân quyền chi tiết ngoài việc khai báo permission keys.
- Thay đổi bảng giá vàng bán lẻ; sản xuất chỉ dùng bảng giá vàng kho và snapshot.

## 15. Chỉ dẫn thực thi ngắn cho Cursor

> Triển khai lần lượt từng phase trong tài liệu này. Trước mỗi phase, inspect code/schema liên quan và nêu danh sách file dự kiến sửa. Không tự thêm công đoạn/trạng thái, không tạo thành phẩm gộp quantity, không tạo serial trước lúc complete, không tạo công nợ lúc nhận hàng. Dùng migration mới từ version 45, transaction và DB constraint cho mọi invariant quan trọng. Sau mỗi phase chạy build/test và báo rõ migration nào đã tạo; chỉ apply vào DB thật khi ứng dụng đã đóng, đã backup và verification chạy xanh trên bản copy.
