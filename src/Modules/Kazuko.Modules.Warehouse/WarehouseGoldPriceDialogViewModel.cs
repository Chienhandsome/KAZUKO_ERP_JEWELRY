using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class WarehouseGoldPriceDialogViewModel : ObservableObject
{
    private readonly IWarehouseGoldPriceRepository _repository;
    private readonly AuthenticatedSession _session;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public WarehouseGoldPriceDialogViewModel(
        IWarehouseGoldPriceRepository repository,
        AuthenticatedSession session)
    {
        _repository = repository;
        _session = session;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadAsync();
    }

    public ObservableCollection<WarehouseGoldPriceRowViewModel> Prices { get; } = new();

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

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task LoadAsync()
    {
        try
        {
            foreach (var item in await _repository.GetAsync(_session))
                Prices.Add(new WarehouseGoldPriceRowViewModel(item));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            await _repository.SaveAsync(
                Prices.Select(x => new WarehouseGoldPriceUpdate(x.Id, x.IssuePricePerChi, x.RowVersion)).ToList(),
                Notes,
                _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Bảng giá vàng kho", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public sealed class WarehouseGoldPriceRowViewModel : ObservableObject
{
    private decimal _issuePricePerChi;

    public WarehouseGoldPriceRowViewModel(WarehouseGoldPriceItem item)
    {
        Id = item.Id;
        GoldCode = item.GoldCode;
        GoldName = item.GoldName;
        PurityRate = item.PurityRate;
        _issuePricePerChi = item.IssuePricePerChi;
        RetailBuyPricePerChi = item.RetailBuyPricePerChi;
        RetailSellPricePerChi = item.RetailSellPricePerChi;
        RowVersion = item.RowVersion;
        UpdatedAt = item.UpdatedAt;
    }

    public long Id { get; }
    public string GoldCode { get; }
    public string GoldName { get; }
    public decimal PurityRate { get; }
    public decimal RetailBuyPricePerChi { get; }
    public decimal RetailSellPricePerChi { get; }
    public long RowVersion { get; }
    public DateTime UpdatedAt { get; }

    public decimal IssuePricePerChi
    {
        get => _issuePricePerChi;
        set => SetProperty(ref _issuePricePerChi, value);
    }
}
