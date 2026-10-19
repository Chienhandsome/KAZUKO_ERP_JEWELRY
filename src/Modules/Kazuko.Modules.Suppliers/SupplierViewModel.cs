using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Shared.UI.Services;
using Kazuko.Core.Suppliers;
using Kazuko.Modules.Suppliers.Dialogs;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private const int GroupTabIndex = 1;
    private const int PurchaseHistoryTabIndex = 2;
    private const int DebtTabIndex = 3;
    private const int ReportTabIndex = 4;

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

    private readonly ISupplierRepository _repository;
    private readonly ISupplierEditorService _editor;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IDebtWorkbenchRepository? _debtWorkbench;
    private readonly IDebtSettlementDialogService? _settlementDialog;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private bool _filtersReady;
    private bool _isLoadingGroups;
    private string _searchText = string.Empty;
    private SupplierGroupOption? _selectedGroup;
    private string? _selectedRegion;
    private string _selectedStatus = "Tất cả";
    private SupplierRowViewModel? _selectedSupplier;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty;
    private int _selectedTabIndex;
    private bool _isDetailLoading;

    // Statistics
    private int _totalCount;
    private int _activeCount;
    private int _inactiveCount;
    private decimal _totalPurchasesSum;
    private decimal _totalDebtSum;

    // Nhóm NCC
    private SupplierGroupRowViewModel? _selectedGroupRow;
    private bool _isGroupLoading;

    // Lịch sử nhập hàng
    private DateTime? _historyFromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _historyToDate = DateTime.Today;
    private bool _isHistoryLoading;

    // Công nợ
    private decimal _debtIncurred;
    private decimal _debtPaid;
    private decimal _debtCurrent;
    private decimal _debtOverdue;
    private string _paymentHistoryError = string.Empty;

    // Báo cáo
    private DateTime _reportFromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _reportToDate = DateTime.Today;
    private int _reportTopN = 10;
    private int _reportInactiveDays = 30;
    private bool _isReportLoading;
    private string _reportErrorMessage = string.Empty;

    public SupplierViewModel(
        ISupplierRepository repository,
        ISupplierEditorService editor,
        ICurrentSessionAccessor sessionAccessor,
        IDebtWorkbenchRepository? debtWorkbench = null,
        IDebtSettlementDialogService? settlementDialog = null)
    {
        _repository = repository;
        _editor = editor;
        _sessionAccessor = sessionAccessor;
        _debtWorkbench = debtWorkbench;
        _settlementDialog = settlementDialog;

        Groups.Add(new SupplierGroupOption(0, string.Empty, "Tất cả nhóm", true, false));
        Regions.Add("Tất cả khu vực");
        foreach (var region in RegionOptions)
            Regions.Add(region);
        StatusOptions.Add("Tất cả");
        StatusOptions.Add("Đang hợp tác");
        StatusOptions.Add("Ngừng hợp tác");

        SelectedGroup = Groups[0];
        SelectedRegion = Regions[0];

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        ExportExcelCommand = new RelayCommand<object?>(ExportExcel, _ => !IsLoading);
        AddCommand = new RelayCommand(AddSupplier, () => !IsLoading);
        EditCommand = new ParameterRelayCommand(
            EditSupplier,
            parameter => !IsLoading && (parameter is SupplierRowViewModel || SelectedSupplier is not null));
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsLoading && SelectedSupplier is not null);
        DeleteCommand = new ParameterRelayCommand(
            DeleteSupplier,
            parameter => !IsLoading && ResolveActiveRow(parameter) is not null);
        DeleteSelectedCommand = new RelayCommand(
            () => DeleteSupplier(SelectedSupplier),
            () => !IsLoading && SelectedSupplier?.IsActive == true);
        ToggleActiveCommand = new ParameterRelayCommand(
            ToggleActive,
            parameter => !IsLoading && (parameter is SupplierRowViewModel || SelectedSupplier is not null));
        ViewPlaceholderCommand = new RelayCommand(() => { }, () => false);
        ImportExcelCommand = new RelayCommand(() => { }, () => false);

        // Nhóm NCC
        AddGroupCommand = new RelayCommand(AddGroup, () => !IsGroupLoading);
        EditGroupCommand = new RelayCommand(EditGroup, () => !IsGroupLoading && SelectedGroupRow is not null);
        DeleteGroupCommand = new AsyncRelayCommand(DeleteGroupAsync, () => !IsGroupLoading && SelectedGroupRow is not null);

        // Lịch sử nhập hàng
        LoadPurchaseHistoryCommand = new AsyncRelayCommand(LoadPurchaseHistoryAsync, () => !IsHistoryLoading && HasSelection);

        // Công nợ
        PaySupplierCommand = new AsyncRelayCommand(PaySupplierAsync,
            () => !IsLoading && !IsDetailLoading && HasSelection && HasCurrentDebt
                && _debtWorkbench is not null && _settlementDialog is not null
                && _sessionAccessor.Current is not null);

        // Báo cáo
        LoadTopSuppliersCommand = new AsyncRelayCommand(LoadTopSuppliersAsync, () => !IsReportLoading);
        LoadInactiveSuppliersCommand = new AsyncRelayCommand(LoadInactiveSuppliersAsync, () => !IsReportLoading);
        LoadDistributionCommand = new AsyncRelayCommand(LoadDistributionAsync, () => !IsReportLoading);
        ExportTopSuppliersCommand = new RelayCommand<object?>(ExportTopSuppliers, _ => !IsReportLoading && TopSuppliers.Count > 0);
        ExportInactiveSuppliersCommand = new RelayCommand<object?>(ExportInactiveSuppliers, _ => !IsReportLoading && InactiveSuppliers.Count > 0);
        ExportDistributionCommand = new RelayCommand<object?>(ExportDistribution, _ => !IsReportLoading && GroupDistribution.Count > 0);

        _ = InitializeAsync();
    }

    public ObservableCollection<SupplierRowViewModel> Suppliers { get; } = new();
    public ObservableCollection<SupplierGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<SupplierGroupRowViewModel> SupplierGroups { get; } = new();
    public ObservableCollection<SupplierPurchaseHistoryRowViewModel> PurchaseHistory { get; } = new();
    public ObservableCollection<SupplierPaymentHistoryRowViewModel> PaymentHistory { get; } = new();
    public ObservableCollection<TopSupplierRowViewModel> TopSuppliers { get; } = new();
    public ObservableCollection<InactiveSupplierRowViewModel> InactiveSuppliers { get; } = new();
    public ObservableCollection<SupplierDistributionRowViewModel> GroupDistribution { get; } = new();
    public ObservableCollection<SupplierDistributionRowViewModel> RegionDistribution { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) && _filtersReady)
                _ = SearchAsync(300);
        }
    }

    public SupplierGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value) && _filtersReady && !_isLoadingGroups)
                _ = SearchAsync();
        }
    }

    public string? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (SetProperty(ref _selectedRegion, value) && _filtersReady)
                _ = SearchAsync();
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value) && _filtersReady)
                _ = SearchAsync();
        }
    }

    public SupplierRowViewModel? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedCode));
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedPhone));
                OnPropertyChanged(nameof(SelectedEmail));
                OnPropertyChanged(nameof(SelectedAddress));
                OnPropertyChanged(nameof(SelectedRegionName));
                OnPropertyChanged(nameof(SelectedTaxCode));
                OnPropertyChanged(nameof(SelectedContactName));
                OnPropertyChanged(nameof(SelectedContactTitle));
                OnPropertyChanged(nameof(SelectedNotes));
                OnPropertyChanged(nameof(SelectedGroupName));
                OnPropertyChanged(nameof(SelectedTotalPurchasesText));
                OnPropertyChanged(nameof(SelectedDebtText));
                OnPropertyChanged(nameof(SelectedLastPurchaseText));
                OnPropertyChanged(nameof(SelectedIsActive));
                OnPropertyChanged(nameof(CanDeleteSelected));
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                ((RelayCommand)DeleteSelectedCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)DeleteCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadPurchaseHistoryCommand).NotifyCanExecuteChanged();

                // Reload tab chi tiết khi đổi NCC đang chọn.
                if (_selectedTabIndex == PurchaseHistoryTabIndex)
                    _ = LoadPurchaseHistoryAsync();
                else if (_selectedTabIndex == DebtTabIndex && !IsDetailLoading)
                    _ = LoadDebtDetailsAsync();
            }
        }
    }

    public bool HasSelection => SelectedSupplier is not null;
    public bool CanDeleteSelected => !IsLoading && SelectedSupplier?.IsActive == true;
    public string SelectedCode => SelectedSupplier?.Code ?? string.Empty;
    public string SelectedName => SelectedSupplier?.Name ?? string.Empty;
    public string SelectedPhone => SelectedSupplier?.Phone ?? string.Empty;
    public string SelectedEmail => SelectedSupplier?.Email ?? string.Empty;
    public string SelectedAddress => SelectedSupplier?.Address ?? string.Empty;
    public string SelectedRegionName => SelectedSupplier?.Region ?? string.Empty;
    public string SelectedTaxCode => SelectedSupplier?.TaxCode ?? string.Empty;
    public string SelectedContactName => SelectedSupplier?.ContactName ?? string.Empty;
    public string SelectedContactTitle => SelectedSupplier?.ContactTitle ?? string.Empty;
    public string SelectedNotes => SelectedSupplier?.Notes ?? string.Empty;
    public string SelectedGroupName => SelectedSupplier?.SupplierGroupName ?? string.Empty;
    public string SelectedTotalPurchasesText => SelectedSupplier?.TotalPurchasesText ?? "0";
    public string SelectedDebtText => SelectedSupplier?.CurrentDebtText ?? "0";
    public string SelectedLastPurchaseText => SelectedSupplier?.LastPurchaseText ?? "—";
    public bool SelectedIsActive => SelectedSupplier?.IsActive ?? false;

    // KPI Card statistics
    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int ActiveCount
    {
        get => _activeCount;
        private set => SetProperty(ref _activeCount, value);
    }

    public int InactiveCount
    {
        get => _inactiveCount;
        private set => SetProperty(ref _inactiveCount, value);
    }

    public decimal TotalPurchasesSum
    {
        get => _totalPurchasesSum;
        private set => SetProperty(ref _totalPurchasesSum, value);
    }

    public decimal TotalDebtSum
    {
        get => _totalDebtSum;
        private set => SetProperty(ref _totalDebtSum, value);
    }

    public string TotalPurchasesSumText => TotalPurchasesSum.ToString("N0", ViCulture) + " đ";
    public string TotalDebtSumText => TotalDebtSum.ToString("N0", ViCulture) + " đ";

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
            if (SetProperty(ref _selectedTabIndex, value))
            {
                if (value == GroupTabIndex)
                    _ = LoadSupplierGroupsAsync();
                else if (value == PurchaseHistoryTabIndex)
                    _ = LoadPurchaseHistoryAsync();
                else if (value == DebtTabIndex && !IsDetailLoading)
                    _ = LoadDebtDetailsAsync();
                else if (value == ReportTabIndex && TopSuppliers.Count == 0 && !IsReportLoading)
                    _ = LoadReportsAsync();
            }
        }
    }

    #region Nhóm NCC

    public SupplierGroupRowViewModel? SelectedGroupRow
    {
        get => _selectedGroupRow;
        set
        {
            if (SetProperty(ref _selectedGroupRow, value))
            {
                ((RelayCommand)EditGroupCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteGroupCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsGroupLoading
    {
        get => _isGroupLoading;
        private set
        {
            if (SetProperty(ref _isGroupLoading, value))
            {
                ((RelayCommand)AddGroupCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditGroupCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteGroupCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand AddGroupCommand { get; }
    public ICommand EditGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }

    private async Task LoadSupplierGroupsAsync()
    {
        if (IsGroupLoading)
            return;
        IsGroupLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var details = await _repository.GetGroupDetailsAsync();
            var selectedId = SelectedGroupRow?.Id;
            SupplierGroups.Clear();
            foreach (var d in details)
                SupplierGroups.Add(new SupplierGroupRowViewModel(d));
            SelectedGroupRow = selectedId.HasValue
                ? SupplierGroups.FirstOrDefault(g => g.Id == selectedId.Value)
                : SupplierGroups.FirstOrDefault();
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể tải danh sách nhóm nhà cung cấp.";
        }
        finally
        {
            IsGroupLoading = false;
        }
    }

    private void AddGroup()
    {
        var vm = new SupplierGroupEditDialogViewModel(_repository, _sessionAccessor, null);
        var dialog = new SupplierGroupEditDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        var saved = false;
        vm.RequestClose += ok =>
        {
            saved = ok;
            dialog.DialogResult = ok;
        };
        dialog.ShowDialog();
        if (saved)
            _ = RefreshSupplierGroupsAsync();
    }

    private void EditGroup()
    {
        if (SelectedGroupRow is null)
            return;
        var vm = new SupplierGroupEditDialogViewModel(_repository, _sessionAccessor, SelectedGroupRow.Detail);
        var dialog = new SupplierGroupEditDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        var saved = false;
        vm.RequestClose += ok =>
        {
            saved = ok;
            dialog.DialogResult = ok;
        };
        dialog.ShowDialog();
        if (saved)
            _ = RefreshSupplierGroupsAsync();
    }

    private async Task RefreshSupplierGroupsAsync()
    {
        await LoadGroupsAsync();
        await LoadSupplierGroupsAsync();
    }

    private async Task DeleteGroupAsync()
    {
        var row = SelectedGroupRow;
        if (row is null || IsGroupLoading)
            return;

        if (row.IsDefault)
        {
            MessageBox.Show("Không thể xóa nhóm mặc định.", "Xóa nhóm",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc muốn xóa nhóm '{row.Name}'?",
            "Xóa nhóm nhà cung cấp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var deleted = false;
        IsGroupLoading = true;
        try
        {
            await _repository.DeleteGroupAsync(row.Id, session);
            deleted = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Xóa nhóm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsGroupLoading = false;
        }
        if (deleted)
            await RefreshSupplierGroupsAsync();
    }

    #endregion

    #region Lịch sử nhập hàng

    public DateTime? HistoryFromDate
    {
        get => _historyFromDate;
        set => SetProperty(ref _historyFromDate, value);
    }

    public DateTime? HistoryToDate
    {
        get => _historyToDate;
        set => SetProperty(ref _historyToDate, value);
    }

    public bool IsHistoryLoading
    {
        get => _isHistoryLoading;
        private set
        {
            if (SetProperty(ref _isHistoryLoading, value))
                ((AsyncRelayCommand)LoadPurchaseHistoryCommand).NotifyCanExecuteChanged();
        }
    }

    public ICommand LoadPurchaseHistoryCommand { get; }

    private async Task LoadPurchaseHistoryAsync()
    {
        if (IsHistoryLoading || SelectedSupplier is null)
            return;
        IsHistoryLoading = true;
        try
        {
            var items = await _repository.GetPurchaseHistoryAsync(
                SelectedSupplier.Id, HistoryFromDate, HistoryToDate);
            PurchaseHistory.Clear();
            var index = 1;
            foreach (var item in items)
                PurchaseHistory.Add(new SupplierPurchaseHistoryRowViewModel(item, index++));
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể tải lịch sử nhập hàng.";
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    #endregion

    #region Công nợ

    public string DebtIncurredText => _debtIncurred.ToString("N0", ViCulture);
    public string DebtPaidText => _debtPaid.ToString("N0", ViCulture);
    public string DebtCurrentText => _debtCurrent.ToString("N0", ViCulture);
    public string DebtOverdueText => _debtOverdue.ToString("N0", ViCulture);
    public bool HasCurrentDebt => _debtCurrent > 0;

    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        private set
        {
            if (SetProperty(ref _isDetailLoading, value))
                ((AsyncRelayCommand)PaySupplierCommand).NotifyCanExecuteChanged();
        }
    }

    public string PaymentHistoryError
    {
        get => _paymentHistoryError;
        private set => SetProperty(ref _paymentHistoryError, value);
    }

    public ICommand PaySupplierCommand { get; }

    private async Task LoadDebtDetailsAsync()
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = new CancellationTokenSource();
        var token = _detailCts.Token;

        IsDetailLoading = true;
        PaymentHistoryError = string.Empty;

        _debtIncurred = 0;
        _debtPaid = 0;
        _debtCurrent = 0;
        _debtOverdue = 0;
        PaymentHistory.Clear();
        RefreshDebtTexts();

        var supplierId = SelectedSupplier?.Id;
        if (supplierId is null || _debtWorkbench is null)
        {
            IsDetailLoading = false;
            return;
        }

        try
        {
            var session = _sessionAccessor.Current;
            if (session is null)
                return;

            // Tổng hợp từ danh sách công nợ NCC (tìm theo mã NCC).
            var filter = new Kazuko.Core.Finance.SupplierDebtSearchFilter(
                SearchText: SelectedSupplier?.Code ?? string.Empty,
                StatusCode: string.Empty,
                FromDate: null,
                ToDate: null);
            var debts = await _debtWorkbench.SearchSupplierDebtsAsync(filter, session, token);
            token.ThrowIfCancellationRequested();
            var row = debts.FirstOrDefault(d => d.SupplierId == supplierId.Value);
            if (row is not null)
            {
                _debtIncurred = row.TotalIncurredVnd;
                _debtPaid = row.PaidVnd;
                _debtCurrent = row.RemainingVnd;
            }

            // Quá hạn: các khoản còn nợ quá 30 ngày.
            var openDebts = await _debtWorkbench.GetSupplierOpenPayablesAsync(supplierId.Value, session, token);
            token.ThrowIfCancellationRequested();
            var cutoff = DateTime.Today.AddDays(-30);
            _debtOverdue = openDebts
                .Where(d => d.CreatedAt.Date <= cutoff)
                .Sum(d => d.RemainingAmountVnd);
            RefreshDebtTexts();
            ((AsyncRelayCommand)PaySupplierCommand).NotifyCanExecuteChanged();

            // Lịch sử thanh toán (dòng chi tiền).
            var ledger = await _debtWorkbench.GetSupplierLedgerAsync(supplierId.Value, session, token);
            token.ThrowIfCancellationRequested();
            foreach (var entry in ledger.Where(e =>
                e.SourceType == Kazuko.Core.Finance.DebtLedgerSourceTypes.PayableDebtPayment))
                PaymentHistory.Add(new SupplierPaymentHistoryRowViewModel(entry));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested)
                PaymentHistoryError = "Không thể tải chi tiết công nợ. Vui lòng mở lại tab để thử lại.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsDetailLoading = false;
        }
    }

    private void RefreshDebtTexts()
    {
        OnPropertyChanged(nameof(DebtIncurredText));
        OnPropertyChanged(nameof(DebtPaidText));
        OnPropertyChanged(nameof(DebtCurrentText));
        OnPropertyChanged(nameof(DebtOverdueText));
        OnPropertyChanged(nameof(HasCurrentDebt));
    }

    private async Task PaySupplierAsync()
    {
        if (IsLoading || SelectedSupplier is null || _debtWorkbench is null || _settlementDialog is null)
            return;

        var supplierId = SelectedSupplier.Id;
        var supplierCode = SelectedSupplier.Code;
        var supplierName = SelectedSupplier.Name;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            var session = _sessionAccessor.Current;
            if (session is null)
                return;

            var openDebts = await _debtWorkbench.GetSupplierOpenPayablesAsync(supplierId, session);
            if (openDebts.Count == 0)
            {
                MessageBox.Show("Không còn khoản phải trả nào.", "Chi tiền",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var allocations = openDebts.Select(x => new DebtSettlementAllocationLine(
                Kazuko.Core.Finance.DebtLedgerSourceTypes.PayableDebt, x.Id, x.Code, x.Code,
                x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

            var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
                FinancialVoucherTypes.Out,
                $"{supplierCode} — {supplierName}",
                allocations,
                $"Chi trả công nợ nhà cung cấp {supplierCode}",
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

            StatusMessage = "Đã chi tiền nhà cung cấp.";
            await SearchAsync();
            await LoadDebtDetailsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Chi tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Báo cáo

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
        set => SetProperty(ref _reportTopN, value);
    }

    public int ReportInactiveDays
    {
        get => _reportInactiveDays;
        set => SetProperty(ref _reportInactiveDays, value);
    }

    public bool IsReportLoading
    {
        get => _isReportLoading;
        private set
        {
            if (SetProperty(ref _isReportLoading, value))
            {
                ((AsyncRelayCommand)LoadTopSuppliersCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadInactiveSuppliersCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadDistributionCommand).NotifyCanExecuteChanged();
                ((RelayCommand<object?>)ExportTopSuppliersCommand).NotifyCanExecuteChanged();
                ((RelayCommand<object?>)ExportInactiveSuppliersCommand).NotifyCanExecuteChanged();
                ((RelayCommand<object?>)ExportDistributionCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ReportErrorMessage
    {
        get => _reportErrorMessage;
        private set => SetProperty(ref _reportErrorMessage, value);
    }

    public ICommand LoadTopSuppliersCommand { get; }
    public ICommand LoadInactiveSuppliersCommand { get; }
    public ICommand LoadDistributionCommand { get; }
    public ICommand ExportTopSuppliersCommand { get; }
    public ICommand ExportInactiveSuppliersCommand { get; }
    public ICommand ExportDistributionCommand { get; }

    private async Task LoadReportsAsync()
    {
        if (IsReportLoading) return;
        await LoadTopSuppliersAsync();
        await LoadInactiveSuppliersAsync();
        await LoadDistributionAsync();
    }

    private async Task LoadTopSuppliersAsync()
    {
        if (IsReportLoading) return;
        IsReportLoading = true;
        ReportErrorMessage = string.Empty;
        try
        {
            var items = await _repository.GetTopSuppliersAsync(ReportFromDate, ReportToDate, ReportTopN);
            TopSuppliers.Clear();
            var rank = 1;
            foreach (var item in items)
                TopSuppliers.Add(new TopSupplierRowViewModel(item, rank++));
        }
        catch (Exception ex)
        {
            ReportErrorMessage = "Không thể tải báo cáo top NCC: " + ex.Message;
        }
        finally
        {
            IsReportLoading = false;
        }
    }

    private Task LoadInactiveSuppliersAsync()
    {
        if (IsReportLoading) return Task.CompletedTask;
        IsReportLoading = true;
        ReportErrorMessage = string.Empty;
        try
        {
            // Dùng danh sách NCC hiện tại + lịch sử nhập để xác định NCC lâu không nhập.
            var cutoff = DateTime.Today.AddDays(-ReportInactiveDays);
            var rows = new List<InactiveSupplierRowViewModel>();
            foreach (var s in Suppliers.Where(x => x.IsActive))
            {
                var lastPurchase = s.LastPurchaseDate;
                if (lastPurchase is null || lastPurchase.Value.Date <= cutoff)
                {
                    var days = lastPurchase.HasValue
                        ? (DateTime.Today - lastPurchase.Value.Date).Days
                        : int.MaxValue;
                    rows.Add(new InactiveSupplierRowViewModel(
                        s.Id, s.Code, s.Name, s.Phone, s.SupplierGroupName,
                        lastPurchase, days, s.TotalPurchases));
                }
            }
            InactiveSuppliers.Clear();
            foreach (var r in rows.OrderByDescending(x => x.DaysSinceLastPurchase))
                InactiveSuppliers.Add(r);
        }
        catch (Exception ex)
        {
            ReportErrorMessage = "Không thể tải báo cáo NCC chưa nhập lại: " + ex.Message;
        }
        finally
        {
            IsReportLoading = false;
        }
        return Task.CompletedTask;
    }

    private Task LoadDistributionAsync()
    {
        if (IsReportLoading) return Task.CompletedTask;
        IsReportLoading = true;
        ReportErrorMessage = string.Empty;
        try
        {
            var active = Suppliers.Where(x => x.IsActive).ToList();
            GroupDistribution.Clear();
            foreach (var g in active.GroupBy(x => string.IsNullOrWhiteSpace(x.SupplierGroupName) ? "(Chưa phân nhóm)" : x.SupplierGroupName)
                                     .OrderByDescending(x => x.Sum(s => s.TotalPurchases)))
                GroupDistribution.Add(new SupplierDistributionRowViewModel(
                    g.Key, g.Count(), g.Sum(s => s.TotalPurchases)));
            RegionDistribution.Clear();
            foreach (var g in active.GroupBy(x => string.IsNullOrWhiteSpace(x.Region) ? "(Chưa rõ)" : x.Region)
                                    .OrderByDescending(x => x.Sum(s => s.TotalPurchases)))
                RegionDistribution.Add(new SupplierDistributionRowViewModel(
                    g.Key, g.Count(), g.Sum(s => s.TotalPurchases)));
        }
        catch (Exception ex)
        {
            ReportErrorMessage = "Không thể tải báo cáo phân bố: " + ex.Message;
        }
        finally
        {
            IsReportLoading = false;
        }
        return Task.CompletedTask;
    }

    private void ExportTopSuppliers(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<TopSupplierRowViewModel>[]
        {
            new("Rank", "Hạng", o => o.Rank, ExcelCellKind.Integer),
            new("Code", "Mã NCC", o => o.Item.Code),
            new("Name", "Tên NCC", o => o.Item.Name),
            new("Phone", "SĐT", o => o.Item.Phone),
            new("Group", "Nhóm", o => o.Item.SupplierGroupName),
            new("PurchaseCount", "Số lần nhập", o => o.Item.PurchaseCount, ExcelCellKind.Integer),
            new("TotalPurchases", "Tổng nhập (VND)", o => o.Item.TotalPurchases, ExcelCellKind.Money),
        };
        ExcelExportService.Export("Top nhà cung cấp", $"TopNCC_{DateTime.Now:yyyyMMdd_HHmmss}", grid, TopSuppliers, columns);
    }

    private void ExportInactiveSuppliers(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<InactiveSupplierRowViewModel>[]
        {
            new("Code", "Mã NCC", o => o.Code),
            new("Name", "Tên NCC", o => o.Name),
            new("Phone", "SĐT", o => o.Phone),
            new("Group", "Nhóm", o => o.GroupName),
            new("LastPurchase", "Lần nhập cuối", o => o.LastPurchaseDate, ExcelCellKind.Date),
            new("Days", "Số ngày chưa nhập", o => o.DaysSinceLastPurchase == int.MaxValue ? 0 : o.DaysSinceLastPurchase, ExcelCellKind.Integer),
            new("TotalPurchases", "Tổng đã nhập (VND)", o => o.TotalPurchases, ExcelCellKind.Money),
        };
        ExcelExportService.Export("NCC chưa nhập lại", $"NCCChuaNhapLai_{DateTime.Now:yyyyMMdd_HHmmss}", grid, InactiveSuppliers, columns);
    }

    private void ExportDistribution(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<SupplierDistributionRowViewModel>[]
        {
            new("Name", "Tên", o => o.Name),
            new("Count", "Số NCC", o => o.SupplierCount, ExcelCellKind.Integer),
            new("Total", "Tổng nhập (VND)", o => o.TotalPurchases, ExcelCellKind.Money),
        };
        ExcelExportService.Export("Phân bố NCC", $"PhanBoNCC_{DateTime.Now:yyyyMMdd_HHmmss}", grid, GroupDistribution, columns);
    }

    #endregion

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportExcelCommand { get; }

    private void ExportExcel(object? parameter)
    {
        var grid = parameter as System.Windows.Controls.DataGrid;
        var columns = new ExcelExportColumn<SupplierRowViewModel>[]
        {
            new("Index", "STT", o => o.Index, ExcelCellKind.Integer),
            new("Code", "Mã NCC", o => o.Code),
            new("Name", "Tên nhà cung cấp", o => o.Name),
            new("Phone", "SĐT", o => o.Phone),
            new("SupplierGroup", "Nhóm NCC", o => o.SupplierGroupName),
            new("Region", "Khu vực", o => o.Region),
            new("ContactName", "Người liên hệ", o => o.ContactName),
            new("TotalPurchases", "Tổng mua", o => o.TotalPurchases, ExcelCellKind.Money),
            new("CurrentDebt", "Công nợ", o => o.CurrentDebt, ExcelCellKind.Money),
            new("LastPurchase", "Mua cuối", o => o.LastPurchaseDate, ExcelCellKind.Date),
            new("Status", "Trạng thái", o => o.IsActive ? "Đang hoạt động" : "Ngừng giao dịch"),
        };
        ExcelExportService.Export(
            "Xuất danh sách nhà cung cấp",
            $"DanhSachNhaCungCap_{DateTime.Now:yyyyMMdd_HHmmss}",
            grid, Suppliers, columns);
    }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand ViewPlaceholderCommand { get; }
    public ICommand ImportExcelCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadGroupsAsync();
        _filtersReady = true;
        await SearchAsync();
    }

    private async Task LoadGroupsAsync()
    {
        _isLoadingGroups = true;
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
            ErrorMessage = "Không thể tải nhóm nhà cung cấp.";
        }
        finally
        {
            _isLoadingGroups = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadGroupsAsync();
        await SearchAsync();
    }

    private Task SearchAsync() => SearchAsync(0);

    private async Task SearchAsync(int delayMilliseconds)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Đang tải danh sách nhà cung cấp...";

        try
        {
            if (delayMilliseconds > 0)
                await Task.Delay(delayMilliseconds, token);
            token.ThrowIfCancellationRequested();

            var filter = new SupplierSearchFilter(
                SearchText,
                SelectedGroup?.Id ?? 0,
                SelectedRegion is null || SelectedRegion == "Tất cả khu vực" ? string.Empty : SelectedRegion,
                SelectedStatus switch
                {
                    "Đang hợp tác" => true,
                    "Ngừng hợp tác" => false,
                    _ => null
                });

            var rows = await _repository.SearchAsync(filter, token);
            token.ThrowIfCancellationRequested();

            var previousId = SelectedSupplier?.Id;
            Suppliers.Clear();
            var index = 1;
            foreach (var row in rows)
                Suppliers.Add(new SupplierRowViewModel(row, index++));

            SelectedSupplier = previousId is long id
                ? Suppliers.FirstOrDefault(s => s.Id == id) ?? Suppliers.FirstOrDefault()
                : Suppliers.FirstOrDefault();

            IsEmpty = Suppliers.Count == 0;
            StatusMessage = IsEmpty
                ? "Không tìm thấy nhà cung cấp phù hợp."
                : $"Hiển thị {Suppliers.Count} nhà cung cấp.";

            // Update KPI stats
            TotalCount = Suppliers.Count;
            ActiveCount = Suppliers.Count(s => s.IsActive);
            InactiveCount = Suppliers.Count(s => !s.IsActive);
            TotalPurchasesSum = Suppliers.Sum(s => s.TotalPurchases);
            TotalDebtSum = Suppliers.Sum(s => s.CurrentDebt);
            OnPropertyChanged(nameof(TotalPurchasesSumText));
            OnPropertyChanged(nameof(TotalDebtSumText));
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer search.
        }
        catch (Exception)
        {
            if (token.IsCancellationRequested)
                return;

            Suppliers.Clear();
            SelectedSupplier = null;
            IsEmpty = true;
            ErrorMessage = "Không thể tải danh sách nhà cung cấp. Vui lòng thử lại.";
            StatusMessage = string.Empty;
            TotalCount = 0;
            ActiveCount = 0;
            InactiveCount = 0;
            TotalPurchasesSum = 0;
            TotalDebtSum = 0;
            OnPropertyChanged(nameof(TotalPurchasesSumText));
            OnPropertyChanged(nameof(TotalDebtSumText));
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private void AddSupplier()
    {
        var result = _editor.ShowCreate();
        if (result is null)
            return;
        _ = SearchAsync();
    }

    private void EditSelected()
    {
        if (SelectedSupplier is null)
            return;
        EditSupplier(SelectedSupplier);
    }

    private void EditSupplier(object? parameter)
    {
        var row = parameter as SupplierRowViewModel ?? SelectedSupplier;
        if (row is null)
            return;

        SelectedSupplier = row;
        var result = _editor.ShowEdit(row.Id);
        if (result is null)
            return;
        _ = SearchAsync();
    }

    private SupplierRowViewModel? ResolveActiveRow(object? parameter)
    {
        var row = parameter as SupplierRowViewModel ?? SelectedSupplier;
        return row is { IsActive: true } ? row : null;
    }

    private async void DeleteSupplier(object? parameter)
    {
        var row = ResolveActiveRow(parameter);
        if (row is null)
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        SelectedSupplier = row;
        var confirm = MessageBox.Show(
            $"Bạn có chắc muốn xóa nhà cung cấp {row.Code} - {row.Name}?\n\n" +
            "Nhà cung cấp sẽ được đánh dấu ngừng hợp tác. Lịch sử chứng từ vẫn được giữ nguyên.",
            "Xóa nhà cung cấp",
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
            ErrorMessage = "Không thể xóa nhà cung cấp.";
            IsLoading = false;
        }
    }

    private async void ToggleActive(object? parameter)
    {
        var row = parameter as SupplierRowViewModel ?? SelectedSupplier;
        if (row is null)
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        SelectedSupplier = row;
        if (row.IsActive)
        {
            var confirm = MessageBox.Show(
                $"Bạn có chắc muốn ngừng hợp tác với nhà cung cấp {row.Code} - {row.Name}?\n\n" +
                "Lịch sử chứng từ và giao dịch vẫn được giữ nguyên.",
                "Ngừng hợp tác",
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
                ? "Không thể ngừng hợp tác nhà cung cấp."
                : "Không thể kích hoạt lại nhà cung cấp.";
            IsLoading = false;
        }
    }
}
