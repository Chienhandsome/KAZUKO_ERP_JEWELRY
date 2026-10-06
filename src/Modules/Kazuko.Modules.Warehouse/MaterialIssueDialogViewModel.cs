using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class MaterialIssueDialogViewModel : ObservableObject
{
    private readonly IMaterialIssueService _service;
    private readonly IWarehouseRepository _warehouse;
    private readonly AuthenticatedSession _session;
    private WarehouseLocationOption? _selectedLocation;
    private MaterialOption? _selectedMaterial;
    private MaterialIssueLineRow? _selectedLine;
    private decimal _lineQuantity = 1m;
    private string _receivedByName = string.Empty;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public MaterialIssueDialogViewModel(
        IMaterialIssueService service,
        IWarehouseRepository warehouse,
        AuthenticatedSession session)
    {
        _service = service;
        _warehouse = warehouse;
        _session = session;
        AddLineCommand = new RelayCommand(AddLine);
        RemoveLineCommand = new RelayCommand(RemoveLine, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadAsync();
    }

    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<MaterialOption> Materials { get; } = new();
    public ObservableCollection<MaterialIssueLineRow> Lines { get; } = new();

    public WarehouseLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set => SetProperty(ref _selectedLocation, value);
    }

    public MaterialOption? SelectedMaterial
    {
        get => _selectedMaterial;
        set => SetProperty(ref _selectedMaterial, value);
    }

    public MaterialIssueLineRow? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
                (RemoveLineCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public decimal LineQuantity
    {
        get => _lineQuantity;
        set => SetProperty(ref _lineQuantity, value);
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

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task LoadAsync()
    {
        foreach (var location in await _warehouse.GetLocationsAsync(_session))
        {
            if (location.InventoryScope is InventoryScopes.RawMaterial)
                Locations.Add(location);
        }
        SelectedLocation = Locations.FirstOrDefault();

        foreach (var material in await _warehouse.SearchMaterialsAsync(string.Empty, string.Empty, _session))
            Materials.Add(material);
        SelectedMaterial = Materials.FirstOrDefault();
    }

    private void AddLine()
    {
        if (SelectedMaterial is null || LineQuantity <= 0)
        {
            ErrorMessage = "Chọn NVL và nhập số lượng lớn hơn 0.";
            return;
        }
        if (Lines.Any(x => x.MaterialId == SelectedMaterial.Id))
        {
            ErrorMessage = "NVL này đã có trên phiếu.";
            return;
        }

        Lines.Add(new MaterialIssueLineRow(
            SelectedMaterial.Id, SelectedMaterial.Code, SelectedMaterial.Name,
            SelectedMaterial.UnitCode, LineQuantity));
        ErrorMessage = string.Empty;
    }

    private void RemoveLine()
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
            if (SelectedLocation is null)
                throw new InvalidOperationException("Chọn kho xuất.");

            await _service.CreateAndPostAsync(
                new MaterialIssueDraft(
                    DateTime.Now, SelectedLocation.Id, ReceivedByName, Notes,
                    Lines.Select(x => new MaterialIssueLineDraft(x.MaterialId, x.Quantity, null)).ToList()),
                _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Xuất kho NVL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public sealed record MaterialIssueLineRow(
    long MaterialId,
    string Code,
    string Name,
    string UnitCode,
    decimal Quantity);
