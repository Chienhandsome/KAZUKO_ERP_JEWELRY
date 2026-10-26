using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Finance;

public sealed class MonthlyFlowBar
{
    public required string MonthLabel { get; init; }
    public required decimal Income { get; init; }
    public required decimal Expense { get; init; }
    public required double IncomeHeight { get; init; }
    public required double ExpenseHeight { get; init; }
    public string IncomeText => Income.ToString("N0");
    public string ExpenseText => Expense.ToString("N0");
}

public sealed class ExpenseBreakdownRow
{
    public required string Category { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Percent { get; init; }
    public required string BrushKey { get; init; }
    public string AmountText => Amount.ToString("N0");
    public string PercentText => $"{Percent:N1}%";
}

public sealed class FundBalanceRow
{
    public required string FundName { get; init; }
    public required decimal Balance { get; init; }
    public required string Kind { get; init; }
    public string BalanceText => Balance.ToString("N0");
    public string KindBadge => Kind == "BANK" ? "NH" : "TM";
}

public sealed class RecentVoucherRow
{
    public required long Id { get; init; }
    public required int Index { get; init; }
    public required DateTime VoucherDate { get; init; }
    public required string Code { get; init; }
    public required string VoucherType { get; init; }
    public required string Content { get; init; }
    public required string PartyName { get; init; }
    public required string PaymentMethod { get; init; }
    public required decimal IncomeAmount { get; init; }
    public required decimal ExpenseAmount { get; init; }
    public required string CreatedByName { get; init; }
    public string CategoryName { get; init; } = "Chưa phân loại";
    public string DateText => VoucherDate.ToString("dd/MM/yyyy");
    public string TypeText => FinanceDisplayTexts.VoucherType(VoucherType);
    public string PaymentMethodText => FinanceDisplayTexts.PaymentMethod(PaymentMethod);
    public string IncomeText => IncomeAmount.ToString("N0");
    public string ExpenseText => ExpenseAmount.ToString("N0");
    public string AmountText => (IncomeAmount + ExpenseAmount).ToString("N0");
}

public sealed class DebtTopRow
{
    public required int Index { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required decimal Total { get; init; }
    public required decimal Paid { get; init; }
    public required decimal Remaining { get; init; }
    public string TotalText => Total.ToString("N0");
    public string PaidText => Paid.ToString("N0");
    public string RemainingText => Remaining.ToString("N0");
}

public sealed class VoucherKindOption(string code, string name)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class FinanceOverviewViewModel : ObservableObject
{
    private readonly Kazuko.Core.UserManagement.IAuthorizationService? _permissions;
    private bool CanView(string function) => _permissions?.Current.Allows(function) ?? true;
    private const double ChartMaxHeight = 150;

    private static readonly string[] BreakdownBrushKeys =
    [
        "ChartBrush.Sale", "ChartBrush.Raw", "ChartBrush.Purple",
        "ChartBrush.Finished", "ChartBrush.Muted"
    ];

    private readonly IFinanceDashboardRepository _dashboard;
    private readonly IFinanceVoucherService _voucherService;
    private readonly IPayableDebtRepository _payables;
    private readonly IDebtWorkbenchRepository _receivables;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly IVoucherCategoryService? _categories;

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _totalIncomeMonthText = "0";
    private string _incomeGrowthText = string.Empty;
    private string _incomeGrowthBrushKey = "SuccessBrush";
    private string _totalExpenseMonthText = "0";
    private string _expenseGrowthText = string.Empty;
    private string _expenseGrowthBrushKey = "DangerBrush";
    private string _cashFundBalanceText = "0";
    private string _bankBalanceTotalText = "0";
    private string _fundTotalText = "0";
    private string _revenueText = "0";
    private string _profitExpenseText = "0";
    private string _profitText = "0";
    private string _profitBrushKey = "SuccessBrush";
    private string _keyword = string.Empty;
    private DateTime? _fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _toDate = DateTime.Today;
    private VoucherKindOption? _selectedKind;

