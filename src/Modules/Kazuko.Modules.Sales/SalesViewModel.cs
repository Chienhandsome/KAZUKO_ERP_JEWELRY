using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Sales;

public sealed class SalesViewModel : ObservableObject
{
    private int _mainTabIndex;

    public SalesViewModel()
    {
        BuySell = new BuySellWorkspaceViewModel();
        BuyOldGold = new BuyOldGoldWorkspaceViewModel();

        NewCommand = new RelayCommand(() => { });
        PreviousCommand = new RelayCommand(() => { });
        NextCommand = new RelayCommand(() => { });
        PayCommand = new RelayCommand(() => { });
        LookupCustomerCommand = new RelayCommand(() => { });
    }

    public BuySellWorkspaceViewModel BuySell { get; }
    public BuyOldGoldWorkspaceViewModel BuyOldGold { get; }

    public int MainTabIndex
    {
        get => _mainTabIndex;
        set => SetProperty(ref _mainTabIndex, value);
    }

    public ICommand NewCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand LookupCustomerCommand { get; }
}

public sealed class BuySellWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private DateTime _documentDate = DateTime.Today;
    private string _documentCode = "MB26/00001";
    private string? _selectedCustomer;
    private string? _selectedStaff;
    private int _contentTabIndex;
    private string _productCodeInput = string.Empty;
    private decimal _discountAmount;
    private SellLineRowViewModel? _selectedSellLine;
    private BuyOldLineRowViewModel? _selectedBuyLine;

    private string? _oldGoldType;
    private decimal _oldTotalWeight;
    private decimal _oldStoneWeight;
    private decimal _oldImpurityDeduction;
    private decimal _oldGoldWeight;
    private decimal _oldValuePercent = 100m;
    private decimal _oldConvertedWeight;
    private decimal _oldBuyPrice;
    private string _oldProductCode = string.Empty;
    private decimal _oldQuantity = 1m;

    private string _einvoiceTaxCode = string.Empty;
    private string _einvoiceBuyerName = string.Empty;
    private string _einvoiceAddress = string.Empty;
    private string _einvoiceEmail = string.Empty;
    private string _einvoicePhone = string.Empty;
    private string _einvoiceInvoiceForm = "Hóa đơn GTGT";
    private string _einvoiceNotes = string.Empty;

    public BuySellWorkspaceViewModel()
    {
        CustomerOptions.Add("Khách lẻ");
        CustomerOptions.Add("Nguyễn Văn A");
        CustomerOptions.Add("Công ty TNHH Vàng Bạc XYZ");
        _selectedCustomer = CustomerOptions[0];

        StaffOptions.Add("NV01 — Trần Thị B");
        StaffOptions.Add("NV02 — Lê Văn C");
        _selectedStaff = StaffOptions[0];

        OldGoldTypeOptions.Add("Vàng 9999");
        OldGoldTypeOptions.Add("Vàng 24K");
        OldGoldTypeOptions.Add("Vàng 18K");
        OldGoldTypeOptions.Add("Vàng 14K");
        _oldGoldType = OldGoldTypeOptions[0];

        InvoiceFormOptions.Add("Hóa đơn GTGT");
        InvoiceFormOptions.Add("Hóa đơn bán hàng");
        InvoiceFormOptions.Add("Không xuất HĐ");

        SellLines.Add(new SellLineRowViewModel(
            1, "SP001", "Nhẫn kim cương", "9999", 1.2m, 0.05m, 350_000m, 0m, 1.15m,
            7_500_000m, 200_000m, "Kho bán hàng"));
        SellLines.Add(new SellLineRowViewModel(
            2, "SP014", "Dây chuyền", "9999", 2.5m, 0m, 450_000m, 0m, 2.5m,
            7_450_000m, 0m, "Kho bán hàng"));

        BuyLines.Add(new BuyOldLineRowViewModel(
            1, "Vàng 18K", 3.2m, 0.1m, 3.1m, 2.325m, 75m, 7_200_000m));

        AddSellLineCommand = new RelayCommand(() => { });
        RemoveSellLineCommand = new RelayCommand(() => { });
        ClearSellLinesCommand = new RelayCommand(() => { });
        AddBuyLineCommand = new RelayCommand(() => { });
        RemoveBuyLineCommand = new RelayCommand(() => { });
        PrintStockLabelCommand = new RelayCommand(() => { });
        LookupCustomerCommand = new RelayCommand(() => { });
    }

    public ObservableCollection<string> CustomerOptions { get; } = [];
    public ObservableCollection<string> StaffOptions { get; } = [];
    public ObservableCollection<string> OldGoldTypeOptions { get; } = [];
    public ObservableCollection<string> InvoiceFormOptions { get; } = [];
    public ObservableCollection<SellLineRowViewModel> SellLines { get; } = [];
    public ObservableCollection<BuyOldLineRowViewModel> BuyLines { get; } = [];

    public DateTime DocumentDate
    {
        get => _documentDate;
        set => SetProperty(ref _documentDate, value);
    }

    public string DocumentCode
    {
        get => _documentCode;
        set => SetProperty(ref _documentCode, value);
    }

    public string? SelectedCustomer
    {
        get => _selectedCustomer;
        set => SetProperty(ref _selectedCustomer, value);
    }

    public string? SelectedStaff
    {
        get => _selectedStaff;
        set => SetProperty(ref _selectedStaff, value);
    }

    public int ContentTabIndex
    {
        get => _contentTabIndex;
        set => SetProperty(ref _contentTabIndex, value);
    }

    public string ProductCodeInput
    {
        get => _productCodeInput;
        set => SetProperty(ref _productCodeInput, value);
    }

    public SellLineRowViewModel? SelectedSellLine
    {
        get => _selectedSellLine;
        set => SetProperty(ref _selectedSellLine, value);
    }

    public BuyOldLineRowViewModel? SelectedBuyLine
    {
        get => _selectedBuyLine;
        set => SetProperty(ref _selectedBuyLine, value);
    }

    public string? OldGoldType
    {
        get => _oldGoldType;
        set => SetProperty(ref _oldGoldType, value);
    }

    public decimal OldTotalWeight
    {
        get => _oldTotalWeight;
        set => SetProperty(ref _oldTotalWeight, value);
    }

    public decimal OldStoneWeight
    {
        get => _oldStoneWeight;
        set => SetProperty(ref _oldStoneWeight, value);
    }

    public decimal OldImpurityDeduction
    {
        get => _oldImpurityDeduction;
        set => SetProperty(ref _oldImpurityDeduction, value);
    }

    public decimal OldGoldWeight
    {
        get => _oldGoldWeight;
        set => SetProperty(ref _oldGoldWeight, value);
    }

    public decimal OldValuePercent
    {
        get => _oldValuePercent;
        set => SetProperty(ref _oldValuePercent, value);
    }

    public decimal OldConvertedWeight
    {
        get => _oldConvertedWeight;
        set => SetProperty(ref _oldConvertedWeight, value);
    }

    public decimal OldBuyPrice
    {
        get => _oldBuyPrice;
        set => SetProperty(ref _oldBuyPrice, value);
    }

    public string OldProductCode
    {
        get => _oldProductCode;
        set => SetProperty(ref _oldProductCode, value);
    }

    public decimal OldQuantity
    {
        get => _oldQuantity;
        set => SetProperty(ref _oldQuantity, value);
    }

    public string EinvoiceTaxCode
    {
        get => _einvoiceTaxCode;
        set => SetProperty(ref _einvoiceTaxCode, value);
    }

    public string EinvoiceBuyerName
    {
        get => _einvoiceBuyerName;
        set => SetProperty(ref _einvoiceBuyerName, value);
    }

    public string EinvoiceAddress
    {
        get => _einvoiceAddress;
        set => SetProperty(ref _einvoiceAddress, value);
    }

    public string EinvoiceEmail
    {
        get => _einvoiceEmail;
        set => SetProperty(ref _einvoiceEmail, value);
    }

    public string EinvoicePhone
    {
        get => _einvoicePhone;
        set => SetProperty(ref _einvoicePhone, value);
    }

    public string EinvoiceInvoiceForm
    {
        get => _einvoiceInvoiceForm;
        set => SetProperty(ref _einvoiceInvoiceForm, value);
    }

    public string EinvoiceNotes
    {
        get => _einvoiceNotes;
        set => SetProperty(ref _einvoiceNotes, value);
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
                RaiseTotalsChanged();
        }
    }

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

    public ICommand AddSellLineCommand { get; }
    public ICommand RemoveSellLineCommand { get; }
    public ICommand ClearSellLinesCommand { get; }
    public ICommand AddBuyLineCommand { get; }
    public ICommand RemoveBuyLineCommand { get; }
    public ICommand PrintStockLabelCommand { get; }
    public ICommand LookupCustomerCommand { get; }

    private void RaiseTotalsChanged()
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
    }
}

