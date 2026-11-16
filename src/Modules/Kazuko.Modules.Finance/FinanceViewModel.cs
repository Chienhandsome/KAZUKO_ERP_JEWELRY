using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Finance;

public sealed class FinanceViewModel
{
    public FinanceViewModel(
        IFinanceDashboardRepository dashboard,
        IFinanceVoucherService voucherService,
        IPayableDebtRepository payables,
        IDebtWorkbenchRepository receivables,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null, IVoucherCategoryService? categories = null)
    {
        Overview = new FinanceOverviewViewModel(dashboard, voucherService, payables, receivables, sessionAccessor, funds, banks, authorization, categories);
        Income = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.In, authorization, categories);
        Expense = new VoucherListViewModel(dashboard, voucherService, sessionAccessor, funds, banks, FinancialVoucherTypes.Out, authorization, categories);
    }

    public FinanceOverviewViewModel Overview { get; }

    public VoucherListViewModel Income { get; }

    public VoucherListViewModel Expense { get; }
}
