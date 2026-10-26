# Danh mục loại thu/chi

## Phạm vi

Phiếu lập tay trong Tài chính có **Loại thu** hoặc **Loại chi**, riêng với hướng Thu/Chi và phương thức tiền mặt/chuyển khoản/thẻ. Nút **Quản lý loại** cạnh trường chọn cho phép thêm, sửa mã/tên, ngừng hoặc dùng lại loại. **Xóa loại** cần xác nhận và quyền Xóa theo hướng phiếu, chỉ cho xóa loại chưa được phiếu nào sử dụng; phiếu đã hủy vẫn chặn xóa. Loại đã sử dụng cần ngừng dùng để giữ liên kết lịch sử.

**Thêm loại mới** mở hộp thêm riêng và focus ô mã, không chỉ xóa vùng thuộc tính.
Lưu thành công đóng hộp và chọn dòng vừa tạo trong danh sách; hủy không sửa bản
đang chọn ở cửa sổ quản lý. Sửa loại đã có vẫn dùng vùng thuộc tính bên phải.

Danh mục tách theo doanh nghiệp và hướng phiếu. Các loại seed: Thu khác, Chi khác, Chi lương, Chi thưởng. Form chỉ chọn loại đang sử dụng; mặc định OTHER nếu còn hoạt động, nếu không chọn loại hoạt động đầu tiên. Phiếu tự động/công nợ và phiếu cũ giữ Chưa phân loại; đợt này không thay luồng tất toán hay hồi tố dữ liệu.

Loại xuất hiện ở danh sách/chi tiết phiếu, có thể tìm bằng tên loại trong ô tìm kiếm. Cơ cấu chi phí ưu tiên tên loại đã lưu; phiếu chưa có loại tiếp tục nhóm theo nguồn như trước.

## Kiến trúc và dữ liệu

- Core: `IVoucherCategoryService`, `VoucherCategory`, `VoucherCategorySaveRequest`, `VoucherCategoryRules`. Finance chỉ gọi service; SQL ở `FirebirdVoucherCategoryService`.
- Migration88: `VOUCHER_CATEGORIES` và `CASH_VOUCHERS.CATEGORY_ID/CATEGORY_NAME` nullable. FK composite `(COMPANY_ID,VOUCHER_TYPE,CATEGORY_ID)` ngăn liên kết sai doanh nghiệp/hướng. Không cập nhật số tiền, trạng thái hay loại các phiếu có sẵn.
- Tên loại được snapshot lúc lập phiếu. Đổi tên/ngừng dùng danh mục không sửa phiếu đã lập. Tạo phiếu khóa và kiểm tra loại còn hoạt động trong cùng transaction ghi phiếu. Gọi `ManualVoucherRequest` cũ không truyền CategoryId tìm loại OTHER hoạt động của đúng hướng/doanh nghiệp; thiếu thì từ chối và yêu cầu chọn loại.
- Mã 1–40 ký tự ASCII A–Z/0–9/_/-, chuẩn hóa hoa, duy nhất trong doanh nghiệp+hướng. Tên 1–150 ký tự Unicode NFC. Không đổi hướng của loại đã tạo. Ghi danh mục có transaction, kiểm tra trùng mã và audit trước/sau.
- `AuthorizedServiceProxy<IVoucherCategoryService>` áp dụng quyền theo hướng: FIN_CASH_IN hoặc FIN_CASH_OUT; List dùng Xem, tạo dùng Thêm, sửa/ngừng dùng dùng Sửa, Delete dùng Xóa. Không thêm grant ngầm cho nhóm. Xóa kiểm tra đúng doanh nghiệp/hướng, khóa cùng danh mục, kiểm tra cả phiếu hủy, xóa và audit trong transaction; FK bảo vệ khi có ghi phiếu đồng thời. Không migration.

## Migration và phục hồi

Đã truy vấn MAX(VERSION_NO)=87 trên DB thật trước khi tạo artifact0088. Chạy qua `database/tools/Invoke-KazukoMigration.ps1`; giữ backup gbak và restore kiểm tra trước. Runner quản lý full-artifact SHA256 và ledger; không sửa migration đã áp dụng.

Khôi phục bằng backup trước88 cùng build trước tính năng này; không drop cột/bảng trên DB đang dùng vì sẽ mất phân loại mới. Backup giữ lại trong thư mục artifacts do phiên triển khai ghi nhận. Kiểm tra sau migration: version88/checksum khớp artifact, bốn loại seed cho mỗi doanh nghiệp, số phiếu và tổng AMOUNT_VND không đổi.

## Kiểm thử

`tests/Kazuko.Finance.IntegrationChecks` chỉ nhận DB thử có tên `test88_*.fdb` được restore và áp dụng migration88, không nhận DATA.FDB. Kiểm tra tạo/sửa/ngừng dùng, trùng mã, phân quyền Add/Edit theo hướng, cách ly doanh nghiệp, loại ngừng dùng/sai hướng, snapshot tên, tìm kiếm, fallback OTHER, hủy phiếu giữ loại, audit và truy vấn cơ cấu chi.

```powershell
dotnet run --project tests/Kazuko.Finance.IntegrationChecks -p:BuildInParallel=false -- D:\Working\artifacts\voucher-categories\test88_<timestamp>.fdb
```

Đặt `KAZUKO_FINANCE_SCREENSHOTS` để xuất ảnh render Light/Dark ở font100%/150%. Kiểm tra thủ công: mở phiếu thu/chi từ Tổng quan và danh sách; sửa danh mục rồi quay lại picker phải thấy dữ liệu mới; lưu phiếu rồi xem chi tiết/tìm theo tên; thu hồi quyền Sửa khi manager đang mở. Kiểm tra cửa sổ tối thiểu, font90–150%, DPI100–200%, Tab/Enter/Escape và nội dung dài.