public sealed class BuyOldGoldWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private DateTime _documentDate = DateTime.Today;
    private string _documentCode = "MVC26/00001";
    private string? _selectedCustomer;
    private string? _selectedStaff;
    private string? _oldGoldType;
    private decimal _oldTotalWeight;
    private decimal _oldStoneWeight;
    private decimal _oldImpurityDeduction;
    private decimal _oldGoldWeight;
    private decimal _oldValuePercent = 100m;
    private decimal _oldConvertedWeight;
    private decimal _oldBuyPrice;
    private string _oldProductCode = string.Empty;
    private decimal _oldQuantity = 1m;
    private BuyOldLineRowViewModel? _selectedBuyLine;

    public BuyOldGoldWorkspaceViewModel()
    {
        CustomerOptions.Add("Khách lẻ");
        CustomerOptions.Add("Nguyễn Văn A");
        _selectedCustomer = CustomerOptions[0];

        StaffOptions.Add("NV01 — Trần Thị B");
        StaffOptions.Add("NV02 — Lê Văn C");
        _selectedStaff = StaffOptions[0];

        OldGoldTypeOptions.Add("Vàng 9999");
        OldGoldTypeOptions.Add("Vàng 24K");
        OldGoldTypeOptions.Add("Vàng 18K");
        OldGoldTypeOptions.Add("Vàng 14K");
        _oldGoldType = OldGoldTypeOptions[0];

        BuyLines.Add(new BuyOldLineRowViewModel(
            1, "Vàng 9999", 5.0m, 0m, 5.0m, 5.0m, 100m, 7_800_000m));

        AddBuyLineCommand = new RelayCommand(() => { });
        RemoveBuyLineCommand = new RelayCommand(() => { });
        PrintStockLabelCommand = new RelayCommand(() => { });
        LookupCustomerCommand = new RelayCommand(() => { });
    }

    public ObservableCollection<string> CustomerOptions { get; } = [];
    public ObservableCollection<string> StaffOptions { get; } = [];
    public ObservableCollection<string> OldGoldTypeOptions { get; } = [];
    public ObservableCollection<BuyOldLineRowViewModel> BuyLines { get; } = [];

    public DateTime DocumentDate
    {
        get => _documentDate;
        set => SetProperty(ref _documentDate, value);
    }

    public string DocumentCode
    {
        get => _documentCode;
        set => SetProperty(ref _documentCode, value);
    }

    public string? SelectedCustomer
    {
        get => _selectedCustomer;
        set => SetProperty(ref _selectedCustomer, value);
    }

    public string? SelectedStaff
    {
        get => _selectedStaff;
        set => SetProperty(ref _selectedStaff, value);
    }

    public string? OldGoldType
    {
        get => _oldGoldType;
        set => SetProperty(ref _oldGoldType, value);
    }

    public decimal OldTotalWeight
    {
        get => _oldTotalWeight;
        set => SetProperty(ref _oldTotalWeight, value);
    }

    public decimal OldStoneWeight
    {
        get => _oldStoneWeight;
        set => SetProperty(ref _oldStoneWeight, value);
    }

    public decimal OldImpurityDeduction
    {
        get => _oldImpurityDeduction;
        set => SetProperty(ref _oldImpurityDeduction, value);
    }

    public decimal OldGoldWeight
    {
        get => _oldGoldWeight;
        set => SetProperty(ref _oldGoldWeight, value);
    }

    public decimal OldValuePercent
    {
        get => _oldValuePercent;
        set => SetProperty(ref _oldValuePercent, value);
    }

    public decimal OldConvertedWeight
    {
        get => _oldConvertedWeight;
        set => SetProperty(ref _oldConvertedWeight, value);
    }

    public decimal OldBuyPrice
    {
        get => _oldBuyPrice;
        set => SetProperty(ref _oldBuyPrice, value);
    }

    public string OldProductCode
    {
        get => _oldProductCode;
        set => SetProperty(ref _oldProductCode, value);
    }

    public decimal OldQuantity
    {
        get => _oldQuantity;
        set => SetProperty(ref _oldQuantity, value);
    }

    public BuyOldLineRowViewModel? SelectedBuyLine
    {
        get => _selectedBuyLine;
        set => SetProperty(ref _selectedBuyLine, value);
    }

    public decimal OldGoldTotal => BuyLines.Sum(x => x.LineTotal);
    public string OldGoldTotalText => OldGoldTotal.ToString("N0", Vi);
    public string AmountPayableText => OldGoldTotal.ToString("N0", Vi);

    public ICommand AddBuyLineCommand { get; }
    public ICommand RemoveBuyLineCommand { get; }
    public ICommand PrintStockLabelCommand { get; }
    public ICommand LookupCustomerCommand { get; }
}

