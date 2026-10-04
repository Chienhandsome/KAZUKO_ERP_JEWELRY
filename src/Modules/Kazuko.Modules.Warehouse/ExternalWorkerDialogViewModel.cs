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
    private bool _activeOnly = true;
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

    public ExternalWorkerDialogViewModel(
        IExternalWorkerRepository workers,
        IOutsourcedCraftService craft,
        AuthenticatedSession session)
    {
        _workers = workers;
        _craft = craft;
        _session = session;

        SearchCommand = new AsyncRelayCommand(LoadAsync);
        NewCommand = new RelayCommand(BeginNew);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CloseCommand = new RelayCommand(() => DialogResult = false);
        _ = InitializeAsync();
    }

    public ExternalWorkerListItem? LastSaved => _lastSaved;

    public ObservableCollection<ExternalWorkerRowViewModel> Workers { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
                _ = LoadAsync();
        }
    }

    public ExternalWorkerRowViewModel? SelectedWorker
    {
        get => _selectedWorker;
        set
        {
            if (!SetProperty(ref _selectedWorker, value) || _suppressSelection)
                return;
            if (value is null)
                return;
            LoadEditor(value);
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

    public ICommand SearchCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadAsync();
        await LoadHoldingsSummaryAsync();
        BeginNew();
    }

    private async Task LoadAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var rows = await _workers.SearchAsync(SearchText, ActiveOnly, _session);
            _suppressSelection = true;
            Workers.Clear();
            foreach (var row in rows)
                Workers.Add(new ExternalWorkerRowViewModel(row));
            _suppressSelection = false;
            StatusMessage = $"Có {Workers.Count} thợ ngoài.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
        StatusMessage = "Nhập thợ mới rồi bấm Lưu.";
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
        StatusMessage = $"Đang sửa {row.Code}.";
    }

    private async Task SaveAsync()
    {
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
            await LoadAsync();
            _suppressSelection = true;
            SelectedWorker = Workers.FirstOrDefault(w => w.Id == saved.Id);
            _suppressSelection = false;
            if (SelectedWorker is not null)
                LoadEditor(SelectedWorker);

            StatusMessage = $"Đã lưu thợ {saved.Code}.";
            MessageBox.Show(
                $"Đã lưu thợ ngoài {saved.Code} — {saved.Name}.\nCó thể chọn trong lệnh sản xuất OUTSOURCED.",
                "Thợ ngoài",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Thợ ngoài", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
