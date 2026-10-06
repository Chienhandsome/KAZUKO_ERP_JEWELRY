using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class MaterialReceiptDialogViewModel : ObservableObject
{
    private readonly IMaterialReceiptService _receiptService;
    private readonly IWarehouseRepository _warehouse;
    private readonly ISupplierRepository _suppliers;
    private readonly ICustomerRepository _customers;
    private readonly ISupplierEditorService _supplierEditor;
    private readonly ICustomerEditorService _customerEditor;
    private readonly IGoldTypeEditorService _goldTypeEditor;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly AuthenticatedSession _session;
    private string _sourceType = StockSourceTypes.Supplier;
    private SupplierListItem? _selectedSupplier;
    private CustomerListItem? _selectedCustomer;
    private WarehouseLocationOption? _selectedLocation;
    private MaterialOption? _selectedMaterial;
    private decimal _lineQuantity = 1;
    private decimal _lineUnitPrice;
    private decimal _lineExchangeRate = 1;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public MaterialReceiptDialogViewModel(
        IMaterialReceiptService receiptService,
        IWarehouseRepository warehouse,
        ISupplierRepository suppliers,
        ICustomerRepository customers,
        ISupplierEditorService supplierEditor,
        ICustomerEditorService customerEditor,
        IGoldTypeEditorService goldTypeEditor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        AuthenticatedSession session)
    {
        _receiptService = receiptService;
        _warehouse = warehouse;
        _suppliers = suppliers;
        _customers = customers;
        _supplierEditor = supplierEditor;
        _customerEditor = customerEditor;
        _goldTypeEditor = goldTypeEditor;
        _funds = funds;
        _banks = banks;
        _session = session;

        MethodOptions.Add(new ReceiptPaymentMethodOption(PaymentMethods.Cash, "Tiền mặt"));
        MethodOptions.Add(new ReceiptPaymentMethodOption(PaymentMethods.BankTransfer, "Chuyển khoản"));

        AddLineCommand = new RelayCommand(AddLine);
        RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine is not null);
        AddPaymentCommand = new RelayCommand(AddPayment);
        RemovePaymentCommand = new RelayCommand(RemoveSelectedPayment, () => SelectedPayment is not null);
        CreateSupplierCommand = new RelayCommand(CreateSupplier);
        CreateCustomerCommand = new RelayCommand(CreateCustomer);
        CreateGoldTypeCommand = new AsyncRelayCommand(CreateGoldTypeAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadLookupsAsync();
    }

    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();
    public ObservableCollection<CustomerListItem> Customers { get; } = new();
    public ObservableCollection<MaterialOption> Materials { get; } = new();
    public ObservableCollection<CashFundOption> Funds { get; } = new();
    public ObservableCollection<BankAccountOption> Banks { get; } = new();
    public ObservableCollection<ReceiptPaymentMethodOption> MethodOptions { get; } = new();
    public ObservableCollection<ReceiptLineRow> Lines { get; } = new();
    public ObservableCollection<PaymentRow> Payments { get; } = new();

    public string SourceType
    {
        get => _sourceType;
        set
        {
            if (SetProperty(ref _sourceType, value))
            {
                OnPropertyChanged(nameof(IsSupplierSource));
                OnPropertyChanged(nameof(IsOldGoldSource));
            }
        }
    }

    public bool IsSupplierSource
    {
        get => string.Equals(SourceType, StockSourceTypes.Supplier, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SourceType = StockSourceTypes.Supplier;
        }
    }

    public bool IsOldGoldSource
    {
        get => string.Equals(SourceType, StockSourceTypes.OldGoldCustomer, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SourceType = StockSourceTypes.OldGoldCustomer;
        }
    }

    public SupplierListItem? SelectedSupplier
    {
        get => _selectedSupplier;
        set => SetProperty(ref _selectedSupplier, value);
    }

    public CustomerListItem? SelectedCustomer
    {
        get => _selectedCustomer;
        set => SetProperty(ref _selectedCustomer, value);
    }

    public WarehouseLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set => SetProperty(ref _selectedLocation, value);
    }

    public MaterialOption? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
            {
                OnPropertyChanged(nameof(UnitPriceLabel));
                if (value is not null
                    && string.Equals(value.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase)
                    && value.ReferenceBuyPricePerChi > 0)
                {
                    LineUnitPrice = value.ReferenceBuyPricePerChi;
                }
            }
        }
    }

    public string UnitPriceLabel =>
        SelectedMaterial is not null &&
        string.Equals(SelectedMaterial.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase)
            ? "Đơn giá/chỉ"
            : "Đơn giá/đơn vị";

    public decimal LineQuantity
    {
        get => _lineQuantity;
        set => SetProperty(ref _lineQuantity, value);
    }

    public decimal LineUnitPrice
    {
        get => _lineUnitPrice;
        set => SetProperty(ref _lineUnitPrice, value);
    }

    public decimal LineExchangeRate
    {
        get => _lineExchangeRate;
        set => SetProperty(ref _lineExchangeRate, value);
    }

    public ReceiptLineRow? SelectedLine { get; set; }
    public PaymentRow? SelectedPayment { get; set; }

    /// <summary>Tổng số tiền các dòng phương thức thanh toán (không nhập tay).</summary>
    public decimal PaidAmount => Payments.Sum(x => x.AmountVnd);

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        set => SetProperty(ref _dialogResult, value);
    }

    public decimal TotalAmount => Lines.Sum(x => x.AmountVnd);
    public decimal DebtAmount => Math.Max(0, TotalAmount - PaidAmount);

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand AddPaymentCommand { get; }
    public ICommand RemovePaymentCommand { get; }
    public ICommand CreateSupplierCommand { get; }
    public ICommand CreateCustomerCommand { get; }
    public ICommand CreateGoldTypeCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task LoadLookupsAsync()
    {
        foreach (var location in await _warehouse.GetLocationsAsync(_session))
        {
            if (location.InventoryScope is InventoryScopes.RawMaterial)
                Locations.Add(location);
        }

        SelectedLocation = Locations.FirstOrDefault(x =>
                string.Equals(x.InventoryScope, InventoryScopes.RawMaterial, StringComparison.OrdinalIgnoreCase))
            ?? Locations.FirstOrDefault();

        foreach (var supplier in await _suppliers.SearchAsync(new SupplierSearchFilter(string.Empty, 0, string.Empty, true)))
            Suppliers.Add(supplier);
        foreach (var customer in await _customers.SearchAsync(new CustomerSearchFilter(string.Empty, 0, string.Empty, true)))
            Customers.Add(customer);
        foreach (var material in await _warehouse.SearchMaterialsAsync(string.Empty, string.Empty, _session))
            Materials.Add(material);
        foreach (var fund in await _funds.GetActiveFundsAsync(_session))
            Funds.Add(fund);
        foreach (var bank in await _banks.GetActiveAccountsAsync(_session))
            Banks.Add(bank);
    }

    private void AddLine()
    {
        if (SelectedMaterial is null || LineQuantity <= 0)
        {
            ErrorMessage = "Chọn NVL và nhập số lượng hợp lệ.";
            return;
        }

        Lines.Add(new ReceiptLineRow(
            SelectedMaterial.Id,
            SelectedMaterial.Code,
            SelectedMaterial.Name,
            SelectedMaterial.MaterialType,
            SelectedMaterial.UnitCode,
            LineQuantity,
            LineUnitPrice,
            LineExchangeRate));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(DebtAmount));
        ErrorMessage = string.Empty;
    }

    private void RemoveSelectedLine()
    {
        if (SelectedLine is null)
            return;
        Lines.Remove(SelectedLine);
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(DebtAmount));
    }

    private void AddPayment()
    {
        var row = new PaymentRow
        {
            PaymentMethod = PaymentMethods.Cash,
            AmountVnd = 0,
            CashFundId = Funds.FirstOrDefault(x => x.IsDefault)?.Id ?? Funds.FirstOrDefault()?.Id
        };
        row.PropertyChanged += OnPaymentRowPropertyChanged;
        Payments.Add(row);
        NotifyPaidChanged();
    }

    private void RemoveSelectedPayment()
    {
        if (SelectedPayment is null)
            return;
        SelectedPayment.PropertyChanged -= OnPaymentRowPropertyChanged;
        Payments.Remove(SelectedPayment);
        NotifyPaidChanged();
    }

    private void OnPaymentRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentRow.AmountVnd))
            NotifyPaidChanged();
    }

    private void NotifyPaidChanged()
    {
        OnPropertyChanged(nameof(PaidAmount));
        OnPropertyChanged(nameof(DebtAmount));
    }

    private void CreateSupplier()
    {
        var created = _supplierEditor.ShowCreate();
        if (created is null)
            return;
        _ = ReloadSuppliersAsync(created.Id);
    }

    private void CreateCustomer()
    {
        var created = _customerEditor.ShowCreate();
        if (created is null)
            return;
        _ = ReloadCustomersAsync(created.Id);
    }

    private async Task CreateGoldTypeAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var created = await _goldTypeEditor.ShowCreateAsync(_session);
            if (created is null)
                return;
            await ReloadMaterialsAsync(created.Code);
        }
        catch (Exception ex)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Không thể thêm loại vàng."
                : ex.Message;
        }
    }

    private async Task ReloadSuppliersAsync(long selectId)
    {
        Suppliers.Clear();
        foreach (var supplier in await _suppliers.SearchAsync(new SupplierSearchFilter(string.Empty, 0, string.Empty, true)))
            Suppliers.Add(supplier);
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == selectId);
        SourceType = StockSourceTypes.Supplier;
    }

    private async Task ReloadCustomersAsync(long selectId)
    {
        Customers.Clear();
        foreach (var customer in await _customers.SearchAsync(new CustomerSearchFilter(string.Empty, 0, string.Empty, true)))
            Customers.Add(customer);
        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == selectId);
        SourceType = StockSourceTypes.OldGoldCustomer;
    }

    private async Task ReloadMaterialsAsync(string goldTypeCode)
    {
        var code = goldTypeCode.Trim().ToUpperInvariant();
        var materialCode = "GOLD_" + code;
        Materials.Clear();
        foreach (var material in await _warehouse.SearchMaterialsAsync(string.Empty, string.Empty, _session))
            Materials.Add(material);
        SelectedMaterial = Materials.FirstOrDefault(x =>
            string.Equals(x.Code, materialCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.PurityCode, code, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            if (SelectedLocation is null)
                throw new InvalidOperationException("Chọn kho nhận.");

            var payments = Payments.Select(p => new PaymentAllocationDraft(
                p.PaymentMethod,
                p.AmountVnd,
                p.CashFundId,
                p.BankAccountId,
                p.ReferenceNumber)).ToList();

            var draft = new MaterialReceiptDraft(
                DateTime.Now,
                SelectedLocation.Id,
                SourceType,
                IsSupplierSource ? SelectedSupplier?.Id : null,
                IsSupplierSource ? null : SelectedCustomer?.Id,
                Notes,
                Lines.Select(l => new MaterialReceiptLineDraft(
                    l.MaterialId, l.Quantity, l.UnitPrice, "VND", l.ExchangeRate, null)).ToList(),
                PaidAmount,
                payments);

            await _receiptService.CreateDraftAndPostAsync(draft, _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Nhập NVL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public sealed class ReceiptLineRow
{
    public ReceiptLineRow(
        long materialId,
        string code,
        string name,
        string materialType,
        string unitCode,
        decimal quantity,
        decimal unitPrice,
        decimal exchangeRate)
    {
        MaterialId = materialId;
        Code = code;
        Name = name;
        MaterialType = materialType;
        UnitCode = unitCode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        ExchangeRate = exchangeRate;
    }

    public long MaterialId { get; }
    public string Code { get; }
    public string Name { get; }
    public string MaterialType { get; }
    public string UnitCode { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal ExchangeRate { get; }
    public decimal AmountVnd => Math.Round(Quantity * UnitPrice * ExchangeRate, 2, MidpointRounding.AwayFromZero);
}

public sealed record ReceiptPaymentMethodOption(string Code, string Label);

public sealed class PaymentRow : ObservableObject
{
    private string _paymentMethod = PaymentMethods.Cash;
    private decimal _amountVnd;
    private long? _cashFundId;
    private long? _bankAccountId;
    private string? _referenceNumber;

    public string PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (!SetProperty(ref _paymentMethod, value))
                return;

            OnPropertyChanged(nameof(RequiresCashFund));
            OnPropertyChanged(nameof(RequiresBankAccount));

            if (RequiresCashFund)
                BankAccountId = null;
            else if (RequiresBankAccount)
                CashFundId = null;
        }
    }

    public decimal AmountVnd
    {
        get => _amountVnd;
        set => SetProperty(ref _amountVnd, value);
    }

    public long? CashFundId
    {
        get => _cashFundId;
        set => SetProperty(ref _cashFundId, value);
    }

    public long? BankAccountId
    {
        get => _bankAccountId;
        set => SetProperty(ref _bankAccountId, value);
    }

    public string? ReferenceNumber
    {
        get => _referenceNumber;
        set => SetProperty(ref _referenceNumber, value);
    }

    public bool RequiresCashFund =>
        string.Equals(PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);

    public bool RequiresBankAccount =>
        string.Equals(PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase);
}
