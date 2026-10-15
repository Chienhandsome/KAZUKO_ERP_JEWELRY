// Contract cho dialog phiếu thu/chi dùng chung giữa module Công nợ và module Tài chính.
//
// Module Công nợ KHÔNG được tham chiếu module Tài chính (kiến trúc module độc lập),
// nên request/result/service được định nghĩa ở Kazuko.Core (WPF-neutral, không dùng
// type System.Windows). Module Tài chính sẽ cài đặt IDebtSettlementDialogService
// bằng CashVoucherDialog ở Wave sau; DI đăng ký implementation thật lúc runtime.
namespace Kazuko.Core.Finance;

/// <summary>
/// Yêu cầu mở dialog phiếu thu/chi để tất toán một khoản công nợ.
/// </summary>
/// <param name="VoucherType">FinancialVoucherTypes.In ("IN") = phiếu thu, Out ("OUT") = phiếu chi.</param>
/// <param name="PartyDisplayName">Tên đối tác hiển thị trên dialog (khách hàng / NCC / thợ ngoài).</param>
/// <param name="SourceDocumentCode">Mã chứng từ gốc để đối chiếu (mã đơn hàng, mã khoản nợ...), có thể null.</param>
/// <param name="DefaultAmountVnd">Số tiền điền sẵn, thường = số dư còn lại của khoản nợ.</param>
/// <param name="DefaultNotes">Ghi chú điền sẵn, có thể null.</param>
/// <param name="AllowAmountEdit">false = khóa ô số tiền (VD: tất toán đúng số dư, không cho sửa).</param>
public sealed record DebtSettlementDialogRequest(
    string VoucherType,
    string PartyDisplayName,
    string? SourceDocumentCode,
    decimal DefaultAmountVnd,
    string? DefaultNotes,
    bool AllowAmountEdit = true);

/// <summary>
/// Kết quả dialog khi người dùng xác nhận: đủ dữ liệu để backend tạo
/// settlement + phiếu thu/chi (ánh xạ 1-1 sang ManualVoucherRequest).
/// </summary>
/// <param name="VoucherType">FinancialVoucherTypes.In / Out.</param>
/// <param name="VoucherDate">Ngày phiếu do người dùng chọn.</param>
/// <param name="PaymentMethod">PaymentMethods.Cash | BankTransfer | Card.</param>
/// <param name="AmountVnd">Số tiền đã xác nhận (luôn &gt; 0).</param>
/// <param name="PartyName">Tên đối tác.</param>
/// <param name="Notes">Ghi chú.</param>
/// <param name="ReferenceNumber">Số tham chiếu (chứng từ ngân hàng...), có thể null.</param>
/// <param name="SourceDocumentCode">Mã chứng từ gốc, echo lại từ request để backend liên kết settlement.</param>
public sealed record DebtSettlementDialogResult(
    string VoucherType,
    DateTime VoucherDate,
    string PaymentMethod,
    decimal AmountVnd,
    string? PartyName,
    string? Notes,
    string? ReferenceNumber,
    string? SourceDocumentCode);

/// <summary>
/// Dịch vụ mở dialog phiếu thu/chi. Trả về null khi người dùng hủy.
/// Implementation WPF (CashVoucherDialog) do module Tài chính cung cấp.
/// </summary>
public interface IDebtSettlementDialogService
{
    DebtSettlementDialogResult? ShowDialog(DebtSettlementDialogRequest request);
}