public sealed class SellLineRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SellLineRowViewModel(
        int index,
        string productCode,
        string productName,
        string goldType,
        decimal goldWeight,
        decimal stoneWeight,
        decimal laborFee,
        decimal sizeCut,
        decimal actualGold,
        decimal sellPricePerChi,
        decimal stoneAmount,
        string warehouse)
    {
        Index = index;
        ProductCode = productCode;
        ProductName = productName;
        GoldType = goldType;
        GoldWeight = goldWeight;
        StoneWeight = stoneWeight;
        LaborFee = laborFee;
        SizeCut = sizeCut;
        ActualGold = actualGold;
        SellPricePerChi = sellPricePerChi;
        StoneAmount = stoneAmount;
        Warehouse = warehouse;
        LineTotal = Math.Round(actualGold * sellPricePerChi + laborFee + stoneAmount, 0, MidpointRounding.AwayFromZero);
    }

    public int Index { get; }
    public string ProductCode { get; }
    public string ProductName { get; }
    public string GoldType { get; }
    public decimal GoldWeight { get; }
    public decimal StoneWeight { get; }
    public decimal LaborFee { get; }
    public decimal SizeCut { get; }
    public decimal ActualGold { get; }
    public decimal SellPricePerChi { get; }
    public decimal StoneAmount { get; }
    public decimal LineTotal { get; }
    public string Warehouse { get; }

    public string GoldWeightText => GoldWeight.ToString("0.####", Vi);
    public string StoneWeightText => StoneWeight.ToString("0.####", Vi);
    public string LaborFeeText => LaborFee.ToString("N0", Vi);
    public string SizeCutText => SizeCut.ToString("0.####", Vi);
    public string ActualGoldText => ActualGold.ToString("0.####", Vi);
    public string SellPriceText => SellPricePerChi.ToString("N0", Vi);
    public string StoneAmountText => StoneAmount.ToString("N0", Vi);
    public string LineTotalText => LineTotal.ToString("N0", Vi);
}

