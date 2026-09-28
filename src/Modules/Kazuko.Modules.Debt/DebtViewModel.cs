using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Debt;

public sealed class DebtViewModel : ObservableObject
{
    private readonly IPayableDebtRepository _debts;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private string _searchText = string.Empty;
    private string _statusFilter = "OPEN";
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private PayableDebtListItem? _selected;
    private decimal _payAmount;
    private string _payMethod = PaymentMethods.Cash;
    private CashFundOption? _selectedFund;
    private BankAccountOption? _selectedBank;

    public DebtViewModel(
        IPayableDebtRepository debts,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        ICurrentSessionAccessor sessionAccessor)
    {
        _debts = debts;
        _funds = funds;
        _banks = banks;
        _sessionAccessor = sessionAccessor;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PayCommand = new AsyncRelayCommand(PayAsync, () => Selected is not null && PayAmount > 0);
        _ = InitializeAsync();
    }

    public ObservableCollection<PayableDebtListItem> Items { get; } = new();
    public ObservableCollection<CashFundOption> Funds { get; } = new();
    public ObservableCollection<BankAccountOption> Banks { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "", "OPEN", "PARTIAL", "PAID" };

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set => SetProperty(ref _statusFilter, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public PayableDebtListItem? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                PayAmount = value.RemainingAmountVnd;
        }
    }

    public decimal PayAmount
    {
        get => _payAmount;
        set => SetProperty(ref _payAmount, value);
    }

    public string PayMethod
    {
        get => _payMethod;
        set => SetProperty(ref _payMethod, value);
    }

    public CashFundOption? SelectedFund
    {
        get => _selectedFund;
        set => SetProperty(ref _selectedFund, value);
    }

    public BankAccountOption? SelectedBank
    {
        get => _selectedBank;
        set => SetProperty(ref _selectedBank, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand PayCommand { get; }

    private async Task InitializeAsync()
    {
        var session = RequireSession();
        foreach (var fund in await _funds.GetActiveFundsAsync(session))
            Funds.Add(fund);
        foreach (var bank in await _banks.GetActiveAccountsAsync(session))
            Banks.Add(bank);
        SelectedFund = Funds.FirstOrDefault(x => x.IsDefault) ?? Funds.FirstOrDefault();
        SelectedBank = Banks.FirstOrDefault();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var session = RequireSession();
            var rows = await _debts.SearchAsync(
                new PayableDebtSearchFilter(SearchText, StatusFilter, string.Empty),
                session);
            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);
            StatusMessage = $"Hiển thị {Items.Count} khoản công nợ.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task PayAsync()
    {
        if (Selected is null)
            return;

        try
        {
            ErrorMessage = string.Empty;
            var session = RequireSession();
            var method = PayMethod.Trim().ToUpperInvariant();
            var allocation = method == PaymentMethods.BankTransfer
                ? new PaymentAllocationDraft(method, PayAmount, null, SelectedBank?.Id, null)
                : new PaymentAllocationDraft(method, PayAmount, SelectedFund?.Id, null, null);

            await _debts.PayAsync(
                new PayableDebtPaymentRequest(Selected.Id, [allocation], null),
                session);
            StatusMessage = "Đã thanh toán công nợ.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Công nợ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private AuthenticatedSession RequireSession() =>
        _sessionAccessor.Current
        ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
