using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Debt;

public sealed class DebtViewModel : ObservableObject
{
    private readonly IDebtWorkbenchRepository _workbench;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerEditorService _customerEditor;
    private readonly ISupplierEditorService _supplierEditor;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IDebtSettlementDialogService _settlementDialog;

    private int _selectedTabIndex;
    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private GroupFilterOption? _selectedGroup;
    private StatusFilterOption? _selectedStatus;
    private CustomerDebtRowViewModel? _selectedCustomer;
    private SupplierDebtRowViewModel? _selectedSupplier;
    private WorkerGoldDebtRowViewModel? _selectedWorkerGoldDebt;
    private GoldPurityFilterOption? _selectedGoldPurity;
    private WorkerDebtStatusFilterOption? _selectedWorkerDebtStatus;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private string _quickNote = string.Empty;
    private bool _isBusy;
    private bool _suppressSelectionLoad;

    private string _kpiCustomerRemainingText = "0";
    private string _kpiCustomerCollectText = "0";
    private string _kpiCustomerPaidText = "0";
    private string _kpiSupplierRemainingText = "0";
    private string _kpiSupplierPayText = "0";
    private string _kpiSupplierPaidText = "0";
    private string _kpiCustomerOverdueText = "0";
    private string _kpiCustomerOverdueCountText = "0 khách hàng";
    private string _kpiSupplierOverdueText = "0";
    private string _kpiSupplierOverdueCountText = "0 nhà cung cấp";
    private string _kpiWorkerGoldRemainingText = "0";
    private string _kpiWorkerGoldDebtorCountText = "0 thợ";

    public DebtViewModel(
        IDebtWorkbenchRepository workbench,
        ICustomerRepository customers,
        ICustomerEditorService customerEditor,
        ISupplierEditorService supplierEditor,
        ICurrentSessionAccessor sessionAccessor,
        IDebtSettlementDialogService settlementDialog)
    {
        _workbench = workbench;
        _customers = customers;
        _customerEditor = customerEditor;
        _supplierEditor = supplierEditor;
        _sessionAccessor = sessionAccessor;
        _settlementDialog = settlementDialog;

        StatusOptions.Add(new StatusFilterOption(string.Empty, "Tất cả trạng thái"));
        StatusOptions.Add(new StatusFilterOption(DebtDisplayStatuses.Normal, "Bình thường"));
        StatusOptions.Add(new StatusFilterOption(DebtDisplayStatuses.Overdue, "Quá hạn"));
        StatusOptions.Add(new StatusFilterOption(DebtDisplayStatuses.Paid, "Đã thanh toán"));
        SelectedStatus = StatusOptions[0];

        Groups.Add(new GroupFilterOption(0, "Tất cả nhóm KH"));
        SelectedGroup = Groups[0];

        WorkerDebtStatusOptions.Add(new WorkerDebtStatusFilterOption(WorkerDebtStatusFilterCodes.Open, "Còn nợ"));
        WorkerDebtStatusOptions.Add(new WorkerDebtStatusFilterOption(WorkerDebtStatusFilterCodes.Paid, "Đã trả hết"));
        WorkerDebtStatusOptions.Add(new WorkerDebtStatusFilterOption(WorkerDebtStatusFilterCodes.All, "Tất cả"));
        SelectedWorkerDebtStatus = WorkerDebtStatusOptions[0];

        GoldPurityOptions.Add(new GoldPurityFilterOption(null, "Tất cả loại vàng"));
        SelectedGoldPurity = GoldPurityOptions[0];

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        AddCustomerCommand = new RelayCommand(AddCustomer, () => !IsBusy && IsCustomerTab);
        AddSupplierCommand = new RelayCommand(AddSupplier, () => !IsBusy && IsSupplierTab);
        AddPartyCommand = new RelayCommand(AddParty, () => !IsBusy && !IsWorkerGoldDebtTab);
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsBusy && HasSelection && !IsWorkerGoldDebtTab);
        CollectOrPayCommand = new AsyncRelayCommand(CollectOrPayAsync, () => !IsBusy && CanCollectOrPay);
        CollectCustomerAllCommand = new RelayCommand<CustomerDebtRowViewModel>(row => { _ = CollectCustomerAsync(row); });
        PayCustomerAllCommand = new RelayCommand<CustomerDebtRowViewModel>(row => { _ = PayCustomerAsync(row); });
        CollectSupplierAllCommand = new RelayCommand<SupplierDebtRowViewModel>(row => { _ = CollectSupplierAsync(row); });
        PaySupplierAllCommand = new RelayCommand<SupplierDebtRowViewModel>(row => { _ = PaySupplierAsync(row); });
        SettleLedgerEntryCommand = new RelayCommand<DebtLedgerRowViewModel>(row => { _ = SettleLedgerEntryAsync(row); });
        RecordRepaymentCommand = new RelayCommand<WorkerGoldDebtRowViewModel>(row => { _ = RecordRepaymentAsync(row); });
        PrintCommand = new RelayCommand(ShowNotImplemented, () => HasSelection);
        ExportExcelCommand = new RelayCommand(ShowNotImplemented, () => true);
        ViewDetailCommand = new RelayCommand(() => { }, () => HasSelection);
        GuideCommand = new RelayCommand(() =>
            MessageBox.Show(
                "Theo dõi công nợ khách hàng (đơn hàng − thanh toán), nhà cung cấp (PAYABLE_DEBTS) và nợ vàng thợ.",
                "Hướng dẫn công nợ",
                MessageBoxButton.OK,
                MessageBoxImage.Information));

