using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales;

public sealed class BuyOldGoldWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private readonly Action _onChanged;

    private DateTime _documentDate = DateTime.Today;
    private string _documentCode = string.Empty;
    private SalesCustomerOption? _selectedCustomer;
    private SalespersonOption? _selectedStaff;
    private SalesLocationOption? _selectedOldGoldLocation;
    private OldGoldMaterialOption? _selectedOldGoldMaterial;
    private decimal _oldTotalWeight;
    private decimal _oldStoneWeight;
    private decimal _oldValuePercent = 100m;
    private decimal _oldBuyPrice;
    private string _oldTotalWeightText = string.Empty;
    private string _oldStoneWeightText = string.Empty;
    private string _oldValuePercentText = "100";
    private string _oldBuyPriceText = string.Empty;
    private string _oldQuantityText = "1";
    private string? _oldOverrideReason;
    private string _oldNotes = string.Empty;
    private decimal _oldQuantity = 1m;
    private BuyOldLineRowViewModel? _selectedBuyLine;
    private bool _isReadOnly;
    private string _statusMessage = string.Empty;
    private bool _canChangeSalesperson;

    public BuyOldGoldWorkspaceViewModel(Action? onChanged = null)
    {
        _onChanged = onChanged ?? (() => { });
        AddBuyLineCommand = new RelayCommand(AddBuyLine, () => !IsReadOnly && SelectedOldGoldMaterial is not null);
        RemoveBuyLineCommand = new RelayCommand(RemoveBuyLine, () => !IsReadOnly && SelectedBuyLine is not null);
        PrintStockLabelCommand = new RelayCommand(() => { });
        AddCustomerCommand = new RelayCommand(() => AddCustomerAction?.Invoke());
    }

    public Action? AddCustomerAction { get; set; }

    public ObservableCollection<SalesCustomerOption> CustomerOptions { get; } = [];
    public ObservableCollection<SalespersonOption> StaffOptions { get; } = [];
    public ObservableCollection<OldGoldMaterialOption> OldGoldTypeOptions { get; } = [];
    public ObservableCollection<SalesLocationOption> OldGoldLocationOptions { get; } = [];
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
                SetOldValuePercent(100m);
                SetOldBuyPrice(value.SourceBuyPricePerChi);
                RecalculatePreview();
                ((RelayCommand)AddBuyLineCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string OldTotalWeightText
    {
        get => _oldTotalWeightText;
        set
        {
            if (!SetProperty(ref _oldTotalWeightText, value))
                return;
            if (SalesDecimalText.TryParse(value, out var parsed))
            {
                _oldTotalWeight = Math.Max(0m, parsed);
                RecalculatePreview();
            }
        }
    }

    public string OldStoneWeightText
    {
        get => _oldStoneWeightText;
        set
        {
            if (!SetProperty(ref _oldStoneWeightText, value))
                return;
            if (SalesDecimalText.TryParse(value, out var parsed))
            {
                _oldStoneWeight = Math.Max(0m, parsed);
                RecalculatePreview();
            }
        }
    }

    public decimal OldGoldWeight { get; private set; }
    public decimal OldConvertedWeight { get; private set; }

    public string OldValuePercentText
    {
        get => _oldValuePercentText;
        set
        {
            if (!SetProperty(ref _oldValuePercentText, value))
                return;
            if (SalesDecimalText.TryParse(value, out var parsed))
            {
                _oldValuePercent = Math.Clamp(parsed, 0m, 100m);
                RecalculatePreview();
            }
        }
    }

    public string OldBuyPriceText
    {
        get => _oldBuyPriceText;
        set
        {
            if (!SetProperty(ref _oldBuyPriceText, value))
                return;
            if (SalesDecimalText.TryParseGrouped(value, out var parsed))
            {
                _oldBuyPrice = Math.Max(0m, parsed);
                RecalculatePreview();
            }
        }
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

    public string OldQuantityText
    {
        get => _oldQuantityText;
        set
        {
            if (!SetProperty(ref _oldQuantityText, value))
                return;
            if (SalesDecimalText.TryParseGrouped(value, out var parsed) && parsed > 0m)
                _oldQuantity = parsed;
        }
    }

    private void SetOldValuePercent(decimal value)
    {
        _oldValuePercent = value;
        _oldValuePercentText = SalesDecimalText.Format(value, "0.##");
        OnPropertyChanged(nameof(OldValuePercentText));
    }

    private void SetOldBuyPrice(decimal value)
    {
        _oldBuyPrice = value;
        _oldBuyPriceText = SalesDecimalText.FormatGrouped(value);
        OnPropertyChanged(nameof(OldBuyPriceText));
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

    public string CheckoutTypeLabel => "OLD_GOLD_PURCHASE";
    public decimal OldGoldTotal => BuyLines.Sum(x => x.LineTotal);
    public string OldGoldTotalText => OldGoldTotal.ToString("N0", Vi);
    public string AmountPayableText => OldGoldTotal.ToString("N0", Vi);
    public string OldGoldWeightText => OldGoldWeight.ToString("0.####", Vi);
    public string OldConvertedWeightText => OldConvertedWeight.ToString("0.####", Vi);

    public ICommand AddBuyLineCommand { get; }
    public ICommand RemoveBuyLineCommand { get; }
    public ICommand PrintStockLabelCommand { get; }
    public ICommand AddCustomerCommand { get; }

    public void ResetForNew(SalesCustomerOption? customer, SalespersonOption? staff)
    {
        DocumentDate = DateTime.Today;
        DocumentCode = string.Empty;
        SelectedCustomer = customer;
        SelectedStaff = staff;
        BuyLines.Clear();
        IsReadOnly = false;
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    public SalesCheckoutTotals ComputeTotals()
    {
        var amounts = BuyLines.Select(x => SalesCheckoutCalculator.CalculateOldGold(x.ToDraft())).ToArray();
        var draft = new SalesCheckoutDraft(
            Guid.Empty, DocumentDate, SelectedCustomer?.Id ?? 0, SelectedStaff?.Id ?? 0,
            0, SelectedOldGoldLocation?.Id, 0, [], BuyLines.Select(x => x.ToDraft()).ToArray(),
            [], 0, null);
        return SalesCheckoutCalculator.CalculateTotals(draft, [], amounts);
    }

    public SalesCheckoutDraft BuildDraft(
        Guid clientRequestId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes) =>
        new(
            clientRequestId,
            DocumentDate,
            SelectedCustomer?.Id ?? 0,
            SelectedStaff?.Id ?? 0,
            0,
            SelectedOldGoldLocation?.Id,
            0,
            [],
            BuyLines.Select(x => x.ToDraft()).ToArray(),
            allocations,
            0,
            notes);

    public void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(OldGoldTotal));
        OnPropertyChanged(nameof(OldGoldTotalText));
        OnPropertyChanged(nameof(AmountPayableText));
    }

    private void AddBuyLine()
    {
        if (SelectedOldGoldMaterial is null) return;
        try
        {
            BuyLines.Add(new BuyOldLineRowViewModel(
                BuyLines.Count + 1,
                SelectedOldGoldMaterial.Id,
                SelectedOldGoldMaterial.Name,
                _oldQuantity,
                _oldTotalWeight,
                _oldStoneWeight,
                _oldValuePercent,
                SelectedOldGoldMaterial.SourceBuyPricePerChi,
                _oldBuyPrice,
                OldOverrideReason,
                string.IsNullOrWhiteSpace(OldNotes) ? null : OldNotes));
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
        for (var i = 0; i < BuyLines.Count; i++)
            BuyLines[i].SetIndex(i + 1);
        SelectedBuyLine = null;
        RaiseTotalsChanged();
        NotifyChanged();
    }

    private void RecalculatePreview()
    {
        var draft = new OldGoldLineDraft(
            SelectedOldGoldMaterial?.Id ?? 0, _oldQuantity, _oldTotalWeight, _oldStoneWeight,
            0m, _oldValuePercent, _oldBuyPrice, OldOverrideReason, null);
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

    private void NotifyChanged() => _onChanged();
}
