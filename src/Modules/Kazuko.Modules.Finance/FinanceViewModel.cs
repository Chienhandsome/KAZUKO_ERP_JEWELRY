using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Finance;

public sealed class FinanceViewModel : ObservableObject
{
    private int _selectedTabIndex;

    public FinanceViewModel(
        IFinanceDashboardRepository dashboard,
        IFinanceVoucherService voucherService,
        IPayableDebtRepository payables,
        IDebtWorkbenchRepository receivables,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null)
    {
        Overview = new FinanceOverviewViewModel(dashboard, voucherService, payables, receivables, sessionAccessor, funds, banks, authorization);
        Income = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.In, authorization);
        Expense = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.Out, authorization);
        if (authorization is not null && !authorization.Current.Allows("FIN_FUND"))
            _selectedTabIndex = authorization.Current.Allows("FIN_CASH_IN") ? 1 : 2;
    }

    public FinanceOverviewViewModel Overview { get; }

    public VoucherListViewModel Income { get; }

    public VoucherListViewModel Expense { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }
}
