# Khen thưởng / kỷ luật và chi lương — migration89

## Luồng sử dụng

Tab Tính lương hiện lý do nút Chi lương bị khóa (bản nháp, chưa chọn nhân viên, thực nhận 0, đã chi, thiếu quyền/dịch vụ). Sau chốt hiện hướng dẫn chi cho nhân viên đang chọn; mặc định ưu tiên dòng chưa chi có thực nhận dương. **Mở chốt để sửa lại** hỏi xác nhận rõ kỳ trở về bản nháp và phải chốt lại mới chi được. Thay đổi quyền cập nhật lại trạng thái nút. Kiểm thử chuỗi tải nháp → chốt → chi khả dụng → hủy/xác nhận mở chốt ở `PayrollPaymentStateTests`.

Nhân sự → Khen thưởng - Kỷ luật → Thêm khoản phát sinh: chọn nhân viên, ngày, loại, nội dung và cách xử lý.

| Cách xử lý | Tiền | Kỳ lương | Phiếu |
|---|---|---|---|
| Chỉ ghi nhận | 0 | Không | Không |
| Cộng/trừ vào lương | Nguyên VND >0 | Bắt buộc | Qua chi lương cuối kỳ |
| Chi/thu riêng | Nguyên VND >0 | Không | Tạo ngay trong cùng transaction |

Thưởng vào lương là cộng, kỷ luật vào lương là khấu trừ; thưởng riêng là chi, kỷ luật thu riêng là thu. Khoản đã lưu không sửa trực tiếp: hủy có lý do rồi tạo lại. Khoản thuộc kỳ đã chốt không hủy được trước khi mở chốt. Hủy khoản riêng hủy đồng thời phiếu liên kết.

Tính lương → chọn kỳ → Tính lại → kiểm tra → Chốt. Chọn từng nhân viên thực nhận >0 → Chi lương nhân viên → chọn quỹ/tài khoản, phương thức và ngày chi → Xác nhận. Phiếu dùng loại có mã SALARY (Chi lương), hiển thị Đã chi và mã phiếu. Chốt không tự tạo chi. Phiên bản đầu trả đủ một lần, không chi từng phần hay ứng lương.

Muốn sửa bảng đã chi: nhập lý do, hủy các khoản chi lương trước rồi mới mở chốt. Hủy chi cập nhật phiếu và bản ghi chi cùng transaction; chi lại sẽ tạo phiếu mới và giữ lịch sử lần đã hủy. Tài chính không cho hủy riêng voucher nguồn HR_AWARD/HR_PAYROLL, hướng người dùng về Nhân sự.

## Công thức, nguồn và chống trùng

`Thực nhận = Salary + Bonus + RecordedBonus - Deduction - RecordedDeduction`.

Bonus/Deduction cũ là điều chỉnh nhập tay. RecordedBonus/RecordedDeduction lấy từ sổ phát sinh. Bảng/Excel dùng TotalBonus/TotalDeduction; chi tiết ghi rõ "điều chỉnh thêm" để tránh nhập lại khoản đã ghi nhận.

- HR_EMPLOYEE_AWARDS giữ snapshot nhân viên, loại/cách xử lý/kỳ, trạng thái, voucher và request GUID+hash. Retry cùng GUID trả bản ghi cũ; GUID dùng với payload khác bị từ chối.
- HR_PAYROLL_AWARD_LINKS có unique AwardId. Tính lại xóa/tạo lại liên kết và tổng từ nguồn, không cộng dồn. Chỉ khoản PAYROLL ACTIVE đúng kỳ được lấy; CASH/NONE không vào lương.
- Ghi chú trên dòng lương tối đa2000 ký tự; nội dung đầy đủ giữ trong các khoản nguồn bất biến. Hash chốt gồm cả khoản nguồn, nên nguồn thay đổi buộc tính lại trước khi chốt.
- HR_PAYROLL_PAYMENTS giữ lịch sử theo kỳ+nhân viên, snapshot số tiền, ngày, voucher. Unique partial chỉ cho một khoản POSTED mỗi kỳ/nhân viên. Không FK tới ID dòng lương vì tính lại tái tạo dòng sau khi mọi chi đã hủy.
- PENDING chỉ tồn tại trong transaction tạo chi trước khi có voucher; lỗi rollback cả payment/voucher. Quỹ/tài khoản/phương thức được kiểm tra bởi posting helper hiện có. SourceAllocationType+Id chống tạo voucher trùng.
- Khóa ghi trên hàng công ty dùng chung cho thêm/hủy khoản và tính/chốt/chi/mở chốt. Hai yêu cầu đồng thời có thể bị serialization conflict và cần tải lại; không thể chi trùng. Chi/hủy chi tăng revision kỳ.
- Hủy chi yêu cầu đúng voucherId người dùng đã xem, không hủy nhầm lần chi mới hơn. Không mở chốt nếu còn khoản POSTED. Các lần chi đã hủy không mất khi tính lại.
- Nhân viên đã ngừng làm nhưng có khoản PAYROLL ACTIVE vẫn được xét; ngày vào làm vẫn áp dụng. Nếu khoản thuộc nhân viên ngoài phạm vi kỳ, tính lại báo lỗi rõ ràng. Tổng khấu trừ không vượt lương+tổng thưởng.

