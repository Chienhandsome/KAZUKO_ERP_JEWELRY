using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales;

public sealed class WholesaleWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private readonly Action _onChanged;
    private CancellationTokenSource? _productSearchCts;
    private int _productSearchVersion;
    private bool _suppressProductSearch;

    private DateTime _documentDate = DateTime.Today;
    private string _documentCode = string.Empty;
    private SalesCustomerOption? _selectedCustomer;
    private SalespersonOption? _selectedStaff;
    private WholesaleLocationOption? _selectedLocation;
    private string _productCodeInput = string.Empty;
    private WholesaleProductSuggestionItem? _selectedProductSuggestion;
    private bool _isProductSuggestionOpen;
    private WholesaleLineRowViewModel? _selectedLine;
    private bool _isReadOnly;
    private string _statusMessage = string.Empty;
    private bool _canChangeSalesperson;
    private bool _canPostWholesale;

    public WholesaleWorkspaceViewModel(Action? onChanged = null)
    {
        _onChanged = onChanged ?? (() => { });
        AddLineCommand = new AsyncRelayCommand(AddLineAsync, () => !IsReadOnly && !string.IsNullOrWhiteSpace(ProductCodeInput));
        RemoveLineCommand = new RelayCommand(RemoveLine, () => !IsReadOnly && SelectedLine is not null);
        ClearLinesCommand = new RelayCommand(ClearLines, () => !IsReadOnly && Lines.Count > 0);
        AddCustomerCommand = new RelayCommand(() => AddCustomerAction?.Invoke());
    }

    public Func<string, long?, Task<WholesaleProductOption?>>? FindProductAsync { get; set; }
    public Func<string, long?, CancellationToken, Task<IReadOnlyList<WholesaleProductOption>>>? SearchProductsAsync { get; set; }
    public Action? AddCustomerAction { get; set; }

    public ObservableCollection<SalesCustomerOption> CustomerOptions { get; } = [];
    public ObservableCollection<SalespersonOption> StaffOptions { get; } = [];
    public ObservableCollection<WholesaleLocationOption> LocationOptions { get; } = [];
    public ObservableCollection<WholesaleLineRowViewModel> Lines { get; } = [];
    public ObservableCollection<WholesaleProductSuggestionItem> ProductSuggestions { get; } = [];

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

    public WholesaleLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (!SetProperty(ref _selectedLocation, value))
                return;
            if (Lines.Count > 0)
            {
                StatusMessage = "Đổi kho sẽ xóa giỏ hàng hiện tại.";
                Lines.Clear();
                SelectedLine = null;
                Renumber();
                RaiseTotalsChanged();
            }
            NotifyChanged();
        }
    }

    public string ProductCodeInput
    {
        get => _productCodeInput;
        set
        {
            if (!SetProperty(ref _productCodeInput, value))
                return;
            ((AsyncRelayCommand)AddLineCommand).NotifyCanExecuteChanged();
            if (!_suppressProductSearch)
                _ = RefreshProductSuggestionsAsync(value);
        }
    }

    public WholesaleProductSuggestionItem? SelectedProductSuggestion
    {
        get => _selectedProductSuggestion;
        set => SetProperty(ref _selectedProductSuggestion, value);
    }

    public bool IsProductSuggestionOpen
    {
        get => _isProductSuggestionOpen;
        set => SetProperty(ref _isProductSuggestionOpen, value);
    }

    public WholesaleLineRowViewModel? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
                ((RelayCommand)RemoveLineCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (SetProperty(ref _isReadOnly, value))
            {
                ((AsyncRelayCommand)AddLineCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RemoveLineCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearLinesCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanChangeSalesperson
    {
        get => _canChangeSalesperson;
        set => SetProperty(ref _canChangeSalesperson, value);
    }

    public bool CanPostWholesale
    {
        get => _canPostWholesale;
        set => SetProperty(ref _canPostWholesale, value);
    }

    public decimal GrandTotal => Lines.Sum(x => x.LineTotal);
    public decimal TotalGoldWeight => Lines.Sum(x => x.GoldWeight);
    public string GrandTotalText => GrandTotal.ToString("N0", Vi);
    public string TotalGoldWeightText => TotalGoldWeight.ToString("0.####", Vi);
    public string AmountDueText => GrandTotal.ToString("N0", Vi);

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ClearLinesCommand { get; }
    public ICommand AddCustomerCommand { get; }

    public void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(TotalGoldWeight));
        OnPropertyChanged(nameof(TotalGoldWeightText));
        OnPropertyChanged(nameof(AmountDueText));
        ((RelayCommand)ClearLinesCommand).NotifyCanExecuteChanged();
        NotifyChanged();
    }

    public void ResetForNew(SalesCustomerOption? customer, SalespersonOption? staff)
    {
        DocumentDate = DateTime.Today;
        DocumentCode = string.Empty;
        SelectedCustomer = customer;
        SelectedStaff = staff;
        Lines.Clear();
        SelectedLine = null;
        ProductCodeInput = string.Empty;
        ProductSuggestions.Clear();
        IsProductSuggestionOpen = false;
        StatusMessage = string.Empty;
        IsReadOnly = false;
        RaiseTotalsChanged();
    }

    public WholesaleSaleDraft BuildDraft(
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal receivableAmountVnd,
        string? notes)
    {
        if (SelectedCustomer is null)
            throw new InvalidOperationException("Phải chọn khách hàng.");
        if (SelectedStaff is null)
            throw new InvalidOperationException("Phải chọn nhân viên.");
        if (SelectedLocation is null)
            throw new InvalidOperationException("Phải chọn kho xuất.");
        if (Lines.Count == 0)
            throw new InvalidOperationException("Phải chọn ít nhất một thành phẩm.");

        return new WholesaleSaleDraft(
            DocumentDate,
            SelectedCustomer.Id,
            SelectedStaff.Id,
            SelectedLocation.Id,
            Lines.Select(x => x.FinishedProductId).ToList(),
            allocations,
            receivableAmountVnd,
            notes);
    }

    public bool TryAcceptProductFromEnter()
    {
        if (SelectedProductSuggestion is not null && IsProductSuggestionOpen)
        {
            AcceptHighlightedSuggestion();
            return true;
        }

        return false;
    }

    public void AcceptHighlightedSuggestion()
    {
        if (SelectedProductSuggestion is null)
            return;
        _suppressProductSearch = true;
        ProductCodeInput = SelectedProductSuggestion.ProductCode;
        _suppressProductSearch = false;
        IsProductSuggestionOpen = false;
        _ = AddLineAsync();
    }

    private async Task AddLineAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (FindProductAsync is null)
                throw new InvalidOperationException("Chưa gắn dịch vụ tìm hàng bán sỉ.");
            if (SelectedLocation is null)
                throw new InvalidOperationException("Chọn kho xuất trước khi thêm hàng.");

            var code = ProductCodeInput.Trim();
            var product = await FindProductAsync(code, SelectedLocation.Id);
            if (product is null)
            {
                StatusMessage = $"Không tìm thấy hàng '{code}' trong kho đã chọn.";
                return;
            }

            if (Lines.Any(x => x.FinishedProductId == product.FinishedProductId))
            {
                StatusMessage = $"Serial '{product.ProductCode}' đã có trong giỏ.";
                return;
            }

            Lines.Add(new WholesaleLineRowViewModel(Lines.Count + 1, product));
            SelectedLine = Lines[^1];
            _suppressProductSearch = true;
            ProductCodeInput = string.Empty;
            _suppressProductSearch = false;
            ProductSuggestions.Clear();
            IsProductSuggestionOpen = false;
            RaiseTotalsChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void RemoveLine()
    {
        if (SelectedLine is null)
            return;
        Lines.Remove(SelectedLine);
        SelectedLine = null;
        Renumber();
        RaiseTotalsChanged();
    }

    private void ClearLines()
    {
        Lines.Clear();
        SelectedLine = null;
        RaiseTotalsChanged();
    }

    private void Renumber()
    {
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].SetIndex(i + 1);
    }

    private async Task RefreshProductSuggestionsAsync(string input)
    {
        var version = ++_productSearchVersion;
        _productSearchCts?.Cancel();
        _productSearchCts = new CancellationTokenSource();
        var token = _productSearchCts.Token;
        var search = (input ?? string.Empty).Trim();
        if (search.Length < 1 || SearchProductsAsync is null || SelectedLocation is null)
        {
            ProductSuggestions.Clear();
            IsProductSuggestionOpen = false;
            return;
        }

        try
        {
            await Task.Delay(180, token);
            var matches = await SearchProductsAsync(search, SelectedLocation.Id, token);
            if (version != _productSearchVersion || token.IsCancellationRequested)
                return;

            ProductSuggestions.Clear();
            foreach (var match in matches)
            {
                if (Lines.Any(x => x.FinishedProductId == match.FinishedProductId))
                    continue;
                ProductSuggestions.Add(new WholesaleProductSuggestionItem(match));
            }

            SelectedProductSuggestion = ProductSuggestions.FirstOrDefault();
            IsProductSuggestionOpen = ProductSuggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void NotifyChanged() => _onChanged();
}

public sealed class WholesaleLineRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public WholesaleLineRowViewModel(int index, WholesaleProductOption product)
    {
        Index = index;
        FinishedProductId = product.FinishedProductId;
        ProductCode = product.ProductCode;
        ProductName = product.ProductName;
        GoldType = product.GoldPurity;
        GoldWeight = product.GoldWeightChi;
        StoneWeight = product.StoneWeightChi;
        SellPricePerChi = product.SellPricePerChi;
        LaborCost = product.LaborCost;
        NonGoldMaterialCost = product.NonGoldMaterialCost;
        LineTotal = product.LineTotalVnd;
        LocationName = product.LocationName;
    }

    public int Index { get; private set; }
    public long FinishedProductId { get; }
    public string ProductCode { get; }
    public string ProductName { get; }
    public string GoldType { get; }
    public decimal GoldWeight { get; }
    public decimal StoneWeight { get; }
    public decimal SellPricePerChi { get; }
    public decimal LaborCost { get; }
    public decimal NonGoldMaterialCost { get; }
    public decimal LineTotal { get; }
    public string LocationName { get; }

    public string GoldWeightText => GoldWeight.ToString("0.####", Vi);
    public string StoneWeightText => StoneWeight.ToString("0.####", Vi);
    public string SellPriceText => SellPricePerChi.ToString("N0", Vi);
    public string LaborFeeText => LaborCost.ToString("N0", Vi);
    public string StoneAmountText => NonGoldMaterialCost.ToString("N0", Vi);
    public string LineTotalText => LineTotal.ToString("N0", Vi);

    public void SetIndex(int index)
    {
        Index = index;
        OnPropertyChanged(nameof(Index));
    }
}

public sealed class WholesaleProductSuggestionItem
{
    public WholesaleProductSuggestionItem(WholesaleProductOption product)
    {
        Product = product;
        ProductCode = product.ProductCode;
        ProductName = product.ProductName;
        WeightText = product.GoldWeightChi.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN"));
    }

    public WholesaleProductOption Product { get; }
    public string ProductCode { get; }
    public string ProductName { get; }
    public string WeightText { get; }
}