    public FinanceOverviewViewModel(
        IFinanceDashboardRepository dashboard,
        IFinanceVoucherService voucherService,
        IPayableDebtRepository payables,
        IDebtWorkbenchRepository receivables,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null, IVoucherCategoryService? categories = null)
    {
        _dashboard = dashboard;
        _voucherService = voucherService;
        _payables = payables;
        _receivables = receivables;
        _sessionAccessor = sessionAccessor;
        _funds = funds;
        _banks = banks;
        _permissions = authorization;
        _categories = categories;

        VoucherKindOptions.Add(new VoucherKindOption(string.Empty, "Tất cả loại"));
        VoucherKindOptions.Add(new VoucherKindOption(FinancialVoucherTypes.In, "Thu"));
        VoucherKindOptions.Add(new VoucherKindOption(FinancialVoucherTypes.Out, "Chi"));
        SelectedKind = VoucherKindOptions[0];

        SearchCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateIncomeCommand = new AsyncRelayCommand(() => AddVoucherAsync(FinancialVoucherTypes.In),
            () => !IsBusy && (_permissions?.Current.Allows("FIN_CASH_IN", Kazuko.Core.UserManagement.PermissionAction.Add) ?? true));
        CreateExpenseCommand = new AsyncRelayCommand(() => AddVoucherAsync(FinancialVoucherTypes.Out),
            () => !IsBusy && (_permissions?.Current.Allows("FIN_CASH_OUT", Kazuko.Core.UserManagement.PermissionAction.Add) ?? true));

        _ = LoadAsync();
    }

    public ObservableCollection<MonthlyFlowBar> MonthlyBars { get; } = [];
    public ObservableCollection<ExpenseBreakdownRow> ExpenseBreakdown { get; } = [];
    public ObservableCollection<FundBalanceRow> FundBalances { get; } = [];
    public ObservableCollection<RecentVoucherRow> RecentVouchers { get; } = [];
    public ObservableCollection<DebtTopRow> TopReceivables { get; } = [];
    public ObservableCollection<DebtTopRow> TopPayables { get; } = [];
    public ObservableCollection<VoucherKindOption> VoucherKindOptions { get; } = [];

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateIncomeCommand { get; }
    public ICommand CreateExpenseCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            ((AsyncRelayCommand)CreateIncomeCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CreateExpenseCommand).NotifyCanExecuteChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string TotalIncomeMonthText { get => _totalIncomeMonthText; private set => SetProperty(ref _totalIncomeMonthText, value); }
    public string IncomeGrowthText { get => _incomeGrowthText; private set => SetProperty(ref _incomeGrowthText, value); }
    public string IncomeGrowthBrushKey { get => _incomeGrowthBrushKey; private set => SetProperty(ref _incomeGrowthBrushKey, value); }
    public string TotalExpenseMonthText { get => _totalExpenseMonthText; private set => SetProperty(ref _totalExpenseMonthText, value); }
    public string ExpenseGrowthText { get => _expenseGrowthText; private set => SetProperty(ref _expenseGrowthText, value); }
    public string ExpenseGrowthBrushKey { get => _expenseGrowthBrushKey; private set => SetProperty(ref _expenseGrowthBrushKey, value); }
    public string CashFundBalanceText { get => _cashFundBalanceText; private set => SetProperty(ref _cashFundBalanceText, value); }
    public string BankBalanceTotalText { get => _bankBalanceTotalText; private set => SetProperty(ref _bankBalanceTotalText, value); }
    public string FundTotalText { get => _fundTotalText; private set => SetProperty(ref _fundTotalText, value); }
    public string RevenueText { get => _revenueText; private set => SetProperty(ref _revenueText, value); }
    public string ProfitExpenseText { get => _profitExpenseText; private set => SetProperty(ref _profitExpenseText, value); }
    public string ProfitText { get => _profitText; private set => SetProperty(ref _profitText, value); }
    public string ProfitBrushKey { get => _profitBrushKey; private set => SetProperty(ref _profitBrushKey, value); }

