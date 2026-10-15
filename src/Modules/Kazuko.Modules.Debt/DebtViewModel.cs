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
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
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

    public DebtViewModel(
        IDebtWorkbenchRepository workbench,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        ICustomerRepository customers,
        ICustomerEditorService customerEditor,
        ISupplierEditorService supplierEditor,
        ICurrentSessionAccessor sessionAccessor,
        IDebtSettlementDialogService settlementDialog)
    {
        _workbench = workbench;
        _funds = funds;
        _banks = banks;
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

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        AddCustomerCommand = new RelayCommand(AddCustomer, () => !IsBusy && IsCustomerTab);
        AddSupplierCommand = new RelayCommand(AddSupplier, () => !IsBusy && IsSupplierTab);
        AddPartyCommand = new RelayCommand(AddParty, () => !IsBusy);
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsBusy && HasSelection);
        CollectOrPayCommand = new AsyncRelayCommand(CollectOrPayAsync, () => !IsBusy && CanCollectOrPay);
        CollectCustomerAllCommand = new RelayCommand<CustomerDebtRowViewModel>(row => { _ = CollectCustomerAsync(row); });
        PayCustomerAllCommand = new RelayCommand<CustomerDebtRowViewModel>(row => { _ = PayCustomerAsync(row); });
        CollectSupplierAllCommand = new RelayCommand<SupplierDebtRowViewModel>(row => { _ = CollectSupplierAsync(row); });
        PaySupplierAllCommand = new RelayCommand<SupplierDebtRowViewModel>(row => { _ = PaySupplierAsync(row); });
        SettleLedgerEntryCommand = new RelayCommand<DebtLedgerRowViewModel>(row => { _ = SettleLedgerEntryAsync(row); });
        PrintCommand = new RelayCommand(ShowNotImplemented, () => HasSelection);
        ExportExcelCommand = new RelayCommand(ShowNotImplemented, () => true);
        ViewDetailCommand = new RelayCommand(() => { }, () => HasSelection);
        GuideCommand = new RelayCommand(() =>
            MessageBox.Show(
                "Theo dõi công nợ khách hàng (đơn hàng − thanh toán) và nhà cung cấp (PAYABLE_DEBTS).",
                "Hướng dẫn công nợ",
                MessageBoxButton.OK,
                MessageBoxImage.Information));

        _ = InitializeAsync();
    }

    public ObservableCollection<CustomerDebtRowViewModel> Customers { get; } = new();
    public ObservableCollection<SupplierDebtRowViewModel> Suppliers { get; } = new();
    public ObservableCollection<DebtLedgerRowViewModel> LedgerEntries { get; } = new();
    public ObservableCollection<GroupFilterOption> Groups { get; } = new();
    public ObservableCollection<StatusFilterOption> StatusOptions { get; } = new();
    public ObservableCollection<CashFundOption> Funds { get; } = new();
    public ObservableCollection<BankAccountOption> Banks { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
                return;
            OnPropertyChanged(nameof(IsCustomerTab));
            OnPropertyChanged(nameof(IsSupplierTab));
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

    public bool HasSelection => IsCustomerTab ? SelectedCustomer is not null : SelectedSupplier is not null;
    public bool CanCollectOrPay =>
        HasSelection && (IsCustomerTab
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
            foreach (var fund in await _funds.GetActiveFundsAsync(session))
                Funds.Add(fund);
            foreach (var bank in await _banks.GetActiveAccountsAsync(session))
                Banks.Add(bank);

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
        else
            await LoadSuppliersAsync(session);
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
        else
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
        return PaySupplierAsync(SelectedSupplier);
    }

    /// <summary>
    /// "Thu toàn bộ" KH: giữ nguyên phạm vi Wave 2a — chỉ thu trên SALES_ORDERS
    /// (chưa gộp RECEIVABLE_DEBTS của KH; các khoản lẻ thu qua nút Thu ở lịch sử).
    /// </summary>
    private async Task CollectCustomerAsync(CustomerDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.RemainingVnd <= 0)
            return;

        var dialogVm = new DebtPaymentDialogViewModel(
            "Thu tiền khách hàng",
            $"{row.Code} — {row.Name}",
            row.RemainingVnd,
            Funds.ToList(),
            Banks.ToList());

        if (!ShowPaymentDialog(dialogVm))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.CollectCustomerPaymentAsync(
                new CustomerCollectPaymentRequest(
                    row.CustomerId,
                    dialogVm.BuildAllocations(),
                    string.IsNullOrWhiteSpace(dialogVm.Notes) ? null : dialogVm.Notes.Trim()),
                session);
            StatusMessage = "Đã thu tiền khách hàng.";
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Thu tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Chi toàn bộ" KH: chi các khoản DN nợ KH (PAYABLE_DEBTS party=CUSTOMER), FIFO.</summary>
    private async Task PayCustomerAsync(CustomerDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.PayableRemainingVnd <= 0)
            return;

        var dialogVm = new DebtPaymentDialogViewModel(
            "Chi tiền khách hàng",
            $"{row.Code} — {row.Name}",
            row.PayableRemainingVnd,
            Funds.ToList(),
            Banks.ToList());

        if (!ShowPaymentDialog(dialogVm))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.PayCustomerFifoAsync(
                row.CustomerId,
                dialogVm.BuildAllocations(),
                string.IsNullOrWhiteSpace(dialogVm.Notes) ? null : dialogVm.Notes.Trim(),
                session);
            StatusMessage = "Đã chi tiền khách hàng.";
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Chi tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Thu toàn bộ" NCC: thu các khoản NCC nợ DN (RECEIVABLE_DEBTS party=SUPPLIER), FIFO.</summary>
    private async Task CollectSupplierAsync(SupplierDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.ReceivableRemainingVnd <= 0)
            return;

        var dialogVm = new DebtPaymentDialogViewModel(
            "Thu tiền nhà cung cấp",
            $"{row.Code} — {row.Name}",
            row.ReceivableRemainingVnd,
            Funds.ToList(),
            Banks.ToList());

        if (!ShowPaymentDialog(dialogVm))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.CollectSupplierPaymentAsync(
                new SupplierCollectPaymentRequest(
                    row.SupplierId,
                    dialogVm.BuildAllocations(),
                    string.IsNullOrWhiteSpace(dialogVm.Notes) ? null : dialogVm.Notes.Trim()),
                session);
            StatusMessage = "Đã thu tiền nhà cung cấp.";
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Thu tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PaySupplierAsync(SupplierDebtRowViewModel? row)
    {
        if (IsBusy || row is null || row.RemainingVnd <= 0)
            return;

        var dialogVm = new DebtPaymentDialogViewModel(
            "Chi tiền nhà cung cấp",
            $"{row.Code} — {row.Name}",
            row.RemainingVnd,
            Funds.ToList(),
            Banks.ToList());

        if (!ShowPaymentDialog(dialogVm))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.PaySupplierFifoAsync(
                row.SupplierId,
                dialogVm.BuildAllocations(),
                string.IsNullOrWhiteSpace(dialogVm.Notes) ? null : dialogVm.Notes.Trim(),
                session);
            StatusMessage = "Đã chi tiền nhà cung cấp.";
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Chi tiền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tất toán lẻ 1 dòng lịch sử (nút Thu/Chi trong cột Thao tác): mở dialog
    /// phiếu thu/chi của module Tài chính (chế độ chỉ thu thập input, cho phép
    /// sửa số tiền = thanh toán một phần), rồi gọi backend settle đúng 1 khoản
    /// nguồn đó trong 1 transaction. Xong thì tải lại danh sách + lịch sử + KPI.
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

        var dialogResult = _settlementDialog.ShowDialog(new DebtSettlementDialogRequest(
            VoucherType: isReceive ? FinancialVoucherTypes.In : FinancialVoucherTypes.Out,
            PartyDisplayName: SelectedName,
            SourceDocumentCode: entry.DocumentCode,
            DefaultAmountVnd: entry.RemainingAmountVnd,
            DefaultNotes: $"Thanh toán {entry.DocumentCode}",
            AllowAmountEdit: true));
        if (dialogResult is null)
            return;

        if (dialogResult.AmountVnd <= 0 || dialogResult.AmountVnd > entry.RemainingAmountVnd)
        {
            MessageBox.Show(
                $"Số tiền phải > 0 và không vượt số còn lại ({entry.RemainingAmountVnd:N0} VND).",
                isReceive ? "Thu tiền" : "Chi tiền",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            await _workbench.SettleSingleLedgerEntryAsync(
                new SingleLedgerSettleRequest(
                    entry.SourceType,
                    sourceId,
                    [BuildSingleAllocation(dialogResult)],
                    dialogResult.Notes),
                session);
            StatusMessage = isReceive ? "Đã thu tiền." : "Đã chi tiền.";
            await SearchAsync(reloadSummary: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(
                ex.Message,
                isReceive ? "Thu tiền" : "Chi tiền",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Dialog tất toán chỉ chọn phương thức (không chọn quỹ/tài khoản):
    /// dùng quỹ mặc định cho tiền mặt, tài khoản đầu tiên cho chuyển khoản/thẻ.
    /// </summary>
    private PaymentAllocationDraft BuildSingleAllocation(DebtSettlementDialogResult result)
    {
        var method = (result.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        long? fundId = null;
        long? bankId = null;
        if (method == PaymentMethods.Cash)
        {
            fundId = Funds.FirstOrDefault(f => f.IsDefault)?.Id ?? Funds.FirstOrDefault()?.Id
                ?? throw new InvalidOperationException(
                    "Chưa có quỹ tiền mặt đang hoạt động. Hãy tạo quỹ trong module Tài chính.");
        }
        else if (method is PaymentMethods.BankTransfer or PaymentMethods.Card)
        {
            bankId = Banks.FirstOrDefault()?.Id
                ?? throw new InvalidOperationException(
                    "Chưa có tài khoản ngân hàng đang hoạt động. Hãy tạo tài khoản trong module Tài chính.");
        }
        else
        {
            throw new InvalidOperationException("Phương thức thanh toán không hợp lệ.");
        }

        return new PaymentAllocationDraft(method, result.AmountVnd, fundId, bankId, result.ReferenceNumber);
    }

    private static bool ShowPaymentDialog(DebtPaymentDialogViewModel viewModel)
    {
        var dialog = new DebtPaymentDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var accepted = false;
        viewModel.RequestClose += result =>
        {
            accepted = result;
            if (dialog.IsLoaded)
                dialog.DialogResult = result;
            else
                dialog.Close();
        };

        dialog.ShowDialog();
        return accepted && viewModel.Confirmed;
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
