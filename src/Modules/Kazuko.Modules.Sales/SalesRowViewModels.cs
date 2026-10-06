using System.Globalization;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales;

public sealed class SellLineRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private decimal _appliedGoldPricePerChi;
    private decimal _appliedLaborAmountVnd;
    private decimal _appliedStoneAmountVnd;
    private string? _overrideReason;

    public SellLineRowViewModel(
        int index,
        SalesProductOption product,
        decimal appliedGold,
        decimal appliedLabor,
        decimal appliedStone,
        string? overrideReason)
    {
        Index = index;
        FinishedProductId = product.FinishedProductId;
        ProductCode = product.ProductCode;
        ProductName = product.ProductName;
        GoldType = product.GoldPurity;
        GoldWeight = product.GoldWeightChi;
        StoneWeight = product.StoneWeightChi;
        SourceGoldPricePerChi = product.SourceGoldPricePerChi;
        SourceLaborAmountVnd = product.SourceLaborAmountVnd;
        SourceStoneAmountVnd = product.SourceStoneAmountVnd;
        Warehouse = product.RetailGoldPriceName;
        _appliedGoldPricePerChi = appliedGold;
        _appliedLaborAmountVnd = appliedLabor;
        _appliedStoneAmountVnd = appliedStone;
        _overrideReason = overrideReason;
        Recalculate();
    }

    public int Index { get; private set; }
    public long FinishedProductId { get; }
    public string ProductCode { get; }
    public string ProductName { get; }
    public string GoldType { get; }
    public decimal GoldWeight { get; }
    public decimal StoneWeight { get; }
    public decimal SourceGoldPricePerChi { get; }
    public decimal SourceLaborAmountVnd { get; }
    public decimal SourceStoneAmountVnd { get; }
    public string Warehouse { get; }

    public decimal AppliedGoldPricePerChi
    {
        get => _appliedGoldPricePerChi;
        set
        {
            if (SetProperty(ref _appliedGoldPricePerChi, value))
                Recalculate();
        }
    }

    public decimal AppliedLaborAmountVnd
    {
        get => _appliedLaborAmountVnd;
        set
        {
            if (SetProperty(ref _appliedLaborAmountVnd, value))
                Recalculate();
        }
    }

    public decimal AppliedStoneAmountVnd
    {
        get => _appliedStoneAmountVnd;
        set
        {
            if (SetProperty(ref _appliedStoneAmountVnd, value))
                Recalculate();
        }
    }

    public string? OverrideReason
    {
        get => _overrideReason;
        set => SetProperty(ref _overrideReason, value);
    }

    public decimal LaborFee => AppliedLaborAmountVnd;
    public decimal SizeCut => 0m;
    public decimal ActualGold => GoldWeight;
    public decimal SellPricePerChi => AppliedGoldPricePerChi;
    public decimal StoneAmount => AppliedStoneAmountVnd;
    public decimal LineTotal { get; private set; }
    public bool IsPriceOverride { get; private set; }

    public string GoldWeightText => GoldWeight.ToString("0.####", Vi);
    public string StoneWeightText => StoneWeight.ToString("0.####", Vi);
    public string LaborFeeText => LaborFee.ToString("N0", Vi);
    public string SizeCutText => SizeCut.ToString("0.####", Vi);
    public string ActualGoldText => ActualGold.ToString("0.####", Vi);
    public string SellPriceText => SellPricePerChi.ToString("N0", Vi);
    public string StoneAmountText => StoneAmount.ToString("N0", Vi);
    public string LineTotalText => LineTotal.ToString("N0", Vi);

    public SalesLineDraft ToDraft() =>
        new(FinishedProductId, AppliedGoldPricePerChi, AppliedLaborAmountVnd, AppliedStoneAmountVnd, OverrideReason);

    public SalesLinePricing ToPricing() =>
        new(FinishedProductId, GoldWeight, SourceGoldPricePerChi, SourceLaborAmountVnd, SourceStoneAmountVnd);

    public void SetIndex(int index) => Index = index;

    private void Recalculate()
    {
        var amounts = SalesCheckoutCalculator.CalculateSaleLine(ToDraft(), ToPricing());
        LineTotal = amounts.TotalAmountVnd;
        IsPriceOverride = amounts.IsPriceOverride;
        OnPropertyChanged(nameof(LaborFee));
        OnPropertyChanged(nameof(SellPricePerChi));
        OnPropertyChanged(nameof(StoneAmount));
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(IsPriceOverride));
        OnPropertyChanged(nameof(LaborFeeText));
        OnPropertyChanged(nameof(SellPriceText));
        OnPropertyChanged(nameof(StoneAmountText));
        OnPropertyChanged(nameof(LineTotalText));
    }
}

public sealed class BuyOldLineRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public BuyOldLineRowViewModel(
        int index,
        long materialId,
        string oldGoldType,
        decimal itemQuantity,
        decimal totalWeight,
        decimal stoneWeight,
        decimal impurityWeight,
        decimal valuePercent,
        decimal sourceBuyPricePerChi,
        decimal appliedBuyPricePerChi,
        string? overrideReason,
        string? notes)
    {
        Index = index;
        MaterialId = materialId;
        OldGoldType = oldGoldType;
        ItemQuantity = itemQuantity;
        TotalWeight = totalWeight;
        StoneWeight = stoneWeight;
        ImpurityWeight = impurityWeight;
        ValuePercent = valuePercent;
        SourceBuyPricePerChi = sourceBuyPricePerChi;
        AppliedBuyPricePerChi = appliedBuyPricePerChi;
        OverrideReason = overrideReason;
        Notes = notes;
        var amounts = SalesCheckoutCalculator.CalculateOldGold(ToDraft());
        GoldWeight = amounts.GoldWeightChi;
        ConvertedWeight = amounts.ConvertedWeightChi;
        LineTotal = amounts.AmountVnd;
        IsPriceOverride = appliedBuyPricePerChi != sourceBuyPricePerChi;
    }

    public int Index { get; private set; }
    public long MaterialId { get; }
    public string OldGoldType { get; }
    public decimal ItemQuantity { get; }
    public decimal TotalWeight { get; }
    public decimal StoneWeight { get; }
    public decimal ImpurityWeight { get; }
    public decimal GoldWeight { get; }
    public decimal ConvertedWeight { get; }
    public decimal ValuePercent { get; }
    public decimal SourceBuyPricePerChi { get; }
    public decimal BuyPricePerChi => AppliedBuyPricePerChi;
    public decimal AppliedBuyPricePerChi { get; }
    public string? OverrideReason { get; }
    public string? Notes { get; }
    public decimal LineTotal { get; }
    public bool IsPriceOverride { get; }

    public string TotalWeightText => TotalWeight.ToString("0.####", Vi);
    public string StoneWeightText => StoneWeight.ToString("0.####", Vi);
    public string GoldWeightText => GoldWeight.ToString("0.####", Vi);
    public string ConvertedWeightText => ConvertedWeight.ToString("0.####", Vi);
    public string ValuePercentText => ValuePercent.ToString("0.##", Vi);
    public string BuyPriceText => BuyPricePerChi.ToString("N0", Vi);
    public string LineTotalText => LineTotal.ToString("N0", Vi);

    public OldGoldLineDraft ToDraft() =>
        new(MaterialId, ItemQuantity, TotalWeight, StoneWeight, ImpurityWeight,
            ValuePercent, AppliedBuyPricePerChi, OverrideReason, Notes);

    public void SetIndex(int index) => Index = index;
}