    public VoucherKindOption? SelectedKind
    {
        get => _selectedKind;
        set => SetProperty(ref _selectedKind, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    private async Task LoadAsync()
    {
        if (!CanView("FIN_FUND")) return;
        ErrorMessage = string.Empty;
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập không hợp lệ.";
            return;
        }

        IsBusy = true;
        try
        {
            var kpiTask = _dashboard.GetKpiAsync(session);
            var flowTask = _dashboard.GetMonthlyCashFlowAsync(12, session);
            var breakdownTask = _dashboard.GetExpenseBreakdownAsync(session);
            var fundsTask = _dashboard.GetFundBalancesAsync(session);
            var kind = string.IsNullOrWhiteSpace(SelectedKind?.Code) ? null : SelectedKind.Code;
            if (kind is null && !(CanView("FIN_CASH_IN") && CanView("FIN_CASH_OUT")))
                kind = CanView("FIN_CASH_IN") ? "IN" : "OUT";
            var vouchersTask = CanView(kind == "OUT" ? "FIN_CASH_OUT" : "FIN_CASH_IN") ? _dashboard.SearchVouchersAsync(
                kind,
                FromDate, ToDate,
                string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
                session) : Task.FromResult<IReadOnlyList<CashVoucherListItem>>([]);
            var receivableTask = CanView("DEBT_CUSTOMER") ? _receivables.SearchCustomerDebtsAsync(
                new CustomerDebtSearchFilter(string.Empty, 0, string.Empty, null, null), session) : Task.FromResult<IReadOnlyList<CustomerDebtRow>>([]);
            var payableTask = CanView("DEBT_CUSTOMER") ? _payables.SearchAsync(
                new PayableDebtSearchFilter(string.Empty, string.Empty, PayablePartyTypes.Supplier), session) : Task.FromResult<IReadOnlyList<PayableDebtListItem>>([]);

            await Task.WhenAll(kpiTask, flowTask, breakdownTask, fundsTask, vouchersTask, receivableTask, payableTask);

            ApplyKpi(await kpiTask);
            ApplyCashFlow(await flowTask);
            ApplyBreakdown(await breakdownTask);
            ApplyFunds(await fundsTask);
            ApplyRecentVouchers(await vouchersTask);
            ApplyTopReceivables(await receivableTask);
            ApplyTopPayables(await payableTask);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không tải được dữ liệu tài chính: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyKpi(FinanceKpi kpi)
    {
        TotalIncomeMonthText = kpi.TotalIncomeMonth.ToString("N0");
        IncomeGrowthText = FormatGrowth(kpi.IncomeGrowthPct);
        IncomeGrowthBrushKey = kpi.IncomeGrowthPct >= 0 ? "SuccessBrush" : "DangerBrush";

        TotalExpenseMonthText = kpi.TotalExpenseMonth.ToString("N0");
        ExpenseGrowthText = FormatGrowth(kpi.ExpenseGrowthPct);
        ExpenseGrowthBrushKey = kpi.ExpenseGrowthPct >= 0 ? "DangerBrush" : "SuccessBrush";

        CashFundBalanceText = kpi.CashFundBalance.ToString("N0");
        BankBalanceTotalText = kpi.BankBalanceTotal.ToString("N0");

        RevenueText = kpi.TotalIncomeMonth.ToString("N0");
        ProfitExpenseText = kpi.TotalExpenseMonth.ToString("N0");
        var profit = kpi.TotalIncomeMonth - kpi.TotalExpenseMonth;
        ProfitText = profit.ToString("N0");
        ProfitBrushKey = profit >= 0 ? "SuccessBrush" : "DangerBrush";
    }

    private static string FormatGrowth(decimal pct) =>
        $"{(pct >= 0 ? "+" : string.Empty)}{pct:N1}%";

    private void ApplyCashFlow(IReadOnlyList<MonthlyCashFlow> flows)
    {
        MonthlyBars.Clear();
        var max = flows.Select(f => Math.Max(f.Income, f.Expense)).DefaultIfEmpty(0).Max();
        foreach (var flow in flows)
        {
            MonthlyBars.Add(new MonthlyFlowBar
            {
                MonthLabel = flow.MonthLabel,
                Income = flow.Income,
                Expense = flow.Expense,
                IncomeHeight = max > 0 ? (double)(flow.Income / max) * ChartMaxHeight : 0,
                ExpenseHeight = max > 0 ? (double)(flow.Expense / max) * ChartMaxHeight : 0
            });
        }
    }

    private void ApplyBreakdown(IReadOnlyList<ExpenseBreakdown> items)
    {
        ExpenseBreakdown.Clear();
        var index = 0;
        foreach (var item in items)
        {
            ExpenseBreakdown.Add(new ExpenseBreakdownRow
            {
                Category = item.Category,
                Amount = item.Amount,
                Percent = item.Percent,
                BrushKey = BreakdownBrushKeys[index % BreakdownBrushKeys.Length]
            });
            index++;
        }
    }

    private void ApplyFunds(IReadOnlyList<FundBalance> funds)
    {
        FundBalances.Clear();
        foreach (var fund in funds)
        {
            FundBalances.Add(new FundBalanceRow
            {
                FundName = fund.FundName,
                Balance = fund.Balance,
                Kind = fund.Kind
            });
        }

        FundTotalText = funds.Sum(f => f.Balance).ToString("N0");
    }

    private void ApplyRecentVouchers(IReadOnlyList<CashVoucherListItem> items)
    {
        RecentVouchers.Clear();
        var index = 1;
        foreach (var item in items)
        {
            var isIncome = item.VoucherType == FinancialVoucherTypes.In;
            RecentVouchers.Add(new RecentVoucherRow
            {
                Id = item.Id,
                Index = index++,
                VoucherDate = item.VoucherDate,
                Code = item.Code,
                VoucherType = item.VoucherType,
                Content = item.Content,
                PartyName = item.PartyName,
                PaymentMethod = item.PaymentMethod,
                IncomeAmount = isIncome ? item.AmountVnd : 0,
                ExpenseAmount = isIncome ? 0 : item.AmountVnd,
                CategoryName = item.CategoryName ?? "Chưa phân loại",
                CreatedByName = item.CreatedByName
            });
        }
    }

    private void ApplyTopReceivables(IReadOnlyList<CustomerDebtRow> rows)
    {
        TopReceivables.Clear();
        var index = 1;
        foreach (var row in rows.Where(r => r.RemainingVnd > 0)
                                .OrderByDescending(r => r.RemainingVnd)
                                .Take(5))
        {
            TopReceivables.Add(new DebtTopRow
            {
                Index = index++,
                Code = row.Code,
                Name = row.Name,
                Total = row.TotalIncurredVnd,
                Paid = row.PaidVnd,
                Remaining = row.RemainingVnd
            });
        }
    }

    private void ApplyTopPayables(IReadOnlyList<PayableDebtListItem> items)
    {
        TopPayables.Clear();
        var index = 1;
        foreach (var group in items.Where(i => i.RemainingAmountVnd > 0)
                                   .GroupBy(i => i.PartyName)
                                   .Select(g => new
                                   {
                                       Name = g.Key,
                                       Total = g.Sum(x => x.OriginalAmountVnd),
                                       Paid = g.Sum(x => x.PaidAmountVnd),
                                       Remaining = g.Sum(x => x.RemainingAmountVnd)
                                   })
                                   .OrderByDescending(g => g.Remaining)
                                   .Take(5))
        {
            TopPayables.Add(new DebtTopRow
            {
                Index = index++,
                Code = string.Empty,
                Name = group.Name,
                Total = group.Total,
                Paid = group.Paid,
                Remaining = group.Remaining
            });
        }
    }

    private async Task AddVoucherAsync(string voucherType)
    {
        try
        {
            if (_permissions is not null)
                await _permissions.DemandAsync([new(voucherType == FinancialVoucherTypes.In ? "FIN_CASH_IN" : "FIN_CASH_OUT", Kazuko.Core.UserManagement.PermissionAction.Add)]);
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            var saved = CashVoucherDialog.Show(owner, _voucherService, _sessionAccessor, _funds, _banks, voucherType, _categories, _permissions);
            if (saved is not null) await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is InvalidOperationException or UnauthorizedAccessException ? ex.Message : "Không mở được form tạo phiếu. Vui lòng thử lại.";
        }
    }
}
