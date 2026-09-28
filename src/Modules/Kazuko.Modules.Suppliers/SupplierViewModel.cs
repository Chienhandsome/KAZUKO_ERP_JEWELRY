using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

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
    private CancellationTokenSource? _searchCts;
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

    // Statistics
    private int _totalCount;
    private int _activeCount;
    private int _inactiveCount;
    private decimal _totalPurchasesSum;
    private decimal _totalDebtSum;

    public SupplierViewModel(
        ISupplierRepository repository,
        ISupplierEditorService editor,
        ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _editor = editor;
        _sessionAccessor = sessionAccessor;

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
        AddCommand = new RelayCommand(AddSupplier, () => !IsLoading);
        EditCommand = new ParameterRelayCommand(
            EditSupplier,
            parameter => !IsLoading && (parameter is SupplierRowViewModel || SelectedSupplier is not null));
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsLoading && SelectedSupplier is not null);
        ToggleActiveCommand = new ParameterRelayCommand(
            ToggleActive,
            parameter => !IsLoading && (parameter is SupplierRowViewModel || SelectedSupplier is not null));
        ViewPlaceholderCommand = new RelayCommand(() => { }, () => false);
        ImportExcelCommand = new RelayCommand(() => { }, () => false);

        _ = InitializeAsync();
    }

    public ObservableCollection<SupplierRowViewModel> Suppliers { get; } = new();
    public ObservableCollection<SupplierGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public SupplierGroupOption? SelectedGroup
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
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => SelectedSupplier is not null;
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
                ((ParameterRelayCommand)ToggleActiveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
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
    public ICommand ToggleActiveCommand { get; }
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
            ErrorMessage = "Không thể tải nhóm nhà cung cấp.";
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
        StatusMessage = "Đang tải danh sách nhà cung cấp...";

        try
        {
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