public sealed class BuyOldLineRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public BuyOldLineRowViewModel(
        int index,
        string oldGoldType,
        decimal totalWeight,
        decimal stoneWeight,
        decimal goldWeight,
        decimal convertedWeight,
        decimal valuePercent,
        decimal buyPricePerChi)
    {
        Index = index;
        OldGoldType = oldGoldType;
        TotalWeight = totalWeight;
        StoneWeight = stoneWeight;
        GoldWeight = goldWeight;
        ConvertedWeight = convertedWeight;
        ValuePercent = valuePercent;
        BuyPricePerChi = buyPricePerChi;
        LineTotal = Math.Round(convertedWeight * buyPricePerChi, 0, MidpointRounding.AwayFromZero);
    }

    public int Index { get; }
    public string OldGoldType { get; }
    public decimal TotalWeight { get; }
    public decimal StoneWeight { get; }
    public decimal GoldWeight { get; }
    public decimal ConvertedWeight { get; }
    public decimal ValuePercent { get; }
    public decimal BuyPricePerChi { get; }
    public decimal LineTotal { get; }

    public string TotalWeightText => TotalWeight.ToString("0.####", Vi);
    public string StoneWeightText => StoneWeight.ToString("0.####", Vi);
    public string GoldWeightText => GoldWeight.ToString("0.####", Vi);
    public string ConvertedWeightText => ConvertedWeight.ToString("0.####", Vi);
    public string ValuePercentText => ValuePercent.ToString("0.##", Vi);
    public string BuyPriceText => BuyPricePerChi.ToString("N0", Vi);
    public string LineTotalText => LineTotal.ToString("N0", Vi);
}
