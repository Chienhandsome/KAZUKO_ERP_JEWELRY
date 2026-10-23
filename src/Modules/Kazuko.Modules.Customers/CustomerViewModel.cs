using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Shared.UI.Services;

namespace Kazuko.Modules.Customers;

public sealed class CustomerViewModel : ObservableObject
{
    public static readonly string[] RegionOptions =
    [
        "TP. Hồ Chí Minh",
        "Hà Nội",
        "Đà Nẵng",
        "Cần Thơ",
        "Hải Phòng",
        "Bình Dương",
        "Đồng Nai",
        "Khác"
    ];

    private const int PurchaseHistoryPreviewLimit = 5;
    private const int PurchaseHistoryTabIndex = 1;
    private const int DebtTabIndex = 2;
    private const int ReportsTabIndex = 3;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ICustomerRepository _repository;
    private readonly ICustomerEditorService _editor;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IDebtWorkbenchRepository? _debtWorkbench;
    private readonly IDebtSettlementDialogService? _settlementDialog;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private string _searchText = string.Empty;
    private CustomerGroupOption? _selectedGroup;
    private string? _selectedRegion;
    private string _loadedKind = "Có hồ sơ";
    private DateTime _summaryFromDate;
    private DateTime _summaryToDate;
    private string _selectedKind = "Có hồ sơ";
    private string _selectedStatus = "Tất cả";
    private CustomerRowViewModel? _selectedCustomer;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty;
    private int _selectedTabIndex;
    private CustomerDebtSnapshot _debtSnapshot = new(0, 0, 0, 0);
    private DateTime _reportFromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _reportToDate = DateTime.Today;
    private int _reportTopN = 10;
    private int _reportInactiveDays = 30;
    private bool _isReportLoading;
    private bool _isDetailLoading;

    public CustomerViewModel(
        ICustomerRepository repository,
        ICustomerEditorService editor,
        ICurrentSessionAccessor sessionAccessor,
        IDebtWorkbenchRepository? debtWorkbench = null,
        IDebtSettlementDialogService? settlementDialog = null)
    {
        _repository = repository;
        _editor = editor;
        _sessionAccessor = sessionAccessor;
        _debtWorkbench = debtWorkbench;
        _settlementDialog = settlementDialog;

        Groups.Add(new CustomerGroupOption(0, string.Empty, "Tất cả nhóm", true, false));
        Regions.Add("Tất cả khu vực");
        foreach (var region in RegionOptions)
            Regions.Add(region);
        StatusOptions.Add("Tất cả");
        StatusOptions.Add("Đang hoạt động");
        StatusOptions.Add("Ngừng giao dịch");

        SelectedGroup = Groups[0];
        SelectedRegion = Regions[0];

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        ExportExcelCommand = new RelayCommand<object?>(ExportExcel, _ => !IsLoading);
        AddCommand = new RelayCommand(AddCustomer, () => !IsLoading);
        EditCommand = new ParameterRelayCommand(
            EditCustomer,
            parameter => !IsLoading && (parameter as CustomerRowViewModel ?? SelectedCustomer) is { IsWalkIn: false });
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsLoading && SelectedCustomer is { IsWalkIn: false });
        DeleteCommand = new ParameterRelayCommand(
            DeleteCustomer,
            parameter => !IsLoading && ResolveActiveRow(parameter) is not null);
        DeleteSelectedCommand = new RelayCommand(
            () => DeleteCustomer(SelectedCustomer),
            () => !IsLoading && SelectedCustomer is { IsActive: true, IsWalkIn: false });
        ToggleActiveCommand = new ParameterRelayCommand(
            ToggleActive,
            parameter => !IsLoading && (parameter as CustomerRowViewModel ?? SelectedCustomer) is { IsWalkIn: false });
        ViewAllPurchasesCommand = new RelayCommand(
            () => OpenActivityTab(false),
            () => HasSelection);
        ViewDebtDetailsCommand = new RelayCommand(
            () => OpenActivityTab(true),
            () => HasSelection);
        CollectPaymentCommand = new AsyncRelayCommand(
            CollectPaymentAsync,
            () => !IsLoading && DebtTab is { IsDetailLoading: false, HasCurrentDebt: true, SelectedCustomer.IsWalkIn: false }
                && _debtWorkbench is not null && _settlementDialog is not null
                && _sessionAccessor.Current is not null);
        HistoryTab = new(repository, sessionAccessor, debtWorkbench, false);
        DebtTab = new(repository, sessionAccessor, debtWorkbench, true, CollectPaymentCommand);
        DebtTab.PropertyChanged += (_, _) => ((AsyncRelayCommand)CollectPaymentCommand).NotifyCanExecuteChanged();
        LoadTopCustomersCommand = new AsyncRelayCommand(LoadTopCustomersAsync, () => !IsReportLoading);
        LoadInactiveCustomersCommand = new AsyncRelayCommand(LoadInactiveCustomersAsync, () => !IsReportLoading);
        LoadDistributionCommand = new AsyncRelayCommand(LoadDistributionAsync, () => !IsReportLoading);
        ExportSalesSummaryCommand = new RelayCommand<object?>(ExportSalesSummary, _ => !IsReportLoading && SalesSummary.Count > 0);
        ExportTopCustomersCommand = new RelayCommand<object?>(ExportTopCustomers, _ => !IsReportLoading && TopCustomers.Count > 0);
        ExportInactiveCustomersCommand = new RelayCommand<object?>(ExportInactiveCustomers, _ => !IsReportLoading && InactiveCustomers.Count > 0);
        ExportDistributionCommand = new RelayCommand<object?>(ExportDistribution, _ => !IsReportLoading && GroupDistribution.Count > 0);
        ViewPlaceholderCommand = new RelayCommand(() => { }, () => false);
        ImportExcelCommand = new RelayCommand(() => { }, () => false);

