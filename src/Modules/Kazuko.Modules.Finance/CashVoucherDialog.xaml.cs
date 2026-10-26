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
    public static CashVoucherDetail? Show(
        Window? owner,
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository fundRepository,
        IBankAccountRepository bankRepository,
        string voucherType, IVoucherCategoryService? categories = null,
        Kazuko.Core.UserManagement.IAuthorizationService? permissions = null)
    {
        var session = sessionAccessor.Current;
        var funds = session is null
            ? Array.Empty<CashFundOption>()
            : fundRepository.GetActiveFundsAsync(session).GetAwaiter().GetResult();
        var banks = session is null
            ? Array.Empty<BankAccountOption>()
            : bankRepository.GetActiveAccountsAsync(session).GetAwaiter().GetResult();

        var viewModel = new CashVoucherDialogViewModel(voucherService, sessionAccessor, funds, banks, voucherType, categories, permissions);
        var dialog = new CashVoucherDialog { DataContext = viewModel };
        dialog.Loaded += async (_, _) => await viewModel.LoadCategoriesAsync();
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
    public static DebtSettlementDialogResult? ShowForDebtSettlement(
        Window? owner,
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository fundRepository,
        IBankAccountRepository bankRepository,
        DebtSettlementDialogRequest request)
    {
        var session = sessionAccessor.Current;
        var funds = session is null
            ? Array.Empty<CashFundOption>()
            : fundRepository.GetActiveFundsAsync(session).GetAwaiter().GetResult();
        var banks = session is null
            ? Array.Empty<BankAccountOption>()
            : bankRepository.GetActiveAccountsAsync(session).GetAwaiter().GetResult();

        var viewModel = new CashVoucherDialogViewModel(voucherService, sessionAccessor, funds, banks, request);
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
