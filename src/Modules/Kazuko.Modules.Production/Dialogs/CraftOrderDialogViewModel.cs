using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
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
    private readonly IProductionQueryRepository _productionQueries;
    private readonly IProductionAttachmentStore _attachments;
    private readonly long _branchId;
    private long _sourceLocationId;

    private string _customerSearch = string.Empty;
    private CraftCustomerOption? _selectedCustomer;
    private bool _isCustomerSuggestionsOpen;
    private bool _suppressCustomerSearch;
    private CancellationTokenSource? _customerSearchCts;
    private DateTime _orderDate = DateTime.Today;
    private string _notes = string.Empty;
    private decimal _cashAmount;
    private decimal _transferAmount;
    private decimal _cardAmount;
    private CashFundOption? _selectedCashFund;
    private BankAccountOption? _selectedBankAccount;
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
        ICustomerEditorService? customerEditor,
        IProductionQueryRepository productionQueries,
        IProductionAttachmentStore attachments,
        long branchId)
    {
        _craftOrders = craftOrders;
        _sessionAccessor = sessionAccessor;
        _cashFunds = cashFunds;
        _bankAccounts = bankAccounts;
        _goldTypes = goldTypes;
        _customerEditor = customerEditor;
        _productionQueries = productionQueries;
        _attachments = attachments;
        _branchId = branchId;

        CreateCustomerCommand = new RelayCommand(CreateCustomer, () => !IsBusy && _customerEditor is not null);
        AddLineCommand = new AsyncRelayCommand(AddLineAsync, () => !IsBusy);
        EditLineCommand = new ParameterRelayCommand(
            p => _ = EditLineAsync(p as ProductionLineEditorViewModel),
            p => !IsBusy && p is ProductionLineEditorViewModel);
        RemoveLineCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is ProductionLineEditorViewModel row && Lines.Remove(row))
                {
                    foreach (var attachment in row.Attachments)
                        _attachments.TryDeleteStagingFile(attachment.StagingPath);
                    RenumberLines();
                }
                RecalcTotals();
            },
            p => !IsBusy && Lines.Count > 0 && p is ProductionLineEditorViewModel);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(_created is not null));

        _ = InitializeAsync();
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<CraftCustomerOption> Customers { get; } = [];
    public ObservableCollection<ProductionLineEditorViewModel> Lines { get; } = [];
    public ObservableCollection<CashFundOption> CashFunds { get; } = [];
    public ObservableCollection<BankAccountOption> BankAccounts { get; } = [];
    public ObservableCollection<GoldTypeOption> GoldTypes { get; } = [];

    public ICommand CreateCustomerCommand { get; }
    public ICommand AddLineCommand { get; }
    public ICommand EditLineCommand { get; }
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
            if (value is not null && value.IsWalkIn)
            {
                ErrorMessage = "Đơn đặt không dùng khách lẻ. Chọn khách có hồ sơ.";
                OnPropertyChanged(nameof(SelectedCustomer));
                return;
            }
            if (!SetProperty(ref _selectedCustomer, value))
                return;
            ErrorMessage = string.Empty;
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

    public decimal CashAmount
    {
        get => _cashAmount;
        set
        {
            if (SetProperty(ref _cashAmount, value))
            {
                OnPropertyChanged(nameof(DepositTotal));
                OnPropertyChanged(nameof(DepositTotalDisplay));
                NotifyCommands();
            }
        }
    }

    public decimal TransferAmount
    {
        get => _transferAmount;
        set
        {
            if (SetProperty(ref _transferAmount, value))
            {
                OnPropertyChanged(nameof(DepositTotal));
                OnPropertyChanged(nameof(DepositTotalDisplay));
                NotifyCommands();
            }
        }
    }

    public decimal CardAmount
    {
        get => _cardAmount;
        set
        {
            if (SetProperty(ref _cardAmount, value))
            {
                OnPropertyChanged(nameof(DepositTotal));
                OnPropertyChanged(nameof(DepositTotalDisplay));
                NotifyCommands();
            }
        }
    }

    public decimal DepositTotal => CashAmount + TransferAmount + CardAmount;

    public string DepositTotalDisplay => DepositTotal.ToString("N0", Vi) + " đ";

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

            var locations = await _productionQueries.GetLocationsAsync(_branchId);
            _sourceLocationId = locations.FirstOrDefault(x =>
                string.Equals(x.InventoryScope, Kazuko.Core.Warehouse.InventoryScopes.RawMaterial,
                    StringComparison.OrdinalIgnoreCase))?.Id ?? 0;

            // Nạp sẵn khách hàng để ComboBox có thể mở danh sách ngay cả khi
            // người dùng chưa gõ từ khóa.
            await SearchCustomersAsync(string.Empty, openSuggestions: false);

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
                await Task.Delay(100, cts.Token);
                await SearchCustomersAsync(string.Empty, openSuggestions: true, cts.Token);
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

    private async Task AddLineAsync()
    {
        var line = new ProductionLineEditorViewModel(Lines.Count + 1);
        if (await ShowLineDialogAsync(line, isNew: true))
        {
            Lines.Add(line);
            RenumberLines();
            RecalcTotals();
        }
    }

    private async Task EditLineAsync(ProductionLineEditorViewModel? line)
    {
        if (line is null)
            return;
        if (await ShowLineDialogAsync(line, isNew: false))
            RecalcTotals();
    }

    private async Task<bool> ShowLineDialogAsync(ProductionLineEditorViewModel line, bool isNew)
    {
        if (_sourceLocationId <= 0)
        {
            ErrorMessage = "Chưa cấu hình kho nguyên liệu cho chi nhánh.";
            return false;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var stock = await _productionQueries.SearchMaterialStockAsync(
                _branchId, _sourceLocationId, null);
            var goldStock = stock
                .Where(x => string.Equals(x.MaterialType, Kazuko.Core.Inventory.MaterialTypes.Gold,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sellPrices = new Dictionary<long, decimal>();
            foreach (var option in await _goldTypes.ListAsync(activeOnly: true))
            {
                var detail = await _goldTypes.GetByIdAsync(option.Id);
                if (detail is not null && detail.MaterialId > 0 && detail.SellPricePerChi > 0)
                    sellPrices[detail.MaterialId] = detail.SellPricePerChi;
            }

            var dialogVm = new ProductionLineEditDialogViewModel(
                _attachments, line.LineNumber, true, goldStock,
                isNew ? null : line, sellPrices);
            var dialog = new ProductionLineEditDialog
            {
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                    ?? Application.Current.MainWindow,
                DataContext = dialogVm
            };
            if (dialog.ShowDialog() != true)
                return false;

            var previousAttachments = line.Attachments.ToList();
            dialogVm.ApplyTo(line);
            foreach (var old in previousAttachments.Where(old =>
                         old.Id is null
                         && !string.IsNullOrWhiteSpace(old.StagingPath)
                         && line.Attachments.All(current => current.StagingPath != old.StagingPath)))
            {
                _attachments.TryDeleteStagingFile(old.StagingPath);
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RenumberLines()
    {
        var no = 0;
        foreach (var line in Lines)
            line.LineNumber = ++no;
    }

    private void RecalcTotals()
    {
        QuotedTotal = Lines.Sum(x => x.QuoteLineTotal);
        OnPropertyChanged(nameof(QuotedTotal));
        OnPropertyChanged(nameof(QuotedTotalDisplay));
        if (DepositTotal <= 0 && QuotedTotal > 0)
            CashAmount = Math.Round(QuotedTotal * 0.3m, 0, MidpointRounding.AwayFromZero);
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

            var lineDrafts = Lines.Select(x => x.ToCraftLineDraft()).ToList();

            var allocations = new List<PaymentAllocationDraft>();
            if (CashAmount > 0)
                allocations.Add(new PaymentAllocationDraft(
                    PaymentMethods.Cash, CashAmount, SelectedCashFund?.Id, null, null));
            if (TransferAmount > 0)
                allocations.Add(new PaymentAllocationDraft(
                    PaymentMethods.BankTransfer, TransferAmount, null, SelectedBankAccount?.Id, null));
            if (CardAmount > 0)
                allocations.Add(new PaymentAllocationDraft(
                    PaymentMethods.Card, CardAmount, null, SelectedBankAccount?.Id, null));

            var draft = new CraftOrderDraft(
                SelectedCustomer.Id,
                OrderDate,
                session.UserId > 0 ? session.UserId : null,
                lineDrafts,
                allocations,
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
        (AddLineCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed record CraftPaymentMethodOption(string Code, string DisplayName);
