using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class FinishedGoodsIssueDialogViewModel : ObservableObject
{
    private readonly IFinishedGoodsIssueService _service;
    private readonly IWarehouseRepository _warehouse;
    private readonly AuthenticatedSession _session;
    private readonly string _preferredScope;
    private readonly List<FinishedProductStockItem> _allCandidates = new();
    private WarehouseSalesOrderOption? _selectedOrder;
    private WarehouseLocationOption? _sourceLocation;
    private FinishedProductStockItem? _selectedCandidate;
    private FinishedProductStockItem? _selectedLine;
    private string _deliveredByName = string.Empty;
    private string _receivedByName = string.Empty;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public FinishedGoodsIssueDialogViewModel(
        IFinishedGoodsIssueService service,
        IWarehouseRepository warehouse,
        AuthenticatedSession session,
        string preferredScope)
    {
        _service = service;
        _warehouse = warehouse;
        _session = session;
        _preferredScope = preferredScope;
        AddProductCommand = new RelayCommand(AddProduct);
        RemoveProductCommand = new RelayCommand(RemoveProduct, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadAsync();
    }

    public ObservableCollection<WarehouseSalesOrderOption> Orders { get; } = new();
    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<FinishedProductStockItem> Candidates { get; } = new();
    public ObservableCollection<FinishedProductStockItem> Lines { get; } = new();

    public WarehouseSalesOrderOption? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
                _ = LoadCandidatesAsync();
        }
    }

    public WarehouseLocationOption? SourceLocation
    {
        get => _sourceLocation;
        set
        {
            if (SetProperty(ref _sourceLocation, value))
                ApplyCandidateFilter();
        }
    }

    public FinishedProductStockItem? SelectedCandidate
    {
        get => _selectedCandidate;
        set => SetProperty(ref _selectedCandidate, value);
    }

    public FinishedProductStockItem? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
                (RemoveProductCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string DeliveredByName
    {
        get => _deliveredByName;
        set => SetProperty(ref _deliveredByName, value);
    }

    public string ReceivedByName
    {
        get => _receivedByName;
        set => SetProperty(ref _receivedByName, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        set => SetProperty(ref _dialogResult, value);
    }

    public ICommand AddProductCommand { get; }
    public ICommand RemoveProductCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task LoadAsync()
    {
        foreach (var location in await _warehouse.GetLocationsAsync(_session))
        {
            // Chỉ cho xuất bán từ Kho bán hàng; không xuất thẳng từ Kho thành phẩm.
            if (location.InventoryScope == InventoryScopes.SalesFloor)
                Locations.Add(location);
        }
        SourceLocation = Locations.FirstOrDefault(x => x.InventoryScope == _preferredScope)
            ?? Locations.FirstOrDefault();

        foreach (var order in await _warehouse.SearchIssueSalesOrdersAsync(_session))
            Orders.Add(order);
        SelectedOrder = Orders.FirstOrDefault();
    }

    private async Task LoadCandidatesAsync()
    {
        _allCandidates.Clear();
        Lines.Clear();
        if (SelectedOrder is not null)
        {
            var rows = await _warehouse.SearchIssueCandidatesAsync(SelectedOrder.Id, _session);
            _allCandidates.AddRange(rows);
        }
        ApplyCandidateFilter();
    }

    private void ApplyCandidateFilter()
    {
        Candidates.Clear();
        if (SourceLocation is not null)
        {
            foreach (var item in _allCandidates.Where(x => x.LocationId == SourceLocation.Id))
                Candidates.Add(item);
        }
        SelectedCandidate = Candidates.FirstOrDefault();
    }

    private void AddProduct()
    {
        if (SelectedCandidate is null)
        {
            ErrorMessage = "Đơn hàng không có thành phẩm phù hợp tại kho đã chọn.";
            return;
        }
        if (Lines.Any(x => x.Id == SelectedCandidate.Id))
        {
            ErrorMessage = "Serial này đã có trên phiếu.";
            return;
        }
        Lines.Add(SelectedCandidate);
        ErrorMessage = string.Empty;
    }

    private void RemoveProduct()
    {
        if (SelectedLine is not null)
            Lines.Remove(SelectedLine);
        SelectedLine = null;
    }

    private async Task SaveAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            if (SelectedOrder is null)
                throw new InvalidOperationException("Chọn đơn bán đã xác nhận.");
            if (SourceLocation is null)
                throw new InvalidOperationException("Chọn kho/vị trí xuất.");

            await _service.CreateAndPostAsync(
                new FinishedGoodsIssueDraft(
                    SelectedOrder.Id, DateTime.Now, SourceLocation.Id,
                    DeliveredByName, ReceivedByName, Notes,
                    Lines.Select(x => new FinishedGoodsIssueLineDraft(x.Id)).ToList()),
                _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Xuất thành phẩm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
