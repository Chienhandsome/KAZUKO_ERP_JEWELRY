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
    private decimal _oldImpurityDeduction;
    private decimal _oldValuePercent = 100m;
    private decimal _oldBuyPrice;
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
        LookupCustomerCommand = new RelayCommand(() => LookupCustomerAction?.Invoke());
    }

    public Action? LookupCustomerAction { get; set; }

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
                OldValuePercent = value.ValuePercent;
                OldBuyPrice = value.SourceBuyPricePerChi;
                RecalculatePreview();
                ((RelayCommand)AddBuyLineCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public decimal OldTotalWeight
    {
        get => _oldTotalWeight;
        set { if (SetProperty(ref _oldTotalWeight, value)) RecalculatePreview(); }
    }

    public decimal OldStoneWeight
    {
        get => _oldStoneWeight;
        set { if (SetProperty(ref _oldStoneWeight, value)) RecalculatePreview(); }
    }

    public decimal OldImpurityDeduction
    {
        get => _oldImpurityDeduction;
        set { if (SetProperty(ref _oldImpurityDeduction, value)) RecalculatePreview(); }
    }

    public decimal OldGoldWeight { get; private set; }
    public decimal OldConvertedWeight { get; private set; }

    public decimal OldValuePercent
    {
        get => _oldValuePercent;
        set { if (SetProperty(ref _oldValuePercent, value)) RecalculatePreview(); }
    }

    public decimal OldBuyPrice
    {
        get => _oldBuyPrice;
        set { if (SetProperty(ref _oldBuyPrice, value)) RecalculatePreview(); }
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
    public ICommand LookupCustomerCommand { get; }

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
                OldQuantity,
                OldTotalWeight,
                OldStoneWeight,
                OldImpurityDeduction,
                OldValuePercent,
                SelectedOldGoldMaterial.SourceBuyPricePerChi,
                OldBuyPrice,
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

    private void NotifyChanged() => _onChanged();
}
