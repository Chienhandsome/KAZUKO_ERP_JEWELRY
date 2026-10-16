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
        IBankAccountRepository banks)
    {
        Overview = new FinanceOverviewViewModel(dashboard, voucherService, payables, receivables, sessionAccessor, funds, banks);
        Income = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.In);
        Expense = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.Out);
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
