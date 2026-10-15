using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Finance;

public partial class CashVoucherDialog : Window
{
    public CashVoucherDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mở dialog tạo phiếu thu/chi. Module khác có thể gọi sau này.
    /// Trả về phiếu vừa tạo, hoặc null nếu người dùng hủy.
    /// </summary>
    /// <param name="owner">Cửa sổ cha (có thể null).</param>
    /// <param name="voucherService">Service tạo/hủy phiếu.</param>
    /// <param name="sessionAccessor">Phiên đăng nhập hiện tại.</param>
    /// <param name="voucherType">"IN" cho phiếu thu, "OUT" cho phiếu chi.</param>
    public static CashVoucherDetail? Show(
        Window? owner,
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        string voucherType)
    {
        var viewModel = new CashVoucherDialogViewModel(voucherService, sessionAccessor, voucherType);
        var dialog = new CashVoucherDialog { DataContext = viewModel };
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        viewModel.RequestClose = _ => dialog.Close();

        dialog.ShowDialog();
        return viewModel.SavedVoucher;
    }

    /// <summary>
    /// Mở dialog để tất toán công nợ. Chỉ thu thập input, KHÔNG tự tạo voucher
    /// (backend Công nợ tạo settlement + voucher trong 1 transaction).
    /// Trả về input đã xác nhận, hoặc null nếu người dùng hủy.
    /// </summary>
    /// <param name="owner">Cửa sổ cha (có thể null).</param>
    /// <param name="voucherService">Service phiếu (không được dùng ở chế độ này, giữ tham số để thống nhất).</param>
    /// <param name="sessionAccessor">Phiên đăng nhập hiện tại.</param>
    /// <param name="request">Yêu cầu pre-fill cho dialog.</param>
    public static DebtSettlementDialogResult? ShowForDebtSettlement(
        Window? owner,
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        DebtSettlementDialogRequest request)
    {
        var viewModel = new CashVoucherDialogViewModel(voucherService, sessionAccessor, request);
        var dialog = new CashVoucherDialog { DataContext = viewModel };
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        viewModel.RequestClose = _ => dialog.Close();

        dialog.ShowDialog();
        return viewModel.SettlementResult;
    }
}
