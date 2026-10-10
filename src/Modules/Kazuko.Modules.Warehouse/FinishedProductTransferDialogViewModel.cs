using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class FinishedProductTransferDialogViewModel : ObservableObject
{
    private readonly IFinishedProductTransferService _service;
    private readonly IWarehouseRepository _warehouse;
    private readonly AuthenticatedSession _session;
    private readonly List<FinishedProductStockItem> _allCandidates = new();
    private WarehouseLocationOption? _sourceLocation;
    private WarehouseLocationOption? _destinationLocation;
    private FinishedProductStockItem? _selectedCandidate;
    private FinishedProductStockItem? _selectedLine;
    private string _searchText = string.Empty;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public FinishedProductTransferDialogViewModel(
        IFinishedProductTransferService service,
        IWarehouseRepository warehouse,
        AuthenticatedSession session)
    {
        _service = service;
        _warehouse = warehouse;
        _session = session;
        AddProductCommand = new RelayCommand(AddProduct);
        RemoveProductCommand = new RelayCommand(RemoveProduct, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadAsync();
    }

    public ObservableCollection<WarehouseLocationOption> SourceLocations { get; } = new();
    public ObservableCollection<WarehouseLocationOption> DestinationLocations { get; } = new();
    public ObservableCollection<FinishedProductStockItem> Candidates { get; } = new();
    public ObservableCollection<FinishedProductStockItem> Lines { get; } = new();

    public WarehouseLocationOption? SourceLocation
    {
        get => _sourceLocation;
        set
        {
            if (SetProperty(ref _sourceLocation, value))
            {
                Lines.Clear();
                ApplyCandidateFilter();
            }
        }
    }

    public WarehouseLocationOption? DestinationLocation
    {
        get => _destinationLocation;
        set => SetProperty(ref _destinationLocation, value);
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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyCandidateFilter();
        }
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
            if (location.InventoryScope == InventoryScopes.FinishedGoods)
                SourceLocations.Add(location);
            if (location.InventoryScope == InventoryScopes.SalesFloor)
                DestinationLocations.Add(location);
        }
        SourceLocation = SourceLocations.FirstOrDefault();
        DestinationLocation = DestinationLocations.FirstOrDefault();

        var items = await _warehouse.SearchFinishedProductStockAsync(
            new FinishedProductSearchFilter(string.Empty, string.Empty, string.Empty, "IN_STOCK"), _session);
        _allCandidates.AddRange(items);
        ApplyCandidateFilter();
    }

    private void ApplyCandidateFilter()
    {
        Candidates.Clear();
        if (SourceLocation is null)
        {
            SelectedCandidate = null;
            return;
        }

        var search = SearchText.Trim();
        foreach (var item in _allCandidates.Where(x =>
                     x.LocationId == SourceLocation.Id
                     && Lines.All(selected => selected.Id != x.Id)
                     && (search.Length == 0
                         || x.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                         || x.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase)
                         || x.GoldPurity.Contains(search, StringComparison.OrdinalIgnoreCase))))
        {
            Candidates.Add(item);
        }

        SelectedCandidate = Candidates.FirstOrDefault();
    }

    private void AddProduct()
    {
        if (SelectedCandidate is null)
        {
            ErrorMessage = "Chọn một thành phẩm.";
            return;
        }
        if (Lines.Any(x => x.Id == SelectedCandidate.Id))
        {
            ErrorMessage = "Serial này đã có trên phiếu.";
            return;
        }

        Lines.Add(SelectedCandidate);
        ApplyCandidateFilter();
        ErrorMessage = string.Empty;
    }

    private void RemoveProduct()
    {
        if (SelectedLine is not null)
            Lines.Remove(SelectedLine);
        SelectedLine = null;
        ApplyCandidateFilter();
    }

    private async Task SaveAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            if (SourceLocation is null || DestinationLocation is null)
                throw new InvalidOperationException("Chọn Kho thành phẩm và Kho bán hàng.");

            await _service.CreateAndPostAsync(
                new FinishedProductTransferDraft(
                    DateTime.Now, SourceLocation.Id, DestinationLocation.Id,
                    Notes, Lines.Select(x => x.Id).ToList()),
                _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Nhập từ kho thành phẩm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
