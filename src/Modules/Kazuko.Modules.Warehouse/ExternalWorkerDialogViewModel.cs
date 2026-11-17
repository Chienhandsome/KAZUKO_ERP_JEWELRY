using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class ExternalWorkerDialogViewModel : ObservableObject
{
    private readonly IExternalWorkerRepository _workers;
    private readonly IOutsourcedCraftService _craft;
    private readonly AuthenticatedSession _session;

    private string _searchText = string.Empty;
    private bool _activeOnly;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _holdingsSummary = string.Empty;
    private ExternalWorkerRowViewModel? _selectedWorker;
    private long? _editId;
    private string _editCode = string.Empty;
    private string _editName = string.Empty;
    private string _editPhone = string.Empty;
    private string _editAddress = string.Empty;
    private string _editIdTaxNumber = string.Empty;
    private string _editPaymentInfo = string.Empty;
    private string _editNotes = string.Empty;
    private bool _editIsActive = true;
    private bool? _dialogResult;
    private bool _suppressSelection;
    private ExternalWorkerListItem? _lastSaved;
    private IReadOnlyList<ExternalWorkerListItem> _allWorkers = [];
    private bool _isEditing;
    private bool _isBusy;
    private readonly long? _initialId;

    public ExternalWorkerDialogViewModel(
        IExternalWorkerRepository workers,
        IOutsourcedCraftService craft,
        AuthenticatedSession session,
        bool canChoose = false,
        long? initialId = null)
    {
        _workers = workers;
        _craft = craft;
        _session = session;
        CanChoose = canChoose;
        _initialId = initialId;
        _activeOnly = canChoose;
        NewCommand = new RelayCommand(BeginNew, () => CanBrowse);
        EditCommand = new RelayCommand(() => IsEditing = true, () => CanBrowse && SelectedWorker is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsEditing && !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeactivateAsync, () => CanBrowse && SelectedWorker?.IsActive == true);
        CancelEditCommand = new RelayCommand(EndEdit, () => IsEditing && !IsBusy);
        ChooseCommand = new RelayCommand(() => DialogResult = true, () => CanChoose && CanBrowse && SelectedWorker?.IsActive == true);
        CloseCommand = new RelayCommand(() => DialogResult = false);
        _ = InitializeAsync();
    }

    public ExternalWorkerListItem? LastSaved => _lastSaved;
    public bool HasChanges { get; private set; }
    public bool CanChoose { get; }
    public bool CanBrowse => !IsEditing && !IsBusy;
    public bool CanEditFields => IsEditing && !IsBusy;
    public bool AreFieldsReadOnly => !CanEditFields;
    public bool CanReactivate => CanEditFields && SelectedWorker?.IsActive == false;
    public bool IsEditing
    {
        get => _isEditing;
        private set { if (SetProperty(ref _isEditing, value)) UpdateActions(); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) UpdateActions(); }
    }

    public ObservableCollection<ExternalWorkerRowViewModel> Workers { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter(SelectedWorker?.Id);
        }
    }

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
                ApplyFilter(SelectedWorker?.Id);
        }
    }

    public ExternalWorkerRowViewModel? SelectedWorker
    {
        get => _selectedWorker;
        set
        {
            if (!SetProperty(ref _selectedWorker, value) || _suppressSelection)
                return;
            if (value is not null)
                LoadEditor(value);
            else
                ClearEditor();
            UpdateActions();
        }
    }

    public string EditCode
    {
        get => _editCode;
        set => SetProperty(ref _editCode, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditPhone
    {
        get => _editPhone;
        set => SetProperty(ref _editPhone, value);
    }

    public string EditAddress
    {
        get => _editAddress;
        set => SetProperty(ref _editAddress, value);
    }

    public string EditIdTaxNumber
    {
        get => _editIdTaxNumber;
        set => SetProperty(ref _editIdTaxNumber, value);
    }

    public string EditPaymentInfo
    {
        get => _editPaymentInfo;
        set => SetProperty(ref _editPaymentInfo, value);
    }

    public string EditNotes
    {
        get => _editNotes;
        set => SetProperty(ref _editNotes, value);
    }

    public bool EditIsActive
    {
        get => _editIsActive;
        set => SetProperty(ref _editIsActive, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string HoldingsSummary
    {
        get => _holdingsSummary;
        set => SetProperty(ref _holdingsSummary, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        set => SetProperty(ref _dialogResult, value);
    }

    public RelayCommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand ChooseCommand { get; }
    public ICommand CloseCommand { get; }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await LoadAsync(_initialId);
            await LoadHoldingsSummaryAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync(long? selectedId)
    {
        try
        {
            ErrorMessage = string.Empty;
            _allWorkers = await _workers.SearchAsync(string.Empty, false, _session);
            ApplyFilter(selectedId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ApplyFilter(long? selectedId)
    {
        var query = SearchText.Trim();
        _suppressSelection = true;
        Workers.Clear();
        foreach (var row in _allWorkers.Where(row => (!ActiveOnly || row.IsActive) &&
            (row.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.IdTaxNumber.Contains(query, StringComparison.OrdinalIgnoreCase))))
            Workers.Add(new ExternalWorkerRowViewModel(row));
        _suppressSelection = false;
        SelectedWorker = Workers.FirstOrDefault(worker => worker.Id == selectedId) ?? Workers.FirstOrDefault();
        if (SelectedWorker is null)
            ClearEditor();
        UpdateActions();
        StatusMessage = $"Có {Workers.Count} thợ ngoài.";
    }

    private async Task LoadHoldingsSummaryAsync()
    {
        try
        {
            var holdings = await _craft.SearchHoldingsAsync(null, string.Empty, _session);
            HoldingsSummary = holdings.Count == 0
                ? "NVL đang giao thợ (luồng kho cũ): chưa có."
                : $"NVL đang giao thợ (luồng kho cũ): {holdings.Count} dòng — "
                  + string.Join("; ", holdings.Take(5).Select(h =>
                      $"{h.WorkerName}/{h.MaterialCode} còn {h.QuantityOutstanding:0.####}"));
        }
        catch
        {
            HoldingsSummary = "Không tải được tóm tắt NVL đang giao thợ.";
        }
    }

    private void BeginNew()
    {
        _suppressSelection = true;
        SelectedWorker = null;
        _suppressSelection = false;
        ClearEditor();
        IsEditing = true;
        StatusMessage = "Nhập thợ mới rồi bấm Lưu.";
    }

    private void ClearEditor()
    {
        _editId = null;
        EditCode = string.Empty;
        EditName = string.Empty;
        EditPhone = string.Empty;
        EditAddress = string.Empty;
        EditIdTaxNumber = string.Empty;
        EditPaymentInfo = string.Empty;
        EditNotes = string.Empty;
        EditIsActive = true;
        ErrorMessage = string.Empty;
    }

    private void LoadEditor(ExternalWorkerRowViewModel row)
    {
        _editId = row.Id;
        EditCode = row.Code;
        EditName = row.Name;
        EditPhone = row.Phone;
        EditAddress = row.Address;
        EditIdTaxNumber = row.IdTaxNumber;
        EditPaymentInfo = row.PaymentInfo;
        EditNotes = row.Notes;
        EditIsActive = row.IsActive;
        ErrorMessage = string.Empty;
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            ErrorMessage = string.Empty;
            var saved = await _workers.SaveAsync(
                new ExternalWorkerSaveRequest(
                    _editId,
                    EditCode,
                    EditName,
                    EditPhone,
                    EditAddress,
                    EditIdTaxNumber,
                    EditPaymentInfo,
                    EditNotes,
                    EditIsActive),
                _session);

            _lastSaved = saved;
            HasChanges = true;
            IsEditing = false;
            SearchText = string.Empty;
            await LoadAsync(saved.Id);
            StatusMessage = $"Đã lưu thợ {saved.Code}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task DeactivateAsync()
    {
        if (SelectedWorker is not { IsActive: true } worker ||
            MessageBox.Show($"Ngừng hoạt động thợ {worker.Code} – {worker.Name}?", "Thợ ngoài",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _workers.DeactivateAsync(worker.Id, _session);
            HasChanges = true;
            _allWorkers = _allWorkers.Select(row => row.Id == worker.Id ? row with { IsActive = false } : row).ToArray();
            ApplyFilter(worker.Id);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void EndEdit()
    {
        IsEditing = false;
        if (SelectedWorker is { } worker)
            LoadEditor(worker);
        else
            ClearEditor();
    }

    private void UpdateActions()
    {
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(AreFieldsReadOnly));
        OnPropertyChanged(nameof(CanReactivate));
        NewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
        ChooseCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ExternalWorkerRowViewModel
{
    public ExternalWorkerRowViewModel(ExternalWorkerListItem item)
    {
        Id = item.Id;
        Code = item.Code;
        Name = item.Name;
        Phone = item.Phone;
        Address = item.Address;
        IdTaxNumber = item.IdTaxNumber;
        PaymentInfo = item.PaymentInfo;
        Notes = item.Notes;
        IsActive = item.IsActive;
    }

    public long Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string Phone { get; }
    public string Address { get; }
    public string IdTaxNumber { get; }
    public string PaymentInfo { get; }
    public string Notes { get; }
    public bool IsActive { get; }
    public string StatusDisplay => IsActive ? "Hoạt động" : "Ngừng";
}
