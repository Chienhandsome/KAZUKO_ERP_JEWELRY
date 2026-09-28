using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class HistoryFilterOption
{
    public HistoryFilterOption(long id, string label)
    {
        Id = id;
        Label = label;
    }

    public long Id { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

public sealed class PriceHistoryRowViewModel
{
    public PriceHistoryRowViewModel(GoldPriceHistoryRecord record)
    {
        ChangedAtText = record.ChangedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        GoldName = record.GoldName;
        UnitName = CleanUnit(record.UnitName);
        OldBuyPrice = record.OldBuyPrice;
        NewBuyPrice = record.NewBuyPrice;
        OldSellPrice = record.OldSellPrice;
        NewSellPrice = record.NewSellPrice;
        ChangedBy = record.ChangedBy;
        Notes = record.Notes;
    }

    public string ChangedAtText { get; }
    public string GoldName { get; }
    public string UnitName { get; }
    public decimal OldBuyPrice { get; }
    public decimal NewBuyPrice { get; }
    public decimal OldSellPrice { get; }
    public decimal NewSellPrice { get; }
    public string ChangedBy { get; }
    public string Notes { get; }

    private static string CleanUnit(string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName))
            return string.Empty;
        var paren = unitName.IndexOf('(');
        return paren > 0 ? unitName[..paren].Trim() : unitName.Trim();
    }
}

public sealed class PriceHistoryDialogViewModel : ObservableObject
{
    private readonly IPriceBoardRepository _repository;
    private HistoryFilterOption? _selectedFilter;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public PriceHistoryDialogViewModel(IPriceBoardRepository repository)
    {
        _repository = repository;
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        Filters.Add(new HistoryFilterOption(0, "--- Tất cả loại vàng ---"));
        SelectedFilter = Filters[0];
        _ = InitializeAsync();
    }

    public event Action? RequestClose;

    public ObservableCollection<HistoryFilterOption> Filters { get; } = new();
    public ObservableCollection<PriceHistoryRowViewModel> Rows { get; } = new();

    public HistoryFilterOption? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
                _ = LoadHistoryAsync();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public ICommand CloseCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            var golds = await _repository.GetGoldPricesAsync();
            foreach (var g in golds)
                Filters.Add(new HistoryFilterOption(g.Id, g.Name));
            await LoadHistoryAsync();
        }
        catch
        {
            ErrorMessage = "Không thể tải bộ lọc lịch sử.";
        }
    }

    private async Task LoadHistoryAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            long? id = SelectedFilter is { Id: > 0 } ? SelectedFilter.Id : null;
            var list = await _repository.GetPriceHistoryAsync(id, 200);
            Rows.Clear();
            foreach (var item in list)
                Rows.Add(new PriceHistoryRowViewModel(item));
        }
        catch
        {
            ErrorMessage = "Không thể tải lịch sử biến động giá.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
