// Contract cho dialog phiếu thu/chi dùng chung giữa module Công nợ và module Tài chính.
//
// Module Công nợ KHÔNG được tham chiếu module Tài chính (kiến trúc module độc lập),
// nên request/result/service được định nghĩa ở Kazuko.Core (WPF-neutral, không dùng
// type System.Windows). Module Tài chính cài đặt IDebtSettlementDialogService
// bằng CashVoucherDialog.
namespace Kazuko.Core.Finance;

/// <summary>
/// Một dòng phân bổ công nợ trong dialog: khoản nợ + số tiền thanh toán cho dòng đó.
/// </summary>
/// <param name="SourceType">DebtLedgerSourceTypes.SalesOrder | ReceivableDebt | PayableDebt.</param>
/// <param name="SourceId">Id của bản ghi gốc (đơn hàng / khoản nợ).</param>
/// <param name="DisplayName">Tên hiển thị (VD: mã đơn hàng, mã khoản nợ).</param>
/// <param name="DocumentCode">Mã chứng từ nguồn để đối chiếu, có thể null.</param>
/// <param name="RemainingAmountVnd">Số tiền còn lại của khoản nợ.</param>
/// <param name="PaymentAmountVnd">Số tiền thanh toán cho dòng này (người dùng sửa được).</param>
public sealed record DebtSettlementAllocationLine(
    string SourceType,
    long SourceId,
    string DisplayName,
    string? DocumentCode,
    decimal RemainingAmountVnd,
    decimal PaymentAmountVnd);

/// <summary>
/// Yêu cầu mở dialog phiếu thu/chi để tất toán công nợ (1 hoặc N khoản).
/// </summary>
/// <param name="VoucherType">FinancialVoucherTypes.In ("IN") = phiếu thu, Out ("OUT") = phiếu chi.</param>
/// <param name="PartyDisplayName">Tên đối tác hiển thị trên dialog (khách hàng / NCC / thợ ngoài).</param>
/// <param name="Allocations">Danh sách khoản nợ cần tất toán (1 dòng = lẻ, N dòng = gộp).</param>
/// <param name="DefaultNotes">Ghi chú điền sẵn, có thể null.</param>
/// <param name="AllowAmountEdit">false = khóa ô số tiền phân bổ.</param>
public sealed record DebtSettlementDialogRequest(
    string VoucherType,
    string PartyDisplayName,
    IReadOnlyList<DebtSettlementAllocationLine> Allocations,
    string? DefaultNotes,
    bool AllowAmountEdit = true);

/// <summary>
/// Kết quả dialog khi người dùng xác nhận: đủ dữ liệu để backend tạo
/// settlement + phiếu thu/chi + N dòng phân bổ trong 1 transaction.
/// </summary>
/// <param name="VoucherType">FinancialVoucherTypes.In / Out.</param>
/// <param name="VoucherDate">Ngày phiếu do người dùng chọn.</param>
/// <param name="Allocations">N dòng phân bổ với số tiền cuối cùng đã xác nhận.</param>
/// <param name="CashAmountVnd">Số tiền mặt.</param>
/// <param name="BankTransferAmountVnd">Số tiền chuyển khoản.</param>
/// <param name="CardAmountVnd">Số tiền thẻ.</param>
/// <param name="FundId">Quỹ tiền mặt (bắt buộc nếu CashAmountVnd &gt; 0).</param>
/// <param name="BankAccountId">Tài khoản NH (bắt buộc nếu BankTransfer/Card &gt; 0).</param>
/// <param name="PartyName">Tên đối tác.</param>
/// <param name="Notes">Ghi chú.</param>
/// <param name="ReferenceNumber">Số tham chiếu (chứng từ ngân hàng...), có thể null.</param>
public sealed record DebtSettlementDialogResult(
    string VoucherType,
    DateTime VoucherDate,
    IReadOnlyList<DebtSettlementAllocationLine> Allocations,
    decimal CashAmountVnd,
    decimal BankTransferAmountVnd,
    decimal CardAmountVnd,
    long? FundId,
    long? BankAccountId,
    string? PartyName,
    string? Notes,
    string? ReferenceNumber);

/// <summary>
/// Dịch vụ mở dialog phiếu thu/chi. Trả về null khi người dùng hủy.
/// Implementation WPF (CashVoucherDialog) do module Tài chính cung cấp.
/// </summary>
public interface IDebtSettlementDialogService
{
    DebtSettlementDialogResult? ShowDialog(DebtSettlementDialogRequest request);
}
