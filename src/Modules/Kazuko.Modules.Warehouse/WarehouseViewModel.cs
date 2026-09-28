using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class WarehouseViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IWarehouseRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private CancellationTokenSource? _loadCts;
    private bool _suppressDocumentDetailLoad;

    private string _balanceSearchText = string.Empty;
    private string _selectedMaterialType = "Tất cả";
    private WarehouseLocationOption? _selectedLocation;
    private bool _lowStockOnly;
    private MaterialBalanceRowViewModel? _selectedBalance;

    private string _documentSearchText = string.Empty;
    private string _selectedDocumentType = "Tất cả";
    private string _selectedDocumentStatus = "Tất cả";
    private StockDocumentRowViewModel? _selectedDocument;

    private string _movementSearchText = string.Empty;
    private WarehouseLocationOption? _selectedMovementLocation;
    private StockMovementRowViewModel? _selectedMovement;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isBalancesEmpty = true;
    private bool _isDocumentsEmpty = true;
    private bool _isMovementsEmpty = true;
    private bool _hasDocumentDetail;
    private int _selectedTabIndex;

    private int _balanceCount;
    private int _lowStockCount;
    private decimal _materialStockValue;
    private int _documentCount;
    private int _todayDocumentCount;

    private string _detailCode = string.Empty;
    private string _detailDate = string.Empty;
    private string _detailType = string.Empty;
    private string _detailStatus = string.Empty;
    private string _detailReference = string.Empty;
    private string _detailCounterparty = string.Empty;
    private string _detailNotes = string.Empty;
    private string _detailCreatedBy = string.Empty;
    private string _detailPostedBy = string.Empty;
    private string _detailPostedAt = string.Empty;

    public WarehouseViewModel(IWarehouseRepository repository, ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;

        Locations.Add(new WarehouseLocationOption(0, 0, string.Empty, "Tất cả vị trí", "WAREHOUSE"));
        MovementLocations.Add(new WarehouseLocationOption(0, 0, string.Empty, "Tất cả vị trí", "WAREHOUSE"));
        SelectedLocation = Locations[0];
        SelectedMovementLocation = MovementLocations[0];

        MaterialTypeOptions.Add("Tất cả");
        MaterialTypeOptions.Add("GOLD");
        MaterialTypeOptions.Add("STONE");
        MaterialTypeOptions.Add("ACCESSORY");

        DocumentTypeOptions.Add("Tất cả");
        DocumentTypeOptions.Add("OPENING");
        DocumentTypeOptions.Add("RECEIPT");
        DocumentTypeOptions.Add("ISSUE");
        DocumentTypeOptions.Add("TRANSFER");

        DocumentStatusOptions.Add("Tất cả");
        DocumentStatusOptions.Add("DRAFT");
        DocumentStatusOptions.Add("POSTED");
        DocumentStatusOptions.Add("CANCELLED");

        SearchBalancesCommand = new AsyncRelayCommand(SearchBalancesAsync, () => !IsLoading);
        SearchDocumentsCommand = new AsyncRelayCommand(SearchDocumentsAsync, () => !IsLoading);
        SearchMovementsCommand = new AsyncRelayCommand(SearchMovementsAsync, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsLoading);
        ClearBalanceFiltersCommand = new RelayCommand(ClearBalanceFilters, () => !IsLoading);
        ClearDocumentFiltersCommand = new RelayCommand(ClearDocumentFilters, () => !IsLoading);
        ClearMovementFiltersCommand = new RelayCommand(ClearMovementFilters, () => !IsLoading);

        _ = InitializeAsync();
    }

    public ObservableCollection<MaterialBalanceRowViewModel> Balances { get; } = new();
    public ObservableCollection<StockDocumentRowViewModel> Documents { get; } = new();
    public ObservableCollection<StockDocumentLineRowViewModel> DocumentLines { get; } = new();
    public ObservableCollection<StockMovementRowViewModel> Movements { get; } = new();
    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<WarehouseLocationOption> MovementLocations { get; } = new();
    public ObservableCollection<string> MaterialTypeOptions { get; } = new();
    public ObservableCollection<string> DocumentTypeOptions { get; } = new();
    public ObservableCollection<string> DocumentStatusOptions { get; } = new();

    public string BalanceSearchText
    {
        get => _balanceSearchText;
        set => SetProperty(ref _balanceSearchText, value);
    }

    public string SelectedMaterialType
    {
        get => _selectedMaterialType;
        set => SetProperty(ref _selectedMaterialType, value);
    }

    public WarehouseLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set => SetProperty(ref _selectedLocation, value);
    }

    public bool LowStockOnly
    {
        get => _lowStockOnly;
        set => SetProperty(ref _lowStockOnly, value);
    }

    public MaterialBalanceRowViewModel? SelectedBalance
    {
        get => _selectedBalance;
        set => SetProperty(ref _selectedBalance, value);
    }

    public string DocumentSearchText
    {
        get => _documentSearchText;
        set => SetProperty(ref _documentSearchText, value);
    }

    public string SelectedDocumentType
    {
        get => _selectedDocumentType;
        set => SetProperty(ref _selectedDocumentType, value);
    }

    public string SelectedDocumentStatus
    {
        get => _selectedDocumentStatus;
        set => SetProperty(ref _selectedDocumentStatus, value);
    }

    public StockDocumentRowViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetProperty(ref _selectedDocument, value) || _suppressDocumentDetailLoad)
                return;
            _ = LoadDocumentDetailAsync(value);
        }
    }

    public string MovementSearchText
    {
        get => _movementSearchText;
        set => SetProperty(ref _movementSearchText, value);
    }

    public WarehouseLocationOption? SelectedMovementLocation
    {
        get => _selectedMovementLocation;
        set => SetProperty(ref _selectedMovementLocation, value);
    }

    public StockMovementRowViewModel? SelectedMovement
    {
        get => _selectedMovement;
        set => SetProperty(ref _selectedMovement, value);
    }

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
                OnPropertyChanged(nameof(IsBusyOverlayVisible));
        }
    }

    public bool IsBusyOverlayVisible => IsLoading;

    public bool IsBalancesEmpty
    {
        get => _isBalancesEmpty;
        private set => SetProperty(ref _isBalancesEmpty, value);
    }

    public bool IsDocumentsEmpty
    {
        get => _isDocumentsEmpty;
        private set => SetProperty(ref _isDocumentsEmpty, value);
    }

    public bool IsMovementsEmpty
    {
        get => _isMovementsEmpty;
        private set => SetProperty(ref _isMovementsEmpty, value);
    }

    public bool HasDocumentDetail
    {
        get => _hasDocumentDetail;
        private set => SetProperty(ref _hasDocumentDetail, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public int BalanceCount
    {
        get => _balanceCount;
        private set => SetProperty(ref _balanceCount, value);
    }

    public int LowStockCount
    {
        get => _lowStockCount;
        private set => SetProperty(ref _lowStockCount, value);
    }

    public decimal MaterialStockValue
    {
        get => _materialStockValue;
        private set
        {
            if (SetProperty(ref _materialStockValue, value))
                OnPropertyChanged(nameof(MaterialStockValueText));
        }
    }

    public string MaterialStockValueText => MaterialStockValue.ToString("N0", ViCulture) + " đ";

    public int DocumentCount
    {
        get => _documentCount;
        private set => SetProperty(ref _documentCount, value);
    }

    public int TodayDocumentCount
    {
        get => _todayDocumentCount;
        private set => SetProperty(ref _todayDocumentCount, value);
    }

    public string DetailCode
    {
        get => _detailCode;
        private set => SetProperty(ref _detailCode, value);
    }

    public string DetailDate
    {
        get => _detailDate;
        private set => SetProperty(ref _detailDate, value);
    }

    public string DetailType
    {
        get => _detailType;
        private set => SetProperty(ref _detailType, value);
    }

    public string DetailStatus
    {
        get => _detailStatus;
        private set => SetProperty(ref _detailStatus, value);
    }

    public string DetailReference
    {
        get => _detailReference;
        private set => SetProperty(ref _detailReference, value);
    }

    public string DetailCounterparty
    {
        get => _detailCounterparty;
        private set => SetProperty(ref _detailCounterparty, value);
    }

    public string DetailNotes
    {
        get => _detailNotes;
        private set => SetProperty(ref _detailNotes, value);
    }

    public string DetailCreatedBy
    {
        get => _detailCreatedBy;
        private set => SetProperty(ref _detailCreatedBy, value);
    }

    public string DetailPostedBy
    {
        get => _detailPostedBy;
        private set => SetProperty(ref _detailPostedBy, value);
    }

    public string DetailPostedAt
    {
        get => _detailPostedAt;
        private set => SetProperty(ref _detailPostedAt, value);
    }

    public ICommand SearchBalancesCommand { get; }
    public ICommand SearchDocumentsCommand { get; }
    public ICommand SearchMovementsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ClearBalanceFiltersCommand { get; }
    public ICommand ClearDocumentFiltersCommand { get; }
    public ICommand ClearMovementFiltersCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadLocationsAsync();
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        await SearchBalancesAsync();
        await SearchDocumentsAsync();
        await SearchMovementsAsync();
    }

    private async Task LoadLocationsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        try
        {
            var locations = await _repository.GetLocationsAsync(session);
            var selectedId = SelectedLocation?.Id ?? 0;
            var selectedMovementId = SelectedMovementLocation?.Id ?? 0;

            while (Locations.Count > 1)
                Locations.RemoveAt(Locations.Count - 1);
            while (MovementLocations.Count > 1)
                MovementLocations.RemoveAt(MovementLocations.Count - 1);

            foreach (var location in locations)
            {
                Locations.Add(location);
                MovementLocations.Add(location);
            }

            SelectedLocation = Locations.FirstOrDefault(x => x.Id == selectedId) ?? Locations[0];
            SelectedMovementLocation =
                MovementLocations.FirstOrDefault(x => x.Id == selectedMovementId) ?? MovementLocations[0];
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể tải danh sách vị trí kho.";
        }
    }

    private async Task SearchBalancesAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải tồn nguyên vật liệu...");
        try
        {
            var filter = new MaterialBalanceSearchFilter(
                BalanceSearchText,
                SelectedMaterialType == "Tất cả" ? string.Empty : SelectedMaterialType,
                SelectedLocation?.Id ?? 0,
                LowStockOnly);

            var rows = await _repository.SearchMaterialBalancesAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();

            (long MaterialId, long LocationId)? previousKey = SelectedBalance is null
                ? null
                : (SelectedBalance.MaterialId, SelectedBalance.LocationId);
            Balances.Clear();
            var index = 1;
            foreach (var row in rows)
                Balances.Add(new MaterialBalanceRowViewModel(row, index++));

            SelectedBalance = previousKey is { } key
                ? Balances.FirstOrDefault(x => x.MaterialId == key.MaterialId && x.LocationId == key.LocationId)
                  ?? Balances.FirstOrDefault()
                : Balances.FirstOrDefault();

            IsBalancesEmpty = Balances.Count == 0;
            BalanceCount = Balances.Count;
            LowStockCount = Balances.Count(x => x.IsLowStock);
            MaterialStockValue = Balances.Sum(x => x.StockValue);
            StatusMessage = IsBalancesEmpty
                ? "Không có tồn nguyên vật liệu phù hợp."
                : $"Hiển thị {Balances.Count} dòng tồn NVL.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Balances.Clear();
            SelectedBalance = null;
            IsBalancesEmpty = true;
            BalanceCount = 0;
            LowStockCount = 0;
            MaterialStockValue = 0;
            ErrorMessage = "Không thể tải tồn nguyên vật liệu. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
    }

    private async Task SearchDocumentsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải phiếu kho...");
        try
        {
            var filter = new StockDocumentSearchFilter(
                DocumentSearchText,
                SelectedDocumentType == "Tất cả" ? string.Empty : SelectedDocumentType,
                SelectedDocumentStatus == "Tất cả" ? string.Empty : SelectedDocumentStatus,
                null,
                null);

            var rows = await _repository.SearchDocumentsAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();

            var previousId = SelectedDocument?.Id;
            Documents.Clear();
            var index = 1;
            foreach (var row in rows)
                Documents.Add(new StockDocumentRowViewModel(row, index++));

            _suppressDocumentDetailLoad = true;
            try
            {
                SelectedDocument = previousId is long id
                    ? Documents.FirstOrDefault(x => x.Id == id) ?? Documents.FirstOrDefault()
                    : Documents.FirstOrDefault();
            }
            finally
            {
                _suppressDocumentDetailLoad = false;
            }

            IsDocumentsEmpty = Documents.Count == 0;
            DocumentCount = Documents.Count;
            var today = DateTime.Today;
            TodayDocumentCount = Documents.Count(x => x.DocumentDate.Date == today);
            StatusMessage = IsDocumentsEmpty
                ? "Không có phiếu kho phù hợp."
                : $"Hiển thị {Documents.Count} phiếu kho.";

            await LoadDocumentDetailAsync(SelectedDocument);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Documents.Clear();
            _suppressDocumentDetailLoad = true;
            try
            {
                SelectedDocument = null;
            }
            finally
            {
                _suppressDocumentDetailLoad = false;
            }
            ClearDocumentDetail();
            IsDocumentsEmpty = true;
            DocumentCount = 0;
            TodayDocumentCount = 0;
            ErrorMessage = "Không thể tải danh sách phiếu kho. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
    }

    private async Task LoadDocumentDetailAsync(StockDocumentRowViewModel? row)
    {
        if (row is null)
        {
            ClearDocumentDetail();
            return;
        }

        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải chi tiết phiếu...");
        try
        {
            var detail = await _repository.GetDocumentAsync(row.Id, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            if (detail is null)
            {
                ClearDocumentDetail();
                ErrorMessage = "Không đọc được phiếu kho (sai chi nhánh hoặc đã bị xóa).";
                return;
            }

            var lines = await _repository.GetDocumentLinesAsync(row.Id, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();

            DetailCode = detail.Code;
            DetailDate = detail.DocumentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            DetailType = row.DocumentTypeDisplay;
            DetailStatus = row.StatusDisplay;
            DetailReference = detail.ReferenceNo;
            DetailCounterparty = detail.CounterpartyName;
            DetailNotes = detail.Notes;
            DetailCreatedBy = detail.CreatedByName;
            DetailPostedBy = string.IsNullOrWhiteSpace(detail.PostedByName) ? "—" : detail.PostedByName;
            DetailPostedAt = detail.PostedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";
            HasDocumentDetail = true;

            DocumentLines.Clear();
            foreach (var line in lines)
                DocumentLines.Add(new StockDocumentLineRowViewModel(line));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ClearDocumentDetail();
            ErrorMessage = "Không thể tải chi tiết phiếu kho.";
        }
    }

    private async Task SearchMovementsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải sổ giao dịch...");
        try
        {
            var filter = new StockMovementSearchFilter(
                0,
                SelectedMovementLocation?.Id ?? 0,
                MovementSearchText);

            var rows = await _repository.SearchMovementsAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();

            var previousId = SelectedMovement?.Id;
            Movements.Clear();
            var index = 1;
            foreach (var row in rows)
                Movements.Add(new StockMovementRowViewModel(row, index++));

            SelectedMovement = previousId is long id
                ? Movements.FirstOrDefault(x => x.Id == id) ?? Movements.FirstOrDefault()
                : Movements.FirstOrDefault();

            IsMovementsEmpty = Movements.Count == 0;
            StatusMessage = IsMovementsEmpty
                ? "Không có giao dịch kho phù hợp."
                : $"Hiển thị {Movements.Count} giao dịch kho.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Movements.Clear();
            SelectedMovement = null;
            IsMovementsEmpty = true;
            ErrorMessage = "Không thể tải sổ giao dịch kho. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
    }

    private void ClearBalanceFilters()
    {
        BalanceSearchText = string.Empty;
        SelectedMaterialType = "Tất cả";
        SelectedLocation = Locations.FirstOrDefault();
        LowStockOnly = false;
        _ = SearchBalancesAsync();
    }

    private void ClearDocumentFilters()
    {
        DocumentSearchText = string.Empty;
        SelectedDocumentType = "Tất cả";
        SelectedDocumentStatus = "Tất cả";
        _ = SearchDocumentsAsync();
    }

    private void ClearMovementFilters()
    {
        MovementSearchText = string.Empty;
        SelectedMovementLocation = MovementLocations.FirstOrDefault();
        _ = SearchMovementsAsync();
    }

    private void ClearDocumentDetail()
    {
        HasDocumentDetail = false;
        DetailCode = string.Empty;
        DetailDate = string.Empty;
        DetailType = string.Empty;
        DetailStatus = string.Empty;
        DetailReference = string.Empty;
        DetailCounterparty = string.Empty;
        DetailNotes = string.Empty;
        DetailCreatedBy = string.Empty;
        DetailPostedBy = string.Empty;
        DetailPostedAt = string.Empty;
        DocumentLines.Clear();
    }

    private AuthenticatedSession? RequireSession()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return null;
        }

        return session;
    }

    private LoadScope BeginLoad(string status)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = status;
        return new LoadScope(this, _loadCts);
    }

    private sealed class LoadScope : IDisposable
    {
        private readonly WarehouseViewModel _owner;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        public LoadScope(WarehouseViewModel owner, CancellationTokenSource cts)
        {
            _owner = owner;
            _cts = cts;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_cts.IsCancellationRequested)
                _owner.IsLoading = false;
        }
    }
}
