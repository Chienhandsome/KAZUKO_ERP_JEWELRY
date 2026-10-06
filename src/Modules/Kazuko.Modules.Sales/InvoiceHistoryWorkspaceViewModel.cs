using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales;

public sealed class InvoiceHistoryWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly Func<AuthenticatedSession> _requireSession;
    private readonly ISalesQueryRepository _queries;
    private readonly Action _onChanged;

    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private DateTime _toDate = DateTime.Today;
    private string _searchText = string.Empty;
    private SalesCheckoutListItem? _selectedItem;
    private SalesCheckoutDetail? _selectedDetail;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _canReadHistory;
    private bool _canPrint;
    private int _searchVersion;
    private int _detailVersion;

    public InvoiceHistoryWorkspaceViewModel(
        ISalesQueryRepository queries,
        Func<AuthenticatedSession> requireSession,
        Action? onChanged = null)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _requireSession = requireSession ?? throw new ArgumentNullException(nameof(requireSession));
        _onChanged = onChanged ?? (() => { });

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading && CanReadHistory);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsLoading && CanPrint && SelectedDetail is not null);
    }

    public ObservableCollection<SalesCheckoutListItem> Items { get; } = [];

    public DateTime FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value.Date);
    }

    public DateTime ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value.Date);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public SalesCheckoutListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
                return;
            _ = LoadDetailAsync(value?.CheckoutId);
            _onChanged();
        }
    }

    public SalesCheckoutDetail? SelectedDetail
    {
        get => _selectedDetail;
        private set
        {
            if (SetProperty(ref _selectedDetail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailCustomerText));
                OnPropertyChanged(nameof(DetailStaffText));
                OnPropertyChanged(nameof(DetailTypeText));
                OnPropertyChanged(nameof(DetailDateText));
                OnPropertyChanged(nameof(DetailNotes));
                OnPropertyChanged(nameof(SaleGrossText));
                OnPropertyChanged(nameof(DiscountText));
                OnPropertyChanged(nameof(OldGoldTotalText));
                OnPropertyChanged(nameof(OffsetText));
                OnPropertyChanged(nameof(NetAmountText));
                OnPropertyChanged(nameof(ReceivableText));
                ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
                _onChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool CanReadHistory
    {
        get => _canReadHistory;
        set
        {
            if (SetProperty(ref _canReadHistory, value))
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
        }
    }

    public bool CanPrint
    {
        get => _canPrint;
        set
        {
            if (SetProperty(ref _canPrint, value))
                ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
        }
    }

    public bool HasDetail => SelectedDetail is not null;
    public string DetailTitle => SelectedDetail is null
        ? "Chọn một hóa đơn để xem chi tiết"
        : SelectedDetail.DocumentCode;
    public string DetailCustomerText => SelectedDetail is null
        ? string.Empty
        : SelectedDetail.CustomerName
          + (string.IsNullOrWhiteSpace(SelectedDetail.CustomerPhone)
              ? string.Empty
              : $" ({SelectedDetail.CustomerPhone})");
    public string DetailStaffText => SelectedDetail?.SalespersonName ?? string.Empty;
    public string DetailTypeText => SelectedDetail is null
        ? string.Empty
        : CheckoutTypeLabel(SelectedDetail.CheckoutType);
    public string DetailDateText => SelectedDetail?.DocumentDate.ToString("dd/MM/yyyy", Vi) ?? string.Empty;
    public string DetailNotes => SelectedDetail?.Notes ?? string.Empty;
    public string SaleGrossText => FormatMoney(SelectedDetail?.Totals.SaleGrossVnd);
    public string DiscountText => FormatMoney(SelectedDetail?.Totals.DiscountAmountVnd);
    public string OldGoldTotalText => FormatMoney(SelectedDetail?.Totals.OldGoldTotalVnd);
    public string OffsetText => FormatMoney(SelectedDetail?.Totals.OffsetAmountVnd);
    public string NetAmountText => FormatMoney(SelectedDetail?.Totals.NetAmountVnd);
    public string ReceivableText => FormatMoney(SelectedDetail?.Totals.ReceivableAmountVnd);

    public ICommand SearchCommand { get; }
    public ICommand PrintCommand { get; }

    public Func<long, Task>? PrintCheckoutAsync { get; set; }

    public Task EnsureLoadedAsync() => SearchAsync();

    public async Task SearchAsync()
    {
        if (!CanReadHistory)
        {
            StatusMessage = "Không có quyền xem lịch sử hóa đơn.";
            Items.Clear();
            SelectedDetail = null;
            return;
        }

        var version = ++_searchVersion;
        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;
            var session = _requireSession();
            var rows = await _queries.SearchPostedCheckoutsAsync(
                new SalesCheckoutSearchFilter(FromDate, ToDate, SearchText, 100),
                session);

            if (version != _searchVersion)
                return;

            var keepId = SelectedItem?.CheckoutId;
            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            SelectedItem = keepId is > 0
                ? Items.FirstOrDefault(x => x.CheckoutId == keepId) ?? Items.FirstOrDefault()
                : Items.FirstOrDefault();

            StatusMessage = Items.Count == 0
                ? "Không có hóa đơn trong khoảng đã chọn."
                : $"Có {Items.Count} hóa đơn.";
        }
        catch (Exception ex)
        {
            if (version == _searchVersion)
                StatusMessage = ex.Message;
        }
        finally
        {
            if (version == _searchVersion)
                IsLoading = false;
        }
    }

    private async Task LoadDetailAsync(long? checkoutId)
    {
        if (checkoutId is null or <= 0)
        {
            SelectedDetail = null;
            return;
        }

        var version = ++_detailVersion;
        try
        {
            IsLoading = true;
            var session = _requireSession();
            var detail = await _queries.GetCheckoutAsync(checkoutId.Value, session);
            if (version != _detailVersion)
                return;
            SelectedDetail = detail;
            if (detail is null)
                StatusMessage = "Không tải được chi tiết hóa đơn.";
        }
        catch (Exception ex)
        {
            if (version == _detailVersion)
            {
                SelectedDetail = null;
                StatusMessage = ex.Message;
            }
        }
        finally
        {
            if (version == _detailVersion)
                IsLoading = false;
        }
    }

    private async Task PrintAsync()
    {
        if (SelectedDetail is null || PrintCheckoutAsync is null)
            return;
        await PrintCheckoutAsync(SelectedDetail.CheckoutId);
    }

    public static string CheckoutTypeLabel(string checkoutType) => checkoutType switch
    {
        SalesCheckoutTypes.Sale => "Bán hàng",
        SalesCheckoutTypes.TradeIn => "Thu đổi",
        SalesCheckoutTypes.OldGoldPurchase => "Mua vàng cũ",
        _ => checkoutType
    };

    public static string FormatListDate(DateTime value) => value.ToString("dd/MM/yyyy", Vi);
    public static string FormatListMoney(decimal value) => value.ToString("N0", Vi);

    private static string FormatMoney(decimal? value) =>
        value is null ? string.Empty : value.Value.ToString("N0", Vi);
}