## Kiến trúc và quyền

IEmployeeMoneyService ở Core, App bọc AuthorizedServiceProxy; SQL/transaction ở Infrastructure. HR không tham chiếu module Finance. Helper posting nhận cùng connection/transaction, không gọi manual voucher service tự commit.

| Thao tác | Quyền |
|---|---|
| Xem khoản | CAT_EMPLOYEES/Xem |
| Thêm khoản | CAT_EMPLOYEES/Thêm; thêm FIN_CASH_OUT/Thêm hoặc FIN_CASH_IN/Thêm khi chi/thu riêng |
| Hủy khoản | CAT_EMPLOYEES/Xóa; thêm FIN_REVERSE/Xóa nếu bản ghi thật là CASH |
| Chi lương | HR_PAYROLL/Sửa + FIN_CASH_OUT/Thêm |
| Hủy chi lương | HR_PAYROLL/Sửa + FIN_REVERSE/Xóa |
| Mở chốt | HR_PAYROLL + HR_PAYROLL_REOPEN/Sửa như trước |

Proxy đọc bản ghi thật khi kiểm tra quyền hủy CASH, không tin nhãn client. Không cấp grant ngầm cho nhóm thường. Tra cứu nhân viên dùng CAT_EMPLOYEES hoặc HR_PAYROLL. Dữ liệu mới scoped theo doanh nghiệp của chi nhánh trong session. PayrollService tại App nhận session accessor; tool cũ không inject accessor giữ fallback KAZUKO. App đã inject mà session bị clear thì từ chối, không fallback.

## Migration và phục hồi

Đã đọc MAX(VERSION_NO)=88 trước khi tạo artifact0089. Chạy qua runner chuẩn sau backup/restore kiểm tra. Migration không tạo phiếu, không tự ghi thưởng hay thay số tiền bảng lương cũ. Các cột mới mặc định0/rỗng. Bảng chốt cũ giữ nguyên số, trạng thái chi ban đầu Chưa chi; bảng nháp cũ cần Tính lại trước khi chốt để cập nhật hash nguồn mới.

Rollback bằng backup trước89 cùng build trước tính năng; không drop bảng hoặc restore khi đã có giao dịch mới mà chưa bảo toàn chúng. Giữ backup riêng ở artifacts/employee-money. Không sửa artifact sau khi áp dụng vì ledger kiểm tra SHA256.

## Kiểm thử

`dotnet test tests/Kazuko.Modules.HumanResources.Tests` kiểm tra hồi quy công thức/chấm công/điều chỉnh cũ.

`dotnet run --project tests/Kazuko.EmployeeMoney.IntegrationChecks -p:BuildInParallel=false -- <test89_*.fdb>` chỉ nhận DB thử đã restore/migrate, không nhận DATA.FDB. Kiểm tra tính lại, nhập tay/tự lấy, tách cash và ghi nhận, hash stale, chốt/mở chốt, sai số tiền/quỹ rollback, retry/chi đồng thời, hủy/rechi/recalculate giữ lịch sử, tenant và quyền.

KAZUKO_HR_MONEY_SCREENSHOTS xuất ảnh Light/Dark font90/100/125/150% ở cửa sổ tối thiểu/thường. Kiểm tra thủ công thêm DPI Windows100/150/200%, Tab/Enter/Escape, cuộn ở font lớn và quyền bị thu hồi. Không ghi phiếu thử hay gửi lệnh in từ DATA.FDB.
