using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Production.Dialogs;

public sealed class CraftOrderDialogViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ICraftOrderApplicationService _craftOrders;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly ICashFundRepository _cashFunds;
    private readonly IBankAccountRepository _bankAccounts;
    private readonly IGoldTypeCatalogService _goldTypes;
    private readonly ICustomerEditorService? _customerEditor;

    private string _customerSearch = string.Empty;
    private CraftCustomerOption? _selectedCustomer;
    private bool _isCustomerSuggestionsOpen;
    private bool _suppressCustomerSearch;
    private CancellationTokenSource? _customerSearchCts;
    private DateTime _orderDate = DateTime.Today;
    private string _notes = string.Empty;
    private decimal _depositAmount;
    private string _paymentMethod = PaymentMethods.Cash;
    private CashFundOption? _selectedCashFund;
    private BankAccountOption? _selectedBankAccount;
    private string _paymentReference = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private CraftOrderCreateResult? _created;

    public CraftOrderDialogViewModel(
        ICraftOrderApplicationService craftOrders,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository cashFunds,
        IBankAccountRepository bankAccounts,
        IGoldTypeCatalogService goldTypes,
        ICustomerEditorService? customerEditor = null)
    {
        _craftOrders = craftOrders;
        _sessionAccessor = sessionAccessor;
        _cashFunds = cashFunds;
        _bankAccounts = bankAccounts;
        _goldTypes = goldTypes;
        _customerEditor = customerEditor;

        SelectCustomerCommand = new ParameterRelayCommand(
            p => SelectCustomer(p as CraftCustomerOption),
            p => p is CraftCustomerOption && !IsBusy);
        CreateCustomerCommand = new RelayCommand(CreateCustomer, () => !IsBusy && _customerEditor is not null);
        CloseCustomerSuggestionsCommand = new RelayCommand(() => IsCustomerSuggestionsOpen = false);
        AddLineCommand = new RelayCommand(AddLine, () => !IsBusy);
        RemoveLineCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is CraftOrderLineEditorRow row && Lines.Count > 1)
                    Lines.Remove(row);
                RecalcTotals();
            },
            p => !IsBusy && Lines.Count > 1 && p is CraftOrderLineEditorRow);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(_created is not null));

        Lines.Add(new CraftOrderLineEditorRow(RecalcTotals));
        _ = InitializeAsync();
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<CraftCustomerOption> Customers { get; } = [];
    public ObservableCollection<CraftOrderLineEditorRow> Lines { get; } = [];
    public ObservableCollection<CashFundOption> CashFunds { get; } = [];
    public ObservableCollection<BankAccountOption> BankAccounts { get; } = [];
    public ObservableCollection<GoldTypeOption> GoldTypes { get; } = [];

    public IReadOnlyList<CraftPaymentMethodOption> PaymentMethodOptions { get; } =
    [
        new(PaymentMethods.Cash, "Tiền mặt"),
        new(PaymentMethods.BankTransfer, "Chuyển khoản"),
        new(PaymentMethods.Card, "Thẻ")
    ];

    public CraftPaymentMethodOption? SelectedPaymentMethodOption
    {
        get => PaymentMethodOptions.FirstOrDefault(x =>
            string.Equals(x.Code, PaymentMethod, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
                return;
            PaymentMethod = value.Code;
            OnPropertyChanged(nameof(SelectedPaymentMethodOption));
        }
    }

    public ICommand SelectCustomerCommand { get; }
    public ICommand CreateCustomerCommand { get; }
    public ICommand CloseCustomerSuggestionsCommand { get; }
    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }

    public CraftOrderCreateResult? Created => _created;

    public string CustomerSearch
    {
        get => _customerSearch;
        set
        {
            if (!SetProperty(ref _customerSearch, value))
                return;
            if (_suppressCustomerSearch)
                return;

            if (SelectedCustomer is not null
                && !string.Equals(value, SelectedCustomer.DisplayText, StringComparison.Ordinal))
            {
                _selectedCustomer = null;
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(SelectedCustomerHint));
                OnPropertyChanged(nameof(HasSelectedCustomer));
            }

            _ = DebouncedSearchCustomersAsync(value);
        }
    }

    public CraftCustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (!SetProperty(ref _selectedCustomer, value))
                return;
            OnPropertyChanged(nameof(SelectedCustomerHint));
            OnPropertyChanged(nameof(HasSelectedCustomer));
            if (value is null)
                return;
            _suppressCustomerSearch = true;
            CustomerSearch = value.DisplayText;
            _suppressCustomerSearch = false;
            IsCustomerSuggestionsOpen = false;
        }
    }

    public bool IsCustomerSuggestionsOpen
    {
        get => _isCustomerSuggestionsOpen;
        set => SetProperty(ref _isCustomerSuggestionsOpen, value);
    }

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    public string SelectedCustomerHint =>
        SelectedCustomer is null
            ? string.Empty
            : $"Đã chọn: {SelectedCustomer.DisplayText}";

    public DateTime OrderDate
    {
        get => _orderDate;
        set => SetProperty(ref _orderDate, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public decimal QuotedTotal { get; private set; }

    public string QuotedTotalDisplay => QuotedTotal.ToString("N0", Vi) + " đ";

    public decimal DepositAmount
    {
        get => _depositAmount;
        set
        {
            if (SetProperty(ref _depositAmount, value))
                NotifyCommands();
        }
    }

    public string PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (!SetProperty(ref _paymentMethod, value))
                return;
            OnPropertyChanged(nameof(RequiresCashFund));
            OnPropertyChanged(nameof(RequiresBankAccount));
            OnPropertyChanged(nameof(SelectedPaymentMethodOption));
        }
    }

    public bool RequiresCashFund =>
        string.Equals(PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);

    public bool RequiresBankAccount =>
        string.Equals(PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(PaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase);

    public CashFundOption? SelectedCashFund
    {
        get => _selectedCashFund;
        set => SetProperty(ref _selectedCashFund, value);
    }

    public BankAccountOption? SelectedBankAccount
    {
        get => _selectedBankAccount;
        set => SetProperty(ref _selectedBankAccount, value);
    }

    public string PaymentReference
    {
        get => _paymentReference;
        set => SetProperty(ref _paymentReference, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var session = RequireSession();
            foreach (var fund in await _cashFunds.GetActiveFundsAsync(session))
                CashFunds.Add(fund);
            foreach (var bank in await _bankAccounts.GetActiveAccountsAsync(session))
                BankAccounts.Add(bank);
            foreach (var gold in await _goldTypes.ListAsync(activeOnly: true))
                GoldTypes.Add(gold);

            SelectedCashFund = CashFunds.FirstOrDefault(x => x.IsDefault) ?? CashFunds.FirstOrDefault();
            SelectedBankAccount = BankAccounts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DebouncedSearchCustomersAsync(string searchText)
    {
        _customerSearchCts?.Cancel();
        _customerSearchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _customerSearchCts = cts;

        try
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                Customers.Clear();
                IsCustomerSuggestionsOpen = false;
                return;
            }

            await Task.Delay(250, cts.Token);
            await SearchCustomersAsync(searchText, openSuggestions: true, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // newer keystroke cancelled this search
        }
    }

    private async Task SearchCustomersAsync(
        string? searchText,
        bool openSuggestions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = RequireSession();
            var items = await _craftOrders.SearchCustomersAsync(searchText, session, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Customers.Clear();
            foreach (var item in items)
                Customers.Add(item);

            if (openSuggestions)
                IsCustomerSuggestionsOpen = Customers.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsCustomerSuggestionsOpen = false;
        }
    }

    private void SelectCustomer(CraftCustomerOption? customer)
    {
        if (customer is null)
            return;
        if (customer.IsWalkIn)
        {
            ErrorMessage = "Đơn đặt không dùng khách lẻ. Chọn khách có hồ sơ.";
            return;
        }

        ErrorMessage = string.Empty;
        SelectedCustomer = customer;
    }

    private void CreateCustomer()
    {
        if (_customerEditor is null)
            return;

        try
        {
            ErrorMessage = string.Empty;
            _ = RequireSession();
            var result = _customerEditor.ShowCreate();
            if (result is null)
                return;

            var code = result.Code.Trim();
            var isWalkIn = string.Equals(code, "KH-VL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "WALK_IN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "KHACH_LE", StringComparison.OrdinalIgnoreCase);
            if (isWalkIn)
            {
                ErrorMessage = "Đơn đặt không dùng khách lẻ. Chọn khách có hồ sơ.";
                return;
            }

            var option = new CraftCustomerOption(
                result.Id,
                code,
                result.Name,
                string.IsNullOrWhiteSpace(result.Phone) ? null : result.Phone.Trim(),
                IsWalkIn: false);

            if (Customers.All(x => x.Id != option.Id))
                Customers.Insert(0, option);
            SelectedCustomer = option;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void AddLine()
    {
        Lines.Add(new CraftOrderLineEditorRow(RecalcTotals));
        RecalcTotals();
    }

    private void RecalcTotals()
    {
        QuotedTotal = Lines.Sum(x => x.LineTotal);
        OnPropertyChanged(nameof(QuotedTotal));
        OnPropertyChanged(nameof(QuotedTotalDisplay));
        if (DepositAmount <= 0 && QuotedTotal > 0)
            DepositAmount = Math.Round(QuotedTotal * 0.3m, 0, MidpointRounding.AwayFromZero);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            IsBusy = true;
            var session = RequireSession();
            if (SelectedCustomer is null)
                throw new InvalidOperationException("Chọn khách hàng đặt hàng.");

            var lineDrafts = Lines.Select(x => x.ToDraft()).ToList();
            var allocation = new PaymentAllocationDraft(
                PaymentMethod,
                DepositAmount,
                RequiresCashFund ? SelectedCashFund?.Id : null,
                RequiresBankAccount ? SelectedBankAccount?.Id : null,
                string.IsNullOrWhiteSpace(PaymentReference) ? null : PaymentReference.Trim());

            var draft = new CraftOrderDraft(
                SelectedCustomer.Id,
                OrderDate,
                session.UserId > 0 ? session.UserId : null,
                lineDrafts,
                [allocation],
                string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());

            _created = await _craftOrders.CreateWithDepositAsync(draft, session);
            StatusMessage =
                $"Đã tạo đơn đặt {_created.Code}. Cọc {_created.DepositAmountVnd.ToString("N0", Vi)} đ.";
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AuthenticatedSession RequireSession() =>
        _sessionAccessor.Current
        ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

    private void NotifyCommands()
    {
        (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (CreateCustomerCommand as RelayCommand)?.NotifyCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed record CraftPaymentMethodOption(string Code, string DisplayName);

public sealed class CraftOrderLineEditorRow : ObservableObject
{
    private readonly Action _onChanged;
    private string _modelName = string.Empty;
    private string _goldPurity = string.Empty;
    private int _quantity = 1;
    private decimal _estimatedWeightChi;
    private decimal _goldUnitPricePerChi;
    private decimal _laborAmountPerUnitVnd;
    private decimal _stoneAmountPerUnitVnd;

    public CraftOrderLineEditorRow(Action onChanged) => _onChanged = onChanged;

    public string ModelName
    {
        get => _modelName;
        set { if (SetProperty(ref _modelName, value)) Notify(); }
    }

    public string GoldPurity
    {
        get => _goldPurity;
        set { if (SetProperty(ref _goldPurity, value)) Notify(); }
    }

    public GoldTypeOption? SelectedGoldType
    {
        get => null;
        set
        {
            if (value is not null)
                GoldPurity = value.Code;
        }
    }

    public int Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) Notify(); }
    }

    public decimal EstimatedWeightChi
    {
        get => _estimatedWeightChi;
        set { if (SetProperty(ref _estimatedWeightChi, value)) Notify(); }
    }

    public decimal GoldUnitPricePerChi
    {
        get => _goldUnitPricePerChi;
        set { if (SetProperty(ref _goldUnitPricePerChi, value)) Notify(); }
    }

    public decimal LaborAmountPerUnitVnd
    {
        get => _laborAmountPerUnitVnd;
        set { if (SetProperty(ref _laborAmountPerUnitVnd, value)) Notify(); }
    }

    public decimal StoneAmountPerUnitVnd
    {
        get => _stoneAmountPerUnitVnd;
        set { if (SetProperty(ref _stoneAmountPerUnitVnd, value)) Notify(); }
    }

    public decimal LineTotal =>
        CraftOrderValidator.LineTotal(ToDraft());

    public string LineTotalDisplay =>
        LineTotal.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";

    public CraftOrderLineDraft ToDraft() =>
        new(ModelName, GoldPurity, Quantity, EstimatedWeightChi,
            GoldUnitPricePerChi, LaborAmountPerUnitVnd, StoneAmountPerUnitVnd, null);

    private void Notify()
    {
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(LineTotalDisplay));
        _onChanged();
    }
}