        _ = InitializeAsync();
    }

    public ObservableCollection<CustomerDebtRowViewModel> Customers { get; } = new();
    public ObservableCollection<SupplierDebtRowViewModel> Suppliers { get; } = new();
    public ObservableCollection<WorkerGoldDebtRowViewModel> WorkerGoldDebts { get; } = new();
    public ObservableCollection<DebtLedgerRowViewModel> LedgerEntries { get; } = new();
    public ObservableCollection<GroupFilterOption> Groups { get; } = new();
    public ObservableCollection<StatusFilterOption> StatusOptions { get; } = new();
    public ObservableCollection<WorkerDebtStatusFilterOption> WorkerDebtStatusOptions { get; } = new();
    public ObservableCollection<GoldPurityFilterOption> GoldPurityOptions { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
                return;
            OnPropertyChanged(nameof(IsCustomerTab));
            OnPropertyChanged(nameof(IsSupplierTab));
            OnPropertyChanged(nameof(IsWorkerGoldDebtTab));
            OnPropertyChanged(nameof(AddPartyButtonText));
            OnPropertyChanged(nameof(CollectOrPayButtonText));
            OnPropertyChanged(nameof(DetailTitle));
            ((RelayCommand)AddCustomerCommand).NotifyCanExecuteChanged();
            ((RelayCommand)AddSupplierCommand).NotifyCanExecuteChanged();
            ((RelayCommand)AddPartyCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CollectOrPayCommand).NotifyCanExecuteChanged();
            _ = SearchAsync();
        }
    }

    public bool IsCustomerTab => SelectedTabIndex == 0;
    public bool IsSupplierTab => SelectedTabIndex == 1;
    public bool IsWorkerGoldDebtTab => SelectedTabIndex == 2;
    public string AddPartyButtonText => IsCustomerTab ? "+ Thêm khách hàng" : "+ Thêm nhà cung cấp";
    public string CollectOrPayButtonText => IsCustomerTab ? "Thu tiền" : "Chi tiền";
    public string DetailTitle => IsCustomerTab ? "Thông tin khách hàng" : "Thông tin nhà cung cấp";

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public GroupFilterOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public StatusFilterOption? SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public CustomerDebtRowViewModel? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (!SetProperty(ref _selectedCustomer, value))
                return;
            NotifySelectionChanged();
            if (!_suppressSelectionLoad && value is not null)
                _ = LoadCustomerDetailAsync(value.CustomerId);
            else if (value is null)
                LedgerEntries.Clear();
        }
    }

    public SupplierDebtRowViewModel? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (!SetProperty(ref _selectedSupplier, value))
                return;
            NotifySelectionChanged();
            if (!_suppressSelectionLoad && value is not null)
                _ = LoadSupplierDetailAsync(value.SupplierId);
            else if (value is null)
                LedgerEntries.Clear();
        }
    }

    public GoldPurityFilterOption? SelectedGoldPurity
    {
        get => _selectedGoldPurity;
        set => SetProperty(ref _selectedGoldPurity, value);
    }

    public WorkerDebtStatusFilterOption? SelectedWorkerDebtStatus
    {
        get => _selectedWorkerDebtStatus;
        set => SetProperty(ref _selectedWorkerDebtStatus, value);
    }

    public WorkerGoldDebtRowViewModel? SelectedWorkerGoldDebt
    {
        get => _selectedWorkerGoldDebt;
        set
        {
            if (!SetProperty(ref _selectedWorkerGoldDebt, value))
                return;
            // Tab nợ vàng thợ không có panel chi tiết — chỉ cập nhật trạng thái chọn.
            NotifySelectionChanged();
        }
    }

    public bool HasSelection => IsCustomerTab
        ? SelectedCustomer is not null
        : IsSupplierTab
            ? SelectedSupplier is not null
            : SelectedWorkerGoldDebt is not null;

    public bool CanCollectOrPay =>
        HasSelection && !IsWorkerGoldDebtTab && (IsCustomerTab
            ? SelectedCustomer!.RemainingVnd > 0
            : SelectedSupplier!.RemainingVnd > 0);

    public string SelectedCode => IsCustomerTab ? SelectedCustomer?.Code ?? "—" : SelectedSupplier?.Code ?? "—";
    public string SelectedName => IsCustomerTab ? SelectedCustomer?.Name ?? "—" : SelectedSupplier?.Name ?? "—";
    public string SelectedPhone => IsCustomerTab ? SelectedCustomer?.Phone ?? "—" : SelectedSupplier?.Phone ?? "—";
    public string SelectedGroupName => IsCustomerTab ? SelectedCustomer?.GroupName ?? "—" : SelectedSupplier?.GroupName ?? "—";
    public string SelectedAddress => IsCustomerTab ? SelectedCustomer?.Address ?? "—" : SelectedSupplier?.Address ?? "—";
    public string SelectedRemainingText => IsCustomerTab
        ? SelectedCustomer?.RemainingText ?? "0"
        : SelectedSupplier?.RemainingText ?? "0";
    public decimal SelectedRemainingVnd => IsCustomerTab
        ? SelectedCustomer?.RemainingVnd ?? 0
        : SelectedSupplier?.RemainingVnd ?? 0;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string QuickNote
    {
        get => _quickNote;
        set => SetProperty(ref _quickNote, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            ((RelayCommand)AddCustomerCommand).NotifyCanExecuteChanged();
            ((RelayCommand)AddSupplierCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CollectOrPayCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsBusyOverlayVisible => IsBusy;
    public bool IsCustomerEmpty => !IsBusy && IsCustomerTab && Customers.Count == 0;
    public bool IsSupplierEmpty => !IsBusy && IsSupplierTab && Suppliers.Count == 0;
    public bool IsWorkerGoldDebtEmpty => !IsBusy && IsWorkerGoldDebtTab && WorkerGoldDebts.Count == 0;
    public bool HasDetail => HasSelection;

    public string KpiCustomerRemainingText
    {
        get => _kpiCustomerRemainingText;
        private set => SetProperty(ref _kpiCustomerRemainingText, value);
    }

    public string KpiCustomerCollectText
    {
        get => _kpiCustomerCollectText;
        private set => SetProperty(ref _kpiCustomerCollectText, value);
    }

    public string KpiCustomerPaidText
    {
        get => _kpiCustomerPaidText;
        private set => SetProperty(ref _kpiCustomerPaidText, value);
    }

    public string KpiSupplierRemainingText
    {
        get => _kpiSupplierRemainingText;
        private set => SetProperty(ref _kpiSupplierRemainingText, value);
    }

    public string KpiSupplierPayText
    {
        get => _kpiSupplierPayText;
        private set => SetProperty(ref _kpiSupplierPayText, value);
    }

    public string KpiSupplierPaidText
    {
        get => _kpiSupplierPaidText;
        private set => SetProperty(ref _kpiSupplierPaidText, value);
    }

    public string KpiCustomerOverdueText
    {
        get => _kpiCustomerOverdueText;
        private set => SetProperty(ref _kpiCustomerOverdueText, value);
    }

    public string KpiCustomerOverdueCountText
    {
        get => _kpiCustomerOverdueCountText;
        private set => SetProperty(ref _kpiCustomerOverdueCountText, value);
    }

    public string KpiSupplierOverdueText
    {
        get => _kpiSupplierOverdueText;
        private set => SetProperty(ref _kpiSupplierOverdueText, value);
    }

    public string KpiSupplierOverdueCountText
    {
        get => _kpiSupplierOverdueCountText;
        private set => SetProperty(ref _kpiSupplierOverdueCountText, value);
    }

    public string KpiWorkerGoldRemainingText
    {
        get => _kpiWorkerGoldRemainingText;
        private set => SetProperty(ref _kpiWorkerGoldRemainingText, value);
    }

    public string KpiWorkerGoldDebtorCountText
    {
        get => _kpiWorkerGoldDebtorCountText;
        private set => SetProperty(ref _kpiWorkerGoldDebtorCountText, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCustomerCommand { get; }
    public ICommand AddSupplierCommand { get; }
    public ICommand AddPartyCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand CollectOrPayCommand { get; }
    public ICommand CollectCustomerAllCommand { get; }
    public ICommand PayCustomerAllCommand { get; }
    public ICommand CollectSupplierAllCommand { get; }
    public ICommand PaySupplierAllCommand { get; }
    public ICommand SettleLedgerEntryCommand { get; }
    public ICommand RecordRepaymentCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ViewDetailCommand { get; }
    public ICommand GuideCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var session = RequireSession();

            Groups.Clear();
            Groups.Add(new GroupFilterOption(0, "Tất cả nhóm KH"));
            foreach (var group in await _customers.GetGroupsAsync())
                Groups.Add(new GroupFilterOption(group.Id, group.Name));
            SelectedGroup = Groups[0];

            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RefreshAsync() => SearchAsync(reloadSummary: true);

    private Task SearchAsync() => SearchAsync(reloadSummary: false);

    private async Task SearchAsync(bool reloadSummary)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await RefreshCoreAsync(reloadSummary);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync(bool reloadSummary = true)
    {
        var session = RequireSession();
        if (reloadSummary)
            await LoadSummaryAsync(session);

        if (IsCustomerTab)
            await LoadCustomersAsync(session);
        else if (IsSupplierTab)
            await LoadSuppliersAsync(session);
        else
            await LoadWorkerGoldDebtsAsync(session);
    }

    private async Task LoadSummaryAsync(AuthenticatedSession session)
    {
        var summary = await _workbench.GetSummaryAsync(session);
        KpiCustomerRemainingText = FormatMoney(summary.CustomerRemainingTotal);
        KpiCustomerCollectText = FormatMoney(summary.CustomerRemainingTotal);
        KpiCustomerPaidText = FormatMoney(summary.CustomerCollectedTotal);
        KpiSupplierRemainingText = FormatMoney(summary.SupplierRemainingTotal);
        KpiSupplierPayText = FormatMoney(summary.SupplierRemainingTotal);
        KpiSupplierPaidText = FormatMoney(summary.SupplierPaidTotal);
        KpiCustomerOverdueText = FormatMoney(summary.CustomerOverdueTotal);
        KpiCustomerOverdueCountText = $"{summary.CustomerOverdueCount} khách hàng";
        KpiSupplierOverdueText = FormatMoney(summary.SupplierOverdueTotal);
        KpiSupplierOverdueCountText = $"{summary.SupplierOverdueCount} nhà cung cấp";
    }

    private async Task LoadCustomersAsync(AuthenticatedSession session)
    {
        var rows = await _workbench.SearchCustomerDebtsAsync(
            new CustomerDebtSearchFilter(
                SearchText,
                SelectedGroup?.Id ?? 0,
                SelectedStatus?.Code ?? string.Empty,
                FromDate,
                ToDate),
            session);

        var previousId = SelectedCustomer?.CustomerId;
        _suppressSelectionLoad = true;
        Customers.Clear();
        var index = 1;
        foreach (var row in rows)
            Customers.Add(new CustomerDebtRowViewModel(index++, row));
        SelectedCustomer = previousId is long id
            ? Customers.FirstOrDefault(x => x.CustomerId == id) ?? Customers.FirstOrDefault()
            : Customers.FirstOrDefault();
        _suppressSelectionLoad = false;

        StatusMessage = $"Hiển thị {Customers.Count} khách hàng có phát sinh công nợ.";
        OnPropertyChanged(nameof(IsCustomerEmpty));
        if (SelectedCustomer is not null)
            await LoadCustomerDetailAsync(SelectedCustomer.CustomerId);
        else
            LedgerEntries.Clear();
    }

    private async Task LoadSuppliersAsync(AuthenticatedSession session)
    {
        var rows = await _workbench.SearchSupplierDebtsAsync(
            new SupplierDebtSearchFilter(
                SearchText,
                SelectedStatus?.Code ?? string.Empty,
                FromDate,
                ToDate),
            session);

        var previousId = SelectedSupplier?.SupplierId;
        _suppressSelectionLoad = true;
        Suppliers.Clear();
        var index = 1;
        foreach (var row in rows)
            Suppliers.Add(new SupplierDebtRowViewModel(index++, row));
        SelectedSupplier = previousId is long id
            ? Suppliers.FirstOrDefault(x => x.SupplierId == id) ?? Suppliers.FirstOrDefault()
            : Suppliers.FirstOrDefault();
        _suppressSelectionLoad = false;

        StatusMessage = $"Hiển thị {Suppliers.Count} nhà cung cấp có phát sinh công nợ.";
        OnPropertyChanged(nameof(IsSupplierEmpty));
        if (SelectedSupplier is not null)
            await LoadSupplierDetailAsync(SelectedSupplier.SupplierId);
        else
            LedgerEntries.Clear();
    }

    private async Task LoadWorkerGoldDebtsAsync(AuthenticatedSession session)
    {
        var onlyOpen = SelectedWorkerDebtStatus is null
            || SelectedWorkerDebtStatus.Code == WorkerDebtStatusFilterCodes.Open;

        var rows = await _workbench.SearchWorkerGoldDebtsAsync(
            new WorkerGoldDebtSearchFilter(
                SearchText,
                SelectedGoldPurity?.Code,
                onlyOpen,
                FromDate,
                ToDate),
            session);

        // "Đã trả hết": filter repo chỉ có OnlyOpen nên lọc phía client.
        if (SelectedWorkerDebtStatus?.Code == WorkerDebtStatusFilterCodes.Paid)
            rows = rows.Where(x => x.RemainingChi <= 0).ToList();

        var previousId = SelectedWorkerGoldDebt?.DebtId;
        _suppressSelectionLoad = true;
        WorkerGoldDebts.Clear();
        var index = 1;
        foreach (var row in rows)
            WorkerGoldDebts.Add(new WorkerGoldDebtRowViewModel(index++, row));
        SelectedWorkerGoldDebt = previousId is long id
            ? WorkerGoldDebts.FirstOrDefault(x => x.DebtId == id) ?? WorkerGoldDebts.FirstOrDefault()
            : WorkerGoldDebts.FirstOrDefault();
        _suppressSelectionLoad = false;

        StatusMessage = $"Hiển thị {WorkerGoldDebts.Count} khoản nợ vàng thợ.";
        OnPropertyChanged(nameof(IsWorkerGoldDebtEmpty));

        var remainingTotal = WorkerGoldDebts.Sum(x => x.RemainingChi);
        KpiWorkerGoldRemainingText = remainingTotal.ToString("N3", CultureInfo.CurrentCulture);
        var debtorCount = WorkerGoldDebts.Count(x => x.RemainingChi > 0);
        KpiWorkerGoldDebtorCountText = $"{debtorCount} thợ";

        // ComboBox loại vàng: distinct từ dữ liệu đang hiển thị.
        var currentPurity = SelectedGoldPurity?.Code;
        GoldPurityOptions.Clear();
        GoldPurityOptions.Add(new GoldPurityFilterOption(null, "Tất cả loại vàng"));
        foreach (var purity in WorkerGoldDebts
                     .Select(x => x.Row.GoldPurityCode)
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct()
                     .OrderBy(c => c))
            GoldPurityOptions.Add(new GoldPurityFilterOption(purity, purity!));
        SelectedGoldPurity = GoldPurityOptions.FirstOrDefault(x => x.Code == currentPurity)
            ?? GoldPurityOptions[0];
    }

    /// <summary>
    /// "Ghi trả vàng" trên từng dòng: mở dialog thu thập số chỉ/ngày/ghi chú,
    /// gọi repository (1 transaction) rồi tải lại danh sách.
    /// </summary>
    private async Task RecordRepaymentAsync(WorkerGoldDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.RemainingChi <= 0)
            return;

        var session = RequireSession();
        var result = WorkerGoldRepaymentDialog.Show(ResolveOwner(), row);
        if (result is null)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _workbench.RecordWorkerGoldRepaymentAsync(
                new WorkerGoldRepaymentRequest(row.DebtId, result.WeightChi, result.PaidDate, result.Notes),
                session);
            StatusMessage = $"Đã ghi nhận trả {result.WeightChi:N3} chỉ vàng — {row.WorkerDisplayName}.";
            await LoadWorkerGoldDebtsAsync(session);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Ghi nhận trả vàng", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCustomerDetailAsync(long customerId)
    {
        try
        {
            var session = RequireSession();
            var entries = await _workbench.GetCustomerLedgerAsync(customerId, session);
            LedgerEntries.Clear();
            foreach (var entry in entries)
                LedgerEntries.Add(new DebtLedgerRowViewModel(entry));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadSupplierDetailAsync(long supplierId)
    {
        try
        {
            var session = RequireSession();
            var entries = await _workbench.GetSupplierLedgerAsync(supplierId, session);
            LedgerEntries.Clear();
            foreach (var entry in entries)
                LedgerEntries.Add(new DebtLedgerRowViewModel(entry));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void AddParty()
    {
        if (IsCustomerTab)
            AddCustomer();
        else if (IsSupplierTab)
            AddSupplier();
    }

    private void AddCustomer()
    {
        var result = _customerEditor.ShowCreate();
        if (result is not null)
            _ = SearchAsync(reloadSummary: true);
    }

    private void AddSupplier()
    {
        var result = _supplierEditor.ShowCreate();
        if (result is not null)
            _ = SearchAsync(reloadSummary: true);
    }

    private void EditSelected()
    {
        if (IsCustomerTab && SelectedCustomer is not null)
        {
            if (_customerEditor.ShowEdit(SelectedCustomer.CustomerId) is not null)
                _ = SearchAsync(reloadSummary: true);
            return;
        }

        if (IsSupplierTab && SelectedSupplier is not null)
        {
            if (_supplierEditor.ShowEdit(SelectedSupplier.SupplierId) is not null)
                _ = SearchAsync(reloadSummary: true);
        }
    }

    private Task CollectOrPayAsync()
    {
        if (IsCustomerTab)
            return CollectCustomerAsync(SelectedCustomer);
        if (IsSupplierTab)
            return PaySupplierAsync(SelectedSupplier);
        // Tab nợ vàng thợ: nút Thu/Chi bị disable (CanCollectOrPay = false).
        return Task.CompletedTask;
    }

    /// <summary>
    /// "Thu toàn bộ" KH: giữ nguyên phạm vi Wave 2a — chỉ thu trên SALES_ORDERS
    /// (chưa gộp RECEIVABLE_DEBTS của KH; các khoản lẻ thu qua nút Thu ở lịch sử).
    /// </summary>
    /// <summary>"Thu toàn bộ" KH: thu các đơn hàng còn nợ, hiển thị N dòng phân bổ.</summary>
    private async Task CollectCustomerAsync(CustomerDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.RemainingVnd <= 0)
            return;

        var session = RequireSession();
        var openLines = await _workbench.GetCustomerOpenReceivablesAsync(row.CustomerId, session);
        if (openLines.Count == 0)
        {
            MessageBox.Show("Không còn khoản phải thu nào.", "Thu tiền", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allocations = openLines.Select(x => new DebtSettlementAllocationLine(
            DebtLedgerSourceTypes.SalesOrder, x.OrderId, x.OrderCode, x.OrderCode,
            x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

        var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            FinancialVoucherTypes.In,
            $"{row.Code} — {row.Name}",
            allocations,
            $"Thu công nợ khách hàng {row.Code}",
            AllowAmountEdit: true));
        if (result is null)
            return;

        await ExecuteDebtSettlementAsync(result, "Đã thu tiền khách hàng.", "Thu tiền");
    }

    /// <summary>"Chi toàn bộ" KH: chi các khoản DN nợ KH (PAYABLE_DEBTS party=CUSTOMER).</summary>
    private async Task PayCustomerAsync(CustomerDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.PayableRemainingVnd <= 0)
            return;

        var session = RequireSession();
        var openDebts = await _workbench.GetCustomerOpenPayablesAsync(row.CustomerId, session);
        if (openDebts.Count == 0)
        {
            MessageBox.Show("Không còn khoản phải trả nào.", "Chi tiền", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allocations = openDebts.Select(x => new DebtSettlementAllocationLine(
            DebtLedgerSourceTypes.PayableDebt, x.Id, x.Code, x.Code,
            x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

        var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            FinancialVoucherTypes.Out,
            $"{row.Code} — {row.Name}",
            allocations,
            $"Chi trả công nợ khách hàng {row.Code}",
            AllowAmountEdit: true));
        if (result is null)
            return;

        await ExecuteDebtSettlementAsync(result, "Đã chi tiền khách hàng.", "Chi tiền");
    }

    /// <summary>"Thu toàn bộ" NCC: thu các khoản NCC nợ DN (RECEIVABLE_DEBTS party=SUPPLIER).</summary>
    private async Task CollectSupplierAsync(SupplierDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.ReceivableRemainingVnd <= 0)
            return;

        var session = RequireSession();
        var openDebts = await _workbench.GetSupplierReceivableDebtsAsync(row.SupplierId, session);
        if (openDebts.Count == 0)
        {
            MessageBox.Show("Không còn khoản phải thu nào.", "Thu tiền", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allocations = openDebts.Select(x => new DebtSettlementAllocationLine(
            DebtLedgerSourceTypes.ReceivableDebt, x.Id, x.Code, x.Code,
            x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

        var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            FinancialVoucherTypes.In,
            $"{row.Code} — {row.Name}",
            allocations,
            $"Thu công nợ nhà cung cấp {row.Code}",
            AllowAmountEdit: true));
        if (result is null)
            return;

        await ExecuteDebtSettlementAsync(result, "Đã thu tiền nhà cung cấp.", "Thu tiền");
    }

    /// <summary>"Chi toàn bộ" NCC: chi các khoản DN nợ NCC (PAYABLE_DEBTS party=SUPPLIER).</summary>
    private async Task PaySupplierAsync(SupplierDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.RemainingVnd <= 0)
            return;

        var session = RequireSession();
        var openDebts = await _workbench.GetSupplierOpenPayablesAsync(row.SupplierId, session);
        if (openDebts.Count == 0)
        {
            MessageBox.Show("Không còn khoản phải trả nào.", "Chi tiền", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allocations = openDebts.Select(x => new DebtSettlementAllocationLine(
            DebtLedgerSourceTypes.PayableDebt, x.Id, x.Code, x.Code,
            x.RemainingAmountVnd, x.RemainingAmountVnd)).ToList();

        var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            FinancialVoucherTypes.Out,
            $"{row.Code} — {row.Name}",
            allocations,
            $"Chi trả công nợ nhà cung cấp {row.Code}",
            AllowAmountEdit: true));
        if (result is null)
            return;

        await ExecuteDebtSettlementAsync(result, "Đã chi tiền nhà cung cấp.", "Chi tiền");
    }

    /// <summary>
    /// Tất toán lẻ 1 dòng lịch sử (nút Thu/Chi trong cột Thao tác): mở dialog
    /// phiếu thu/chi dùng chung với 1 dòng phân bổ, cho phép sửa số tiền
    /// (thanh toán một phần), rồi gọi backend settle trong 1 transaction.
    /// </summary>
    private async Task SettleLedgerEntryAsync(DebtLedgerRowViewModel? row)
    {
        if (IsBusy || row is null)
            return;

        var entry = row.Entry;
        if (!entry.CanSettle || entry.RemainingAmountVnd <= 0
            || string.IsNullOrWhiteSpace(entry.SourceType)
            || entry.SourceId is not { } sourceId)
            return;

        var isReceive = string.Equals(
            entry.Direction, DebtLedgerDirections.Receive, StringComparison.OrdinalIgnoreCase);

        var allocations = new List<DebtSettlementAllocationLine>
        {
            new(entry.SourceType, sourceId,
                entry.DocumentCode ?? entry.SourceType,
                entry.DocumentCode,
                entry.RemainingAmountVnd,
                entry.RemainingAmountVnd)
        };

        var result = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            isReceive ? FinancialVoucherTypes.In : FinancialVoucherTypes.Out,
            SelectedName,
            allocations,
            $"Thanh toán {entry.DocumentCode}",
            AllowAmountEdit: true));
        if (result is null)
            return;

        await ExecuteDebtSettlementAsync(result, isReceive ? "Đã thu tiền." : "Đã chi tiền.", isReceive ? "Thu tiền" : "Chi tiền");
    }

    /// <summary>
    /// Thực thi tất toán từ kết quả dialog chung: gọi backend
    /// SettleDebtAllocationsAsync trong 1 transaction, xong tải lại dữ liệu.
    /// </summary>
    private async Task ExecuteDebtSettlementAsync(DebtSettlementDialogResult result, string successMessage, string dialogTitle)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.SettleDebtAllocationsAsync(
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
            StatusMessage = successMessage;
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }


    private static Window? ResolveOwner()
    {
        if (Application.Current?.Windows is null)
            return Application.Current?.MainWindow;

        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsActive)
                return window;
        }

        return Application.Current.MainWindow;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(CanCollectOrPay));
        OnPropertyChanged(nameof(SelectedCode));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedPhone));
        OnPropertyChanged(nameof(SelectedGroupName));
        OnPropertyChanged(nameof(SelectedAddress));
        OnPropertyChanged(nameof(SelectedRemainingText));
        OnPropertyChanged(nameof(SelectedRemainingVnd));
        ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CollectOrPayCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PrintCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ViewDetailCommand).NotifyCanExecuteChanged();
    }

    private static void ShowNotImplemented() =>
        MessageBox.Show("Chức năng sẽ được bổ sung sau.", "Công nợ", MessageBoxButton.OK, MessageBoxImage.Information);

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    private AuthenticatedSession RequireSession() =>
        _sessionAccessor.Current
        ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
