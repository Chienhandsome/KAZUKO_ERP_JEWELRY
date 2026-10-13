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
}