        _ = InitializeAsync();
    }

    public CustomerActivityTabViewModel HistoryTab { get; }
    public CustomerActivityTabViewModel DebtTab { get; }
    private void OpenActivityTab(bool debt)
    {
        if (SelectedCustomer is null) return;
        var tab = debt ? DebtTab : HistoryTab;
        tab.SelectedCustomer = ActivityOption(tab, SelectedCustomer);
        SelectedTabIndex = debt ? DebtTabIndex : PurchaseHistoryTabIndex;
    }
    private static CustomerActivityOption ActivityOption(CustomerActivityTabViewModel tab, CustomerRowViewModel row) =>
        tab.CustomerOptions.FirstOrDefault(x => x.Id == row.Id) ?? new(row.Id, row.Code, row.Name, row.Phone, row.IsWalkIn);

    public ObservableCollection<CustomerRowViewModel> Customers { get; } = new();
    public ObservableCollection<CustomerGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<PurchaseHistoryRowViewModel> RecentPurchases { get; } = new();
    public ObservableCollection<CustomerSalesSummaryRow> SalesSummary { get; } = new();
    public string SalesSummaryText => SalesSummary.Count == 0 ? string.Empty : $"Kỳ {_summaryFromDate:dd/MM/yyyy}–{_summaryToDate:dd/MM/yyyy} · Tổng cộng: {SalesSummary.Sum(x => x.OrderCount).ToString("N0", Vi)} đơn · {SalesSummary.Sum(x => x.TotalSales).ToString("N0", Vi)} VND";
    public ObservableCollection<TopCustomerRowViewModel> TopCustomers { get; } = new();
    public ObservableCollection<InactiveCustomerRowViewModel> InactiveCustomers { get; } = new();
    public ObservableCollection<DistributionRowViewModel> GroupDistribution { get; } = new();
    public ObservableCollection<DistributionRowViewModel> RegionDistribution { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public CustomerGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string? SelectedRegion
    {
        get => _selectedRegion;
        set => SetProperty(ref _selectedRegion, value);
    }

    public string[] KindOptions { get; } = ["Có hồ sơ", "Vãng lai", "Tất cả"];
    public string SelectedKind
    {
        get => _selectedKind;
        set => SetProperty(ref _selectedKind, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public CustomerRowViewModel? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedKindText));
                OnPropertyChanged(nameof(SelectedProtectionHint));
                ((ParameterRelayCommand)EditCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)ToggleActiveCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedCode));
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedPhone));
                OnPropertyChanged(nameof(SelectedEmail));
                OnPropertyChanged(nameof(SelectedAddress));
                OnPropertyChanged(nameof(SelectedNotes));
                OnPropertyChanged(nameof(SelectedGroupName));
                OnPropertyChanged(nameof(SelectedRegionName));
                OnPropertyChanged(nameof(SelectedOccupation));
                OnPropertyChanged(nameof(SelectedMembershipRank));
                OnPropertyChanged(nameof(SelectedTaxCode));
                OnPropertyChanged(nameof(SelectedGender));
                OnPropertyChanged(nameof(SelectedBirthText));
                OnPropertyChanged(nameof(SelectedJoinText));
                OnPropertyChanged(nameof(SelectedPointsText));
                OnPropertyChanged(nameof(SelectedCreditLimitText));
                OnPropertyChanged(nameof(SelectedDebtText));
                OnPropertyChanged(nameof(SelectedIsActive));
                OnPropertyChanged(nameof(SelectedPhotoPath));
                OnPropertyChanged(nameof(HasPhoto));
                OnPropertyChanged(nameof(RecentPurchasesTitle));
                OnPropertyChanged(nameof(HasRecentPurchases));
                OnPropertyChanged(nameof(CanDeleteSelected));
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                ((RelayCommand)DeleteSelectedCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)DeleteCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ViewAllPurchasesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ViewDebtDetailsCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CollectPaymentCommand).NotifyCanExecuteChanged();
                _ = LoadSelectionDetailsAsync();
            }
        }
    }

    public string SelectedProtectionHint => SelectedCustomer?.ProtectionHint ?? string.Empty;
    public string SelectedKindText => SelectedCustomer?.KindText ?? string.Empty;
    public bool HasSelection => SelectedCustomer is not null;
    public bool CanDeleteSelected => !IsLoading && SelectedCustomer is { IsActive: true, IsWalkIn: false };
    public string SelectedCode => SelectedCustomer?.Code ?? string.Empty;
    public string SelectedName => SelectedCustomer?.Name ?? string.Empty;
    public string SelectedPhone => SelectedCustomer?.Phone ?? string.Empty;
    public string SelectedEmail => SelectedCustomer?.Item.Email ?? string.Empty;
    public string SelectedAddress => SelectedCustomer?.Item.Address ?? string.Empty;
    public string SelectedNotes => SelectedCustomer?.Item.Notes ?? string.Empty;
    public string? SelectedPhotoPath => SelectedCustomer?.Item.PhotoPath;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(SelectedPhotoPath);
    public string SelectedGroupName => SelectedCustomer?.CustomerGroupName ?? string.Empty;
    public string SelectedRegionName => SelectedCustomer?.Region ?? string.Empty;
    public string SelectedOccupation => SelectedCustomer?.Item.Occupation ?? string.Empty;
    public string SelectedMembershipRank => SelectedCustomer?.Item.MembershipRank ?? string.Empty;
    public string SelectedTaxCode => SelectedCustomer?.Item.TaxCode ?? string.Empty;
    public string SelectedGender => SelectedCustomer?.Item.Gender ?? string.Empty;
    public string SelectedBirthText => SelectedCustomer?.Item.BirthOrEstablishedDate?.ToString("dd/MM/yyyy") ?? string.Empty;
    public string SelectedJoinText => SelectedCustomer?.Item.JoinDate?.ToString("dd/MM/yyyy") ?? string.Empty;
    public string SelectedPointsText => SelectedCustomer?.Item.OpeningPoints.ToString("N0") ?? string.Empty;
    public string SelectedCreditLimitText => SelectedCustomer?.Item.CreditLimit.ToString("N0") ?? "0";
    public string SelectedDebtText => SelectedCustomer?.CurrentDebtText ?? "0";
    public bool SelectedIsActive => SelectedCustomer?.IsActive ?? false;

    public string RecentPurchasesTitle =>
        HasSelection
            ? $"Lịch sử mua hàng ({RecentPurchases.Count} giao dịch gần nhất)"
            : "Lịch sử mua hàng";

    public bool HasRecentPurchases => RecentPurchases.Count > 0;

    public string DebtIncurredText => _debtSnapshot.TotalIncurred.ToString("N0", Vi);
    public string DebtPaidText => _debtSnapshot.Paid.ToString("N0", Vi);
    public string DebtCurrentText => _debtSnapshot.CurrentDebt.ToString("N0", Vi);
    public string DebtOverdueText => _debtSnapshot.Overdue.ToString("N0", Vi);
    public bool HasCurrentDebt => _debtSnapshot.CurrentDebt > 0;
    public bool HasOverdueDebt => _debtSnapshot.Overdue > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                CommandManager.InvalidateRequerySuggested();
                ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)EditCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)DeleteCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)ToggleActiveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                ((RelayCommand)DeleteSelectedCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanDeleteSelected));
                OnPropertyChanged(nameof(IsBusyOverlayVisible));
                ((AsyncRelayCommand)CollectPaymentCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public bool IsBusyOverlayVisible => IsLoading;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value) && value == ReportsTabIndex)
                _ = LoadReportsAsync();

        }
    }

    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        private set
        {
            if (SetProperty(ref _isDetailLoading, value))
                ((AsyncRelayCommand)CollectPaymentCommand).NotifyCanExecuteChanged();
        }
    }

    public string ReportErrorMessage { get; private set; } = string.Empty;

    private void SetReportError(string message)
    {
        ReportErrorMessage = message;
        OnPropertyChanged(nameof(ReportErrorMessage));
    }

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportExcelCommand { get; }

    private void ExportExcel(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<CustomerRowViewModel>[]
        {
            new("Index", "STT", o => o.Index, ExcelCellKind.Integer),
            new("Code", "Mã KH", o => o.Code),
            new("Name", "Tên khách hàng", o => o.Name),
            new("Kind", "Loại khách", o => o.KindText),
            new("Phone", "SĐT", o => o.Phone),
            new("CustomerGroup", "Nhóm KH", o => o.CustomerGroupName),
            new("Region", "Khu vực", o => o.Region),
            new("TotalSales", "Tổng mua", o => o.TotalSales, ExcelCellKind.Money),
            new("CurrentDebt", "Công nợ", o => o.CurrentDebt, ExcelCellKind.Money),
            new("LastPurchase", "Mua cuối", o => o.LastPurchaseDate, ExcelCellKind.Date),
            new("Status", "Trạng thái", o => o.StatusText),
        };
        ExcelExportService.Export(
            "Xuất danh sách khách hàng",
            $"DanhSachKhachHang_{_loadedKind}_{DateTime.Now:yyyyMMdd_HHmmss}",
            grid, Customers, columns);
    }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand ViewAllPurchasesCommand { get; }
    public ICommand ViewDebtDetailsCommand { get; }
    public ICommand CollectPaymentCommand { get; }
    public ICommand LoadTopCustomersCommand { get; }
    public ICommand LoadInactiveCustomersCommand { get; }
    public ICommand LoadDistributionCommand { get; }
    public ICommand ExportSalesSummaryCommand { get; }
    public ICommand ExportTopCustomersCommand { get; }
    public ICommand ExportInactiveCustomersCommand { get; }
    public ICommand ExportDistributionCommand { get; }

    public DateTime ReportFromDate
    {
        get => _reportFromDate;
        set => SetProperty(ref _reportFromDate, value);
    }

    public DateTime ReportToDate
    {
        get => _reportToDate;
        set => SetProperty(ref _reportToDate, value);
    }

    public int ReportTopN
    {
        get => _reportTopN;
        set => SetProperty(ref _reportTopN, value < 1 ? 1 : value);
    }

    public int ReportInactiveDays
    {
        get => _reportInactiveDays;
        set => SetProperty(ref _reportInactiveDays, value < 1 ? 1 : value);
    }

    public bool IsReportLoading
    {
        get => _isReportLoading;
        private set
        {
            if (!SetProperty(ref _isReportLoading, value)) return;
            ((AsyncRelayCommand)LoadTopCustomersCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LoadInactiveCustomersCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LoadDistributionCommand).NotifyCanExecuteChanged();
            ((RelayCommand<object?>)ExportTopCustomersCommand).NotifyCanExecuteChanged();
            ((RelayCommand<object?>)ExportSalesSummaryCommand).NotifyCanExecuteChanged();
            ((RelayCommand<object?>)ExportInactiveCustomersCommand).NotifyCanExecuteChanged();
            ((RelayCommand<object?>)ExportDistributionCommand).NotifyCanExecuteChanged();
        }
    }
    public ICommand ViewPlaceholderCommand { get; }
    public ICommand ImportExcelCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadGroupsAsync();
        await SearchAsync();
        await HistoryTab.LoadCustomersAsync();
        await DebtTab.LoadCustomersAsync();
    }

    private async Task LoadGroupsAsync()
    {
        try
        {
            var groups = await _repository.GetGroupsAsync(activeOnly: false);
            var selectedId = SelectedGroup?.Id ?? 0;
            while (Groups.Count > 1)
                Groups.RemoveAt(Groups.Count - 1);
            foreach (var group in groups.Where(g => g.IsActive || g.Id == selectedId))
                Groups.Add(group);
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectedId) ?? Groups[0];
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể tải nhóm khách hàng.";
        }
    }

    private async Task RefreshAsync()
    {
        await LoadGroupsAsync();
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Đang tải danh sách khách hàng...";

        try
        {
            var filter = new CustomerSearchFilter(
                SearchText,
                SelectedGroup?.Id ?? 0,
                SelectedRegion is null || SelectedRegion == "Tất cả khu vực" ? string.Empty : SelectedRegion,
                SelectedStatus switch
                {
                    "Đang hoạt động" => true,
                    "Ngừng giao dịch" => false,
                    _ => null
                }, SelectedKind switch { "Có hồ sơ" => false, "Vãng lai" => true, _ => (bool?)null });

            var rows = await _repository.SearchAsync(filter, token);
            token.ThrowIfCancellationRequested();

            _loadedKind = filter.IsWalkIn switch { false => "Có hồ sơ", true => "Vãng lai", null => "Tất cả" };
            var previousId = SelectedCustomer?.Id;
            Customers.Clear();
            var index = 1;
            foreach (var row in rows)
                Customers.Add(new CustomerRowViewModel(row, index++));

            SelectedCustomer = previousId is long id
                ? Customers.FirstOrDefault(c => c.Id == id) ?? Customers.FirstOrDefault()
                : Customers.FirstOrDefault();

            IsEmpty = Customers.Count == 0;
            StatusMessage = IsEmpty
                ? "Không tìm thấy khách hàng phù hợp."
                : $"Hiển thị {Customers.Count} khách hàng.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception)
        {
            Customers.Clear();
            SelectedCustomer = null;
            IsEmpty = true;
            ErrorMessage = "Không thể tải danh sách khách hàng. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private async Task LoadSelectionDetailsAsync()
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = new CancellationTokenSource();
        var token = _detailCts.Token;

        IsDetailLoading = true;
        ErrorMessage = string.Empty;

        RecentPurchases.Clear();
        SetDebtSnapshot(new CustomerDebtSnapshot(0, 0, 0, 0));
        OnPropertyChanged(nameof(RecentPurchasesTitle));
        OnPropertyChanged(nameof(HasRecentPurchases));

        var customerId = SelectedCustomer?.Id;
        if (customerId is null)
        {
            IsDetailLoading = false;
            return;
        }

        try
        {
            var historyTask = _repository.GetPurchaseHistoryAsync(
                customerId.Value, PurchaseHistoryPreviewLimit, token);
            var debtTask = _repository.GetDebtSnapshotAsync(customerId.Value, token);
            await Task.WhenAll(historyTask, debtTask);
            token.ThrowIfCancellationRequested();

            var history = await historyTask;
            foreach (var item in history.Take(PurchaseHistoryPreviewLimit))
                RecentPurchases.Add(new PurchaseHistoryRowViewModel(item));

            SetDebtSnapshot(await debtTask);
            ((AsyncRelayCommand)CollectPaymentCommand).NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(RecentPurchasesTitle));
            OnPropertyChanged(nameof(HasRecentPurchases));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                ErrorMessage = "Không thể tải lịch sử mua hàng / công nợ của khách hàng.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsDetailLoading = false;
        }
    }


    /// <summary>
    /// Thu tiền nhanh ngay trong tab Công nợ: mở dialog thu tiền chung của module Công nợ
    /// (CashVoucherDialog ở debt mode), xong refresh lại dữ liệu.
    /// </summary>
    private async Task CollectPaymentAsync()
    {
        if (IsLoading || DebtTab.SelectedCustomer is null || _debtWorkbench is null || _settlementDialog is null)
            return;

        var customerId = DebtTab.SelectedCustomer.Id;
        var customerCode = DebtTab.SelectedCustomer.Code;
        var customerName = DebtTab.SelectedCustomer.Name;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            var session = _sessionAccessor.Current;
            if (session is null)
                return;

            var openLines = await _debtWorkbench.GetCustomerOpenReceivablesAsync(customerId, session);
            if (openLines.Count == 0)
            {
                MessageBox.Show("Không còn khoản phải thu nào.", "Thu tiền",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var allocations = openLines.Select(x => new DebtSettlementAllocationLine(
                DebtLedgerSourceTypes.SalesOrder, x.OrderId, x.OrderCode, x.OrderCode,
                x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

            var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
                FinancialVoucherTypes.In,
                $"{customerCode} — {customerName}",
                allocations,
                $"Thu công nợ khách hàng {customerCode}",
                AllowAmountEdit: true));
            if (result is null)
                return;

            await _debtWorkbench.SettleDebtAllocationsAsync(
                new DebtAllocationSettleRequest(
                    result.Allocations
                        .Where(x => x.PaymentAmountVnd > 0)
                        .Select(x => new DebtAllocationSettleLine(x.SourceType, x.SourceId, x.PaymentAmountVnd))
                        .ToList(),
                    result.CashAmountVnd,
                    result.BankTransferAmountVnd,
                    result.CardAmountVnd,
                    result.FundId,
                    result.BankAccountId,
                    string.IsNullOrWhiteSpace(result.Notes) ? null : result.Notes.Trim()),
                session);

            StatusMessage = "Đã thu tiền khách hàng.";
            await RefreshAsync();
            await DebtTab.LoadAsync();
            await HistoryTab.LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Thu tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }


    #region Báo cáo

    private async Task LoadReportsAsync()
    {
        if (IsReportLoading) return;
        var errors = new List<string>();
        await LoadTopCustomersAsync();
        if (!string.IsNullOrEmpty(ReportErrorMessage)) errors.Add(ReportErrorMessage);
        await LoadInactiveCustomersAsync();
        if (!string.IsNullOrEmpty(ReportErrorMessage)) errors.Add(ReportErrorMessage);
        await LoadDistributionAsync();
        if (!string.IsNullOrEmpty(ReportErrorMessage)) errors.Add(ReportErrorMessage);
        SetReportError(string.Join(Environment.NewLine, errors));
    }

    private async Task LoadTopCustomersAsync()
    {
        if (IsReportLoading)
            return;
        try
        {
            IsReportLoading = true;
            SetReportError(string.Empty);
            TopCustomers.Clear();
            SalesSummary.Clear();
            OnPropertyChanged(nameof(SalesSummaryText));
            if (ReportFromDate.Date > ReportToDate.Date)
                throw new InvalidOperationException("Từ ngày phải nhỏ hơn hoặc bằng Đến ngày.");
            var fromDate = ReportFromDate;
            var toDate = ReportToDate;
            var topN = ReportTopN;
            var summary = await _repository.GetSalesSummaryAsync(fromDate, toDate);
            _summaryFromDate = fromDate;
            _summaryToDate = toDate;
            foreach (var kind in new[] { false, true })
            {
                var item = summary.SingleOrDefault(x => x.IsWalkIn == kind);
                SalesSummary.Add(new CustomerSalesSummaryRow(kind ? "Bán vãng lai" : "Khách có hồ sơ", item?.OrderCount ?? 0, item?.TotalSales ?? 0));
            }
            OnPropertyChanged(nameof(SalesSummaryText));
            var items = await _repository.GetTopCustomersAsync(fromDate, toDate, topN);
            var rank = 1;
            foreach (var item in items)
                TopCustomers.Add(new TopCustomerRowViewModel(item, rank++));
            StatusMessage = $"Đã tải {TopCustomers.Count} khách hàng top doanh số.";
            ((RelayCommand<object?>)ExportTopCustomersCommand).NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetReportError(ex.Message);
        }
        finally
        {
            IsReportLoading = false;
        }
    }

    private async Task LoadInactiveCustomersAsync()
    {
        if (IsReportLoading)
            return;
        try
        {
            IsReportLoading = true;
            SetReportError(string.Empty);
            InactiveCustomers.Clear();
            var cutoff = DateTime.Today.AddDays(-ReportInactiveDays);
            var items = await _repository.SearchAsync(
                new CustomerSearchFilter(string.Empty, 0, string.Empty, true));
            foreach (var item in items
                .Where(x => x.LastPurchaseDate is null || x.LastPurchaseDate.Value.Date < cutoff)
                .OrderBy(x => x.LastPurchaseDate))
            {
                var days = item.LastPurchaseDate is null
                    ? -1
                    : (DateTime.Today - item.LastPurchaseDate.Value.Date).Days;
                InactiveCustomers.Add(new InactiveCustomerRowViewModel(item, days));
            }
            StatusMessage = $"Tìm thấy {InactiveCustomers.Count} khách hàng chưa mua lại quá {ReportInactiveDays} ngày.";
            ((RelayCommand<object?>)ExportInactiveCustomersCommand).NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetReportError(ex.Message);
        }
        finally
        {
            IsReportLoading = false;
        }
    }

    private async Task LoadDistributionAsync()
    {
        if (IsReportLoading)
            return;
        try
        {
            IsReportLoading = true;
            SetReportError(string.Empty);
            GroupDistribution.Clear();
            RegionDistribution.Clear();
            var items = await _repository.SearchAsync(
                new CustomerSearchFilter(string.Empty, 0, string.Empty, null));

            foreach (var g in items
                .GroupBy(x => string.IsNullOrWhiteSpace(x.CustomerGroupName) ? "(Chưa phân nhóm)" : x.CustomerGroupName)
                .OrderByDescending(g => g.Sum(x => x.TotalSales)))
            {
                GroupDistribution.Add(new DistributionRowViewModel(
                    g.Key, g.Count(), g.Sum(x => x.TotalSales)));
            }

            foreach (var g in items
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Region) ? "(Chưa rõ khu vực)" : x.Region)
                .OrderByDescending(g => g.Sum(x => x.TotalSales)))
            {
                RegionDistribution.Add(new DistributionRowViewModel(
                    g.Key, g.Count(), g.Sum(x => x.TotalSales)));
            }

            StatusMessage = $"Đã tổng hợp phân bố {GroupDistribution.Count} nhóm / {RegionDistribution.Count} khu vực.";
            ((RelayCommand<object?>)ExportDistributionCommand).NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetReportError(ex.Message);
        }
        finally
        {
            IsReportLoading = false;
        }
    }

    private void ExportSalesSummary(object? parameter) => ExcelExportService.Export(
        "Xuất tổng hợp doanh thu theo loại khách",
        $"DoanhThuLoaiKhach_{_summaryFromDate:yyyyMMdd}_{_summaryToDate:yyyyMMdd}",
        parameter as System.Windows.Controls.DataGrid, SalesSummary,
        new ExcelExportColumn<CustomerSalesSummaryRow>[]
        {
            new("Kind", "Loại khách", x => x.Kind),
            new("OrderCount", "Số đơn", x => x.OrderCount, ExcelCellKind.Integer),
            new("TotalSales", "Doanh thu", x => x.TotalSales, ExcelCellKind.Money)
        });

    private void ExportTopCustomers(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<TopCustomerRowViewModel>[]
        {
            new("Rank", "Hạng", o => o.Rank, ExcelCellKind.Integer),
            new("Code", "Mã KH", o => o.Code),
            new("Name", "Tên khách hàng", o => o.Name),
            new("Phone", "SĐT", o => o.Phone),
            new("Group", "Nhóm KH", o => o.CustomerGroupName),
            new("OrderCount", "Số đơn", o => o.OrderCount, ExcelCellKind.Integer),
            new("TotalSales", "Tổng mua (VND)", o => o.TotalSales, ExcelCellKind.Money),
        };
        ExcelExportService.Export(
            "Xuất top khách hàng",
            $"TopKhachHang_{_summaryFromDate:yyyyMMdd}_{_summaryToDate:yyyyMMdd}",
            grid, TopCustomers, columns);
    }

    private void ExportInactiveCustomers(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<InactiveCustomerRowViewModel>[]
        {
            new("Code", "Mã KH", o => o.Code),
            new("Name", "Tên khách hàng", o => o.Name),
            new("Phone", "SĐT", o => o.Phone),
            new("Group", "Nhóm KH", o => o.CustomerGroupName),
            new("LastPurchase", "Mua cuối", o => o.Item.LastPurchaseDate, ExcelCellKind.Date),
            new("DaysInactive", "Số ngày chưa mua", o => o.DaysSinceLastPurchase < 0 ? null : o.DaysSinceLastPurchase, ExcelCellKind.Integer),
            new("TotalSales", "Tổng mua (VND)", o => o.Item.TotalSales, ExcelCellKind.Money),
        };
        ExcelExportService.Export(
            "Xuất khách hàng chưa mua lại",
            $"KhachHangChuaMuaLai_{DateTime.Now:yyyyMMdd}",
            grid, InactiveCustomers, columns);
    }

    private void ExportDistribution(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<DistributionRowViewModel>[]
        {
            new("Kind", "Phân bố", o => GroupDistribution.Contains(o) ? "Nhóm khách hàng" : "Khu vực"),
            new("Name", "Tên", o => o.Name),
            new("Count", "Số KH", o => o.CustomerCount, ExcelCellKind.Integer),
            new("TotalSales", "Tổng mua (VND)", o => o.TotalSales, ExcelCellKind.Money),
        };
        ExcelExportService.Export(
            "Xuất phân bố khách hàng",
            $"PhanBoKhachHang_{DateTime.Now:yyyyMMdd}",
            grid, GroupDistribution.Concat(RegionDistribution).ToList(), columns);
    }

    #endregion

    private void SetDebtSnapshot(CustomerDebtSnapshot snapshot)
    {
        _debtSnapshot = snapshot;
        OnPropertyChanged(nameof(DebtIncurredText));
        OnPropertyChanged(nameof(DebtPaidText));
        OnPropertyChanged(nameof(DebtCurrentText));
        OnPropertyChanged(nameof(DebtOverdueText));
        OnPropertyChanged(nameof(HasCurrentDebt));
        OnPropertyChanged(nameof(HasOverdueDebt));
    }

    private void AddCustomer()
    {
        var result = _editor.ShowCreate();
        if (result is null)
            return;
        _ = RefreshAsync();
    }

    private void EditSelected()
    {
        if (SelectedCustomer is null)
            return;
        EditCustomer(SelectedCustomer);
    }

    private void EditCustomer(object? parameter)
    {
        var row = parameter as CustomerRowViewModel ?? SelectedCustomer;
        if (row is null || row.IsWalkIn)
            return;

        SelectedCustomer = row;
        var result = _editor.ShowEdit(row.Id);
        if (result is null)
            return;
        _ = RefreshAsync();
    }

    private CustomerRowViewModel? ResolveActiveRow(object? parameter)
    {
        var row = parameter as CustomerRowViewModel ?? SelectedCustomer;
        return row is { IsActive: true, IsWalkIn: false } ? row : null;
    }

    private async void DeleteCustomer(object? parameter)
    {
        var row = ResolveActiveRow(parameter);
        if (row is null || row.IsWalkIn)
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        SelectedCustomer = row;
        var confirm = MessageBox.Show(
            $"Bạn có chắc muốn xóa khách hàng {row.Code} - {row.Name}?\n\n" +
            "Khách hàng sẽ được đánh dấu ngừng giao dịch. Lịch sử giao dịch vẫn được giữ lại.",
            "Xóa khách hàng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            await _repository.SetActiveAsync(row.Id, false, session);
            await SearchAsync();
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể xóa khách hàng.";
            IsLoading = false;
        }
    }

    private async void ToggleActive(object? parameter)
    {
        var row = parameter as CustomerRowViewModel ?? SelectedCustomer;
        if (row is null || row.IsWalkIn)
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        SelectedCustomer = row;
        if (row.IsActive)
        {
            var confirm = MessageBox.Show(
                $"Bạn có chắc muốn ngừng giao dịch khách hàng {row.Code} - {row.Name}?\n\n" +
                "Lịch sử giao dịch vẫn được giữ lại.",
                "Ngừng giao dịch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            await _repository.SetActiveAsync(row.Id, !row.IsActive, session);
            await SearchAsync();
        }
        catch (Exception)
        {
            ErrorMessage = row.IsActive
                ? "Không thể ngừng giao dịch khách hàng."
                : "Không thể kích hoạt lại khách hàng.";
            IsLoading = false;
        }
    }
}
