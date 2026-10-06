using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales;

public sealed class BuySellWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private readonly Action _onChanged;

    private DateTime _documentDate = DateTime.Today;
    private string _documentCode = string.Empty;
    private SalesCustomerOption? _selectedCustomer;
    private SalespersonOption? _selectedStaff;
    private SalesLocationOption? _selectedOldGoldLocation;
    private OldGoldMaterialOption? _selectedOldGoldMaterial;
    private int _contentTabIndex;
    private string _productCodeInput = string.Empty;
    private decimal _discountAmount;
    private SellLineRowViewModel? _selectedSellLine;
    private BuyOldLineRowViewModel? _selectedBuyLine;
    private decimal _oldTotalWeight;
    private decimal _oldStoneWeight;
    private decimal _oldImpurityDeduction;
    private decimal _oldValuePercent = 100m;
    private decimal _oldBuyPrice;
    private string? _oldOverrideReason;
    private string _oldNotes = string.Empty;
    private decimal _oldQuantity = 1m;
    private bool _isReadOnly;
    private string _statusMessage = string.Empty;
    private bool _canChangeSalesperson;

    public BuySellWorkspaceViewModel(Action? onChanged = null)
    {
        _onChanged = onChanged ?? (() => { });
        AddSellLineCommand = new AsyncRelayCommand(AddSellLineAsync, () => !IsReadOnly && !string.IsNullOrWhiteSpace(ProductCodeInput));
        RemoveSellLineCommand = new RelayCommand(RemoveSellLine, () => !IsReadOnly && SelectedSellLine is not null);
        ClearSellLinesCommand = new RelayCommand(ClearSellLines, () => !IsReadOnly && SellLines.Count > 0);
        AddBuyLineCommand = new RelayCommand(AddBuyLine, () => !IsReadOnly && SelectedOldGoldMaterial is not null);
        RemoveBuyLineCommand = new RelayCommand(RemoveBuyLine, () => !IsReadOnly && SelectedBuyLine is not null);
        LookupCustomerCommand = new RelayCommand(() => { });
        PrintStockLabelCommand = new RelayCommand(() => { });
    }

    public Func<string, Task<SalesProductOption?>>? FindProductAsync { get; set; }

    public ObservableCollection<SalesCustomerOption> CustomerOptions { get; } = [];
    public ObservableCollection<SalespersonOption> StaffOptions { get; } = [];
    public ObservableCollection<OldGoldMaterialOption> OldGoldTypeOptions { get; } = [];
    public ObservableCollection<SalesLocationOption> OldGoldLocationOptions { get; } = [];
    public ObservableCollection<SellLineRowViewModel> SellLines { get; } = [];
    public ObservableCollection<BuyOldLineRowViewModel> BuyLines { get; } = [];

    public DateTime DocumentDate
    {
        get => _documentDate;
        set { if (SetProperty(ref _documentDate, value)) NotifyChanged(); }
    }

    public string DocumentCode
    {
        get => _documentCode;
        set => SetProperty(ref _documentCode, value);
    }

    public SalesCustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set { if (SetProperty(ref _selectedCustomer, value)) NotifyChanged(); }
    }

    public SalespersonOption? SelectedStaff
    {
        get => _selectedStaff;
        set { if (SetProperty(ref _selectedStaff, value)) NotifyChanged(); }
    }

    public SalesLocationOption? SelectedOldGoldLocation
    {
        get => _selectedOldGoldLocation;
        set { if (SetProperty(ref _selectedOldGoldLocation, value)) NotifyChanged(); }
    }

    public OldGoldMaterialOption? SelectedOldGoldMaterial
    {
        get => _selectedOldGoldMaterial;
        set
        {
            if (SetProperty(ref _selectedOldGoldMaterial, value) && value is not null)
            {
                OldValuePercent = value.ValuePercent;
                OldBuyPrice = value.SourceBuyPricePerChi;
                RecalculateOldGoldPreview();
            }
        }
    }

    public int ContentTabIndex
    {
        get => _contentTabIndex;
        set => SetProperty(ref _contentTabIndex, value);
    }

    public string ProductCodeInput
    {
        get => _productCodeInput;
        set
        {
            if (SetProperty(ref _productCodeInput, value))
                ((AsyncRelayCommand)AddSellLineCommand).NotifyCanExecuteChanged();
        }
    }

    public SellLineRowViewModel? SelectedSellLine
    {
        get => _selectedSellLine;
        set
        {
            if (SetProperty(ref _selectedSellLine, value))
                ((RelayCommand)RemoveSellLineCommand).NotifyCanExecuteChanged();
        }
    }

    public BuyOldLineRowViewModel? SelectedBuyLine
    {
        get => _selectedBuyLine;
        set
        {
            if (SetProperty(ref _selectedBuyLine, value))
                ((RelayCommand)RemoveBuyLineCommand).NotifyCanExecuteChanged();
        }
    }

    public decimal OldTotalWeight
    {
        get => _oldTotalWeight;
        set { if (SetProperty(ref _oldTotalWeight, value)) RecalculateOldGoldPreview(); }
    }

    public decimal OldStoneWeight
    {
        get => _oldStoneWeight;
        set { if (SetProperty(ref _oldStoneWeight, value)) RecalculateOldGoldPreview(); }
    }

    public decimal OldImpurityDeduction
    {
        get => _oldImpurityDeduction;
        set { if (SetProperty(ref _oldImpurityDeduction, value)) RecalculateOldGoldPreview(); }
    }

    public decimal OldGoldWeight { get; private set; }
    public decimal OldConvertedWeight { get; private set; }

    public decimal OldValuePercent
    {
        get => _oldValuePercent;
        set { if (SetProperty(ref _oldValuePercent, value)) RecalculateOldGoldPreview(); }
    }

    public decimal OldBuyPrice
    {
        get => _oldBuyPrice;
        set { if (SetProperty(ref _oldBuyPrice, value)) RecalculateOldGoldPreview(); }
    }

    public string? OldOverrideReason
    {
        get => _oldOverrideReason;
        set => SetProperty(ref _oldOverrideReason, value);
    }

    public string OldNotes
    {
        get => _oldNotes;
        set => SetProperty(ref _oldNotes, value);
    }

    public decimal OldQuantity
    {
        get => _oldQuantity;
        set => SetProperty(ref _oldQuantity, Math.Max(0.001m, value));
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
            {
                RaiseTotalsChanged();
                NotifyChanged();
            }
        }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value);
    }

    public bool CanChangeSalesperson
    {
        get => _canChangeSalesperson;
        set => SetProperty(ref _canChangeSalesperson, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CheckoutTypeLabel =>
        SellLines.Count > 0 && BuyLines.Count > 0 ? "TRADE_IN"
        : BuyLines.Count > 0 ? "OLD_GOLD_PURCHASE"
        : "SALE";

    public decimal NewGoldTotal => SellLines.Sum(x => x.LineTotal);
    public decimal OldGoldTotal => BuyLines.Sum(x => x.LineTotal);
    public decimal Subtotal => NewGoldTotal - OldGoldTotal;
    public decimal GrandTotal => Subtotal - DiscountAmount;
    public decimal AmountDue => GrandTotal;

    public string NewGoldTotalText => NewGoldTotal.ToString("N0", Vi);
    public string OldGoldTotalText => OldGoldTotal.ToString("N0", Vi);
    public string SubtotalText => Subtotal.ToString("N0", Vi);
    public string GrandTotalText => GrandTotal.ToString("N0", Vi);
    public string AmountDueText => AmountDue.ToString("N0", Vi);
    public string OldGoldWeightText => OldGoldWeight.ToString("0.####", Vi);
    public string OldConvertedWeightText => OldConvertedWeight.ToString("0.####", Vi);

    public ICommand AddSellLineCommand { get; }
    public ICommand RemoveSellLineCommand { get; }
    public ICommand ClearSellLinesCommand { get; }
    public ICommand AddBuyLineCommand { get; }
    public ICommand RemoveBuyLineCommand { get; }
    public ICommand PrintStockLabelCommand { get; }
    public ICommand LookupCustomerCommand { get; }

    public void ResetForNew(SalesCustomerOption? customer, SalespersonOption? staff)
    {
        DocumentDate = DateTime.Today;
        DocumentCode = string.Empty;
        SelectedCustomer = customer;
        SelectedStaff = staff;
        DiscountAmount = 0;
        SellLines.Clear();
        BuyLines.Clear();
        ProductCodeInput = string.Empty;
        IsReadOnly = false;
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    public SalesCheckoutTotals ComputeTotals()
    {
        var saleAmounts = SellLines.Select(x =>
            SalesCheckoutCalculator.CalculateSaleLine(x.ToDraft(), x.ToPricing())).ToArray();
        var oldGoldAmounts = BuyLines.Select(x =>
            SalesCheckoutCalculator.CalculateOldGold(x.ToDraft())).ToArray();
        var draft = BuildDraftSkeleton(Guid.Empty, [], 0);
        return SalesCheckoutCalculator.CalculateTotals(draft, saleAmounts, oldGoldAmounts);
    }

    public SalesCheckoutDraft BuildDraft(
        Guid clientRequestId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal receivableAmountVnd,
        long sourceLocationId,
        string? notes) =>
        BuildDraftSkeleton(clientRequestId, allocations, receivableAmountVnd) with
        {
            SourceLocationId = sourceLocationId,
            Notes = notes
        };

    public void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(NewGoldTotal));
        OnPropertyChanged(nameof(OldGoldTotal));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(AmountDue));
        OnPropertyChanged(nameof(NewGoldTotalText));
        OnPropertyChanged(nameof(OldGoldTotalText));
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(AmountDueText));
        OnPropertyChanged(nameof(CheckoutTypeLabel));
        ((RelayCommand)ClearSellLinesCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddBuyLineCommand).NotifyCanExecuteChanged();
    }

    private SalesCheckoutDraft BuildDraftSkeleton(
        Guid clientRequestId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal receivableAmountVnd) =>
        new(
            clientRequestId,
            DocumentDate,
            SelectedCustomer?.Id ?? 0,
            SelectedStaff?.Id ?? 0,
            0,
            BuyLines.Count > 0 ? SelectedOldGoldLocation?.Id : null,
            DiscountAmount,
            SellLines.Select(x => x.ToDraft()).ToArray(),
            BuyLines.Select(x => x.ToDraft()).ToArray(),
            allocations,
            receivableAmountVnd,
            null);

    private async Task AddSellLineAsync()
    {
        if (FindProductAsync is null || string.IsNullOrWhiteSpace(ProductCodeInput))
            return;
        var product = await FindProductAsync(ProductCodeInput.Trim());
        if (product is null)
        {
            StatusMessage = "Không tìm thấy thành phẩm có thể bán.";
            return;
        }
        if (SellLines.Any(x => x.FinishedProductId == product.FinishedProductId))
        {
            StatusMessage = "Thành phẩm đã có trên phiếu.";
            return;
        }

        SellLines.Add(new SellLineRowViewModel(
            SellLines.Count + 1, product,
            product.SourceGoldPricePerChi, product.SourceLaborAmountVnd,
            product.SourceStoneAmountVnd, null));
        ProductCodeInput = string.Empty;
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    private void RemoveSellLine()
    {
        if (SelectedSellLine is null) return;
        SellLines.Remove(SelectedSellLine);
        RenumberSell();
        SelectedSellLine = null;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    private void ClearSellLines()
    {
        SellLines.Clear();
        RaiseTotalsChanged();
        NotifyChanged();
    }

    private void AddBuyLine()
    {
        if (SelectedOldGoldMaterial is null) return;
        try
        {
            var row = new BuyOldLineRowViewModel(
                BuyLines.Count + 1,
                SelectedOldGoldMaterial.Id,
                SelectedOldGoldMaterial.Name,
                OldQuantity,
                OldTotalWeight,
                OldStoneWeight,
                OldImpurityDeduction,
                OldValuePercent,
                SelectedOldGoldMaterial.SourceBuyPricePerChi,
                OldBuyPrice,
                OldOverrideReason,
                string.IsNullOrWhiteSpace(OldNotes) ? null : OldNotes);
            BuyLines.Add(row);
            StatusMessage = string.Empty;
            RaiseTotalsChanged();
            NotifyChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void RemoveBuyLine()
    {
        if (SelectedBuyLine is null) return;
        BuyLines.Remove(SelectedBuyLine);
        RenumberBuy();
        SelectedBuyLine = null;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    private void RecalculateOldGoldPreview()
    {
        var draft = new OldGoldLineDraft(
            SelectedOldGoldMaterial?.Id ?? 0, OldQuantity, OldTotalWeight, OldStoneWeight,
            OldImpurityDeduction, OldValuePercent, OldBuyPrice, OldOverrideReason, null);
        try
        {
            var amounts = SalesCheckoutCalculator.CalculateOldGold(draft);
            OldGoldWeight = amounts.GoldWeightChi;
            OldConvertedWeight = amounts.ConvertedWeightChi;
        }
        catch
        {
            OldGoldWeight = 0;
            OldConvertedWeight = 0;
        }
        OnPropertyChanged(nameof(OldGoldWeight));
        OnPropertyChanged(nameof(OldConvertedWeight));
        OnPropertyChanged(nameof(OldGoldWeightText));
        OnPropertyChanged(nameof(OldConvertedWeightText));
    }

    private void RenumberSell()
    {
        for (var i = 0; i < SellLines.Count; i++)
            SellLines[i].SetIndex(i + 1);
    }

    private void RenumberBuy()
    {
        for (var i = 0; i < BuyLines.Count; i++)
            BuyLines[i].SetIndex(i + 1);
    }

    private void NotifyChanged() => _onChanged();
}
