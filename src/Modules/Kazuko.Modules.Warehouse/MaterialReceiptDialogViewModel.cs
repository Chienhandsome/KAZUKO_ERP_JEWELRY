using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
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
    private readonly ISupplierEditorService _supplierEditor;
    private readonly IGoldTypeEditorService _goldTypeEditor;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly AuthenticatedSession _session;
    private SupplierListItem? _selectedSupplier;
    private WarehouseLocationOption? _selectedLocation;
    private MaterialOption? _selectedMaterial;
    private decimal _lineQuantity = 1;
    private decimal _lineUnitPrice;
    private decimal _lineExchangeRate = 1;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;
    private DateTime _documentDate = DateTime.Today;
    private string _documentNumberPreview = $"PNK-{DateTime.Today:yyyy}-XXXX";
    private string _description = string.Empty;
    private string _receivedByName = string.Empty;
    private decimal _discountAmount;
    private decimal _taxAmount;
    private decimal _shippingFee;

    public MaterialReceiptDialogViewModel(
        IMaterialReceiptService receiptService,
        IWarehouseRepository warehouse,
        ISupplierRepository suppliers,
        ISupplierEditorService supplierEditor,
        IGoldTypeEditorService goldTypeEditor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        AuthenticatedSession session)
    {
        _receiptService = receiptService;
        _warehouse = warehouse;
        _suppliers = suppliers;
        _supplierEditor = supplierEditor;
        _goldTypeEditor = goldTypeEditor;
        _funds = funds;
        _banks = banks;
        _session = session;

        MethodOptions.Add(new ReceiptPaymentMethodOption(PaymentMethods.Cash, "Tiền mặt"));
        MethodOptions.Add(new ReceiptPaymentMethodOption(PaymentMethods.BankTransfer, "Chuyển khoản"));

        AddLineCommand = new RelayCommand(AddLine);
        RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine is not null);
        CreateSupplierCommand = new RelayCommand(CreateSupplier);
        CreateGoldTypeCommand = new AsyncRelayCommand(CreateGoldTypeAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadLookupsAsync();
    }

    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();
    public ObservableCollection<MaterialOption> Materials { get; } = new();
    public ObservableCollection<CashFundOption> Funds { get; } = new();
    public ObservableCollection<BankAccountOption> Banks { get; } = new();
    public ObservableCollection<ReceiptPaymentMethodOption> MethodOptions { get; } = new();
    public ObservableCollection<ReceiptLineRow> Lines { get; } = new();

    public SupplierListItem? SelectedSupplier
    {
        get => _selectedSupplier;
        set => SetProperty(ref _selectedSupplier, value);
    }

    private BankAccountOption? _selectedBankAccount;

    /// <summary>Tài khoản ngân hàng cho chuyển khoản/thẻ.</summary>
    public BankAccountOption? SelectedBankAccount
    {
        get => _selectedBankAccount;
        set => SetProperty(ref _selectedBankAccount, value);
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

    private decimal _cashAmount;
    public decimal CashAmount
    {
        get => _cashAmount;
        set
        {
            if (SetProperty(ref _cashAmount, value))
                OnPropertyChanged(nameof(PaidAmount));
        }
    }

    private decimal _transferAmount;
    public decimal TransferAmount
    {
        get => _transferAmount;
        set
        {
            if (SetProperty(ref _transferAmount, value))
                OnPropertyChanged(nameof(PaidAmount));
        }
    }

    private decimal _cardAmount;
    public decimal CardAmount
    {
        get => _cardAmount;
        set
        {
            if (SetProperty(ref _cardAmount, value))
                OnPropertyChanged(nameof(PaidAmount));
        }
    }

    /// <summary>Tổng số tiền thanh toán (Tiền mặt + Chuyển khoản + Thẻ).</summary>
    public decimal PaidAmount => CashAmount + TransferAmount + CardAmount;

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
    public decimal DebtAmount => Math.Max(0, GrandTotal - PaidAmount);

    /// <summary>Ngày chứng từ (mặc định hôm nay).</summary>
    public DateTime DocumentDate
    {
        get => _documentDate;
        set
        {
            if (SetProperty(ref _documentDate, value))
                DocumentNumberPreview = $"PNK-{value:yyyy}-XXXX";
        }
    }

    /// <summary>Mã phiếu preview (sinh thật khi ghi sổ).</summary>
    public string DocumentNumberPreview
    {
        get => _documentNumberPreview;
        private set => SetProperty(ref _documentNumberPreview, value);
    }

    /// <summary>Diễn giải chứng từ.</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>Nhân viên nhập kho (nhập tay).</summary>
    public string ReceivedByName
    {
        get => _receivedByName;
        set => SetProperty(ref _receivedByName, value);
    }

    /// <summary>Giảm giá (VND, nhập tay).</summary>
    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
                NotifyTotalsChanged();
        }
    }

    /// <summary>Thuế (VND, nhập tay).</summary>
    public decimal TaxAmount
    {
        get => _taxAmount;
        set
        {
            if (SetProperty(ref _taxAmount, value))
                NotifyTotalsChanged();
        }
    }

    /// <summary>Phí vận chuyển (VND, nhập tay).</summary>
    public decimal ShippingFee
    {
        get => _shippingFee;
        set
        {
            if (SetProperty(ref _shippingFee, value))
                NotifyTotalsChanged();
        }
    }

    /// <summary>Tổng cộng = Tiền hàng - Giảm giá + Thuế + Phí vận chuyển.</summary>
    public decimal GrandTotal => Math.Max(0, TotalAmount - DiscountAmount + TaxAmount + ShippingFee);

    private void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(DebtAmount));
    }

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand CreateSupplierCommand { get; }
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
        NotifyTotalsChanged();
        ErrorMessage = string.Empty;
    }

    private void RemoveSelectedLine()
    {
        if (SelectedLine is null)
            return;
        Lines.Remove(SelectedLine);
        OnPropertyChanged(nameof(TotalAmount));
        NotifyTotalsChanged();
    }


    private void CreateSupplier()
    {
        var created = _supplierEditor.ShowCreate();
        if (created is null)
            return;
        _ = ReloadSuppliersAsync(created.Id);
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

            var payments = new List<PaymentAllocationDraft>();
            if (CashAmount > 0)
                payments.Add(new PaymentAllocationDraft(PaymentMethods.Cash, CashAmount, null, null, null));
            if (TransferAmount > 0)
                payments.Add(new PaymentAllocationDraft(PaymentMethods.BankTransfer, TransferAmount, null, SelectedBankAccount?.Id, null));
            if (CardAmount > 0)
                payments.Add(new PaymentAllocationDraft(PaymentMethods.Card, CardAmount, null, SelectedBankAccount?.Id, null));

            var draft = new MaterialReceiptDraft(
                DocumentDate,
                SelectedLocation.Id,
                StockSourceTypes.Supplier,
                SelectedSupplier?.Id,
                null,
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
