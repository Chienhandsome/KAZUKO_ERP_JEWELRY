using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

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
    private const int PurchaseHistoryFullLimit = 100;
    private const int PurchaseHistoryTabIndex = 3;
    private const int DebtTabIndex = 4;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ICustomerRepository _repository;
    private readonly ICustomerEditorService _editor;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private string _searchText = string.Empty;
    private CustomerGroupOption? _selectedGroup;
    private string? _selectedRegion;
    private string _selectedStatus = "Tất cả";
    private CustomerRowViewModel? _selectedCustomer;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty;
    private int _selectedTabIndex;
    private CustomerDebtSnapshot _debtSnapshot = new(0, 0, 0, 0);

    public CustomerViewModel(
        ICustomerRepository repository,
        ICustomerEditorService editor,
        ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _editor = editor;
        _sessionAccessor = sessionAccessor;

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
        AddCommand = new RelayCommand(AddCustomer, () => !IsLoading);
        EditCommand = new ParameterRelayCommand(
            EditCustomer,
            parameter => !IsLoading && (parameter is CustomerRowViewModel || SelectedCustomer is not null));
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsLoading && SelectedCustomer is not null);
        DeleteCommand = new ParameterRelayCommand(
            DeleteCustomer,
            parameter => !IsLoading && ResolveActiveRow(parameter) is not null);
        DeleteSelectedCommand = new RelayCommand(
            () => DeleteCustomer(SelectedCustomer),
            () => !IsLoading && SelectedCustomer?.IsActive == true);
        ToggleActiveCommand = new ParameterRelayCommand(
            ToggleActive,
            parameter => !IsLoading && (parameter is CustomerRowViewModel || SelectedCustomer is not null));
        ViewAllPurchasesCommand = new RelayCommand(
            () => SelectedTabIndex = PurchaseHistoryTabIndex,
            () => HasSelection);
        ViewDebtDetailsCommand = new RelayCommand(
            () => SelectedTabIndex = DebtTabIndex,
            () => HasSelection);
        ViewPlaceholderCommand = new RelayCommand(() => { }, () => false);
        ImportExcelCommand = new RelayCommand(() => { }, () => false);

        _ = InitializeAsync();
    }

    public ObservableCollection<CustomerRowViewModel> Customers { get; } = new();
    public ObservableCollection<CustomerGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<PurchaseHistoryRowViewModel> RecentPurchases { get; } = new();
    public ObservableCollection<PurchaseHistoryRowViewModel> FullPurchaseHistory { get; } = new();

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
                OnPropertyChanged(nameof(HasFullPurchaseHistory));
                OnPropertyChanged(nameof(CanDeleteSelected));
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                ((RelayCommand)DeleteSelectedCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)DeleteCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ViewAllPurchasesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ViewDebtDetailsCommand).NotifyCanExecuteChanged();
                _ = LoadSelectionDetailsAsync();
            }
        }
    }

    public bool HasSelection => SelectedCustomer is not null;
    public bool CanDeleteSelected => !IsLoading && SelectedCustomer?.IsActive == true;
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
    public bool HasFullPurchaseHistory => FullPurchaseHistory.Count > 0;

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
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand ViewAllPurchasesCommand { get; }
    public ICommand ViewDebtDetailsCommand { get; }
    public ICommand ViewPlaceholderCommand { get; }
    public ICommand ImportExcelCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadGroupsAsync();
        await SearchAsync();
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
                });

            var rows = await _repository.SearchAsync(filter, token);
            token.ThrowIfCancellationRequested();

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

        RecentPurchases.Clear();
        FullPurchaseHistory.Clear();
        SetDebtSnapshot(new CustomerDebtSnapshot(0, 0, 0, 0));
        OnPropertyChanged(nameof(RecentPurchasesTitle));
        OnPropertyChanged(nameof(HasRecentPurchases));
        OnPropertyChanged(nameof(HasFullPurchaseHistory));

        var customerId = SelectedCustomer?.Id;
        if (customerId is null)
            return;

        try
        {
            var historyTask = _repository.GetPurchaseHistoryAsync(
                customerId.Value, PurchaseHistoryFullLimit, token);
            var debtTask = _repository.GetDebtSnapshotAsync(customerId.Value, token);
            await Task.WhenAll(historyTask, debtTask);
            token.ThrowIfCancellationRequested();

            var history = await historyTask;
            foreach (var item in history)
                FullPurchaseHistory.Add(new PurchaseHistoryRowViewModel(item));
            foreach (var item in history.Take(PurchaseHistoryPreviewLimit))
                RecentPurchases.Add(new PurchaseHistoryRowViewModel(item));

            SetDebtSnapshot(await debtTask);
            OnPropertyChanged(nameof(RecentPurchasesTitle));
            OnPropertyChanged(nameof(HasRecentPurchases));
            OnPropertyChanged(nameof(HasFullPurchaseHistory));
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
    }

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
        if (row is null)
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
        return row is { IsActive: true } ? row : null;
    }

    private async void DeleteCustomer(object? parameter)
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
        if (row is null)
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
