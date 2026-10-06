using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class GoldPriceRowViewModel : ObservableObject
{
    private decimal _buyPrice;
    private decimal _sellPrice;
    private string _updatedAtDisplay = string.Empty;

    public GoldPriceRowViewModel(GoldPriceRecord record, string unitCode, int index)
    {
        Id = record.Id;
        Code = record.Code;
        Name = record.Name;
        Index = index;
        _buyPrice = record.DisplayBuyPrice(unitCode);
        _sellPrice = record.DisplaySellPrice(unitCode);
        _updatedAtDisplay = record.UpdatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public long Id { get; }
    public string Code { get; }
    public string Name { get; }
    public int Index { get; }

    public decimal BuyPrice
    {
        get => _buyPrice;
        set => SetProperty(ref _buyPrice, value);
    }

    public decimal SellPrice
    {
        get => _sellPrice;
        set => SetProperty(ref _sellPrice, value);
    }

    public string UpdatedAtDisplay
    {
        get => _updatedAtDisplay;
        set => SetProperty(ref _updatedAtDisplay, value);
    }
}

public sealed class CurrencyRateRowViewModel : ObservableObject
{
    private decimal _buyRate;
    private decimal _sellRate;
    private string _updatedAtDisplay = string.Empty;

    public CurrencyRateRowViewModel(CurrencyRateRecord record, int index)
    {
        Id = record.Id;
        CurrencyCode = record.CurrencyCode;
        CurrencyName = record.CurrencyName;
        Index = index;
        _buyRate = record.BuyRate;
        _sellRate = record.SellRate;
        _updatedAtDisplay = record.UpdatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public long Id { get; }
    public string CurrencyCode { get; }
    public string CurrencyName { get; }
    public int Index { get; }

    public decimal BuyRate
    {
        get => _buyRate;
        set => SetProperty(ref _buyRate, value);
    }

    public decimal SellRate
    {
        get => _sellRate;
        set => SetProperty(ref _sellRate, value);
    }

    public string UpdatedAtDisplay
    {
        get => _updatedAtDisplay;
        set => SetProperty(ref _updatedAtDisplay, value);
    }
}

public sealed class PriceBoardDialogViewModel : ObservableObject
{
    private readonly IPriceBoardRepository _repository;
    private readonly IGoldTypeCatalogService _goldTypes;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly PriceBoardDialogService _dialogs;
    private readonly string _unitCode = GoldUnitConverter.DefaultUnitCode;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private GoldPriceRowViewModel? _selectedGold;
    private CurrencyRateRowViewModel? _selectedCurrency;

    public PriceBoardDialogViewModel(
        IPriceBoardRepository repository,
        IGoldTypeCatalogService goldTypes,
        ICurrentSessionAccessor sessionAccessor,
        PriceBoardDialogService dialogs)
    {
        _repository = repository;
        _goldTypes = goldTypes;
        _sessionAccessor = sessionAccessor;
        _dialogs = dialogs;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(), () => !IsBusy);
        HistoryCommand = new RelayCommand(OpenHistory, () => !IsBusy);
        AddGoldCommand = new AsyncRelayCommand(AddGoldAsync, () => !IsBusy);
        DeleteGoldCommand = new AsyncRelayCommand(DeleteGoldAsync, () => !IsBusy && SelectedGold is not null);
        AddCurrencyCommand = new AsyncRelayCommand(AddCurrencyAsync, () => !IsBusy);
        DeleteCurrencyCommand = new AsyncRelayCommand(DeleteCurrencyAsync, () => !IsBusy && SelectedCurrency is not null);

        _ = LoadAsync();
    }

    public event Action? RequestClose;

    public ObservableCollection<GoldPriceRowViewModel> GoldPrices { get; } = new();
    public ObservableCollection<CurrencyRateRowViewModel> Currencies { get; } = new();

    public string UnitLabel => "VND";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CloseCommand).NotifyCanExecuteChanged();
                ((RelayCommand)HistoryCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddGoldCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteGoldCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddCurrencyCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteCurrencyCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public GoldPriceRowViewModel? SelectedGold
    {
        get => _selectedGold;
        set
        {
            if (SetProperty(ref _selectedGold, value))
                ((AsyncRelayCommand)DeleteGoldCommand).NotifyCanExecuteChanged();
        }
    }

    public CurrencyRateRowViewModel? SelectedCurrency
    {
        get => _selectedCurrency;
        set
        {
            if (SetProperty(ref _selectedCurrency, value))
                ((AsyncRelayCommand)DeleteCurrencyCommand).NotifyCanExecuteChanged();
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand AddGoldCommand { get; }
    public ICommand DeleteGoldCommand { get; }
    public ICommand AddCurrencyCommand { get; }
    public ICommand DeleteCurrencyCommand { get; }

    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var gold = await _repository.GetGoldPricesAsync();
            var currency = await _repository.GetCurrencyRatesAsync();

            GoldPrices.Clear();
            var gi = 1;
            foreach (var item in gold)
                GoldPrices.Add(new GoldPriceRowViewModel(item, _unitCode, gi++));

            Currencies.Clear();
            var ci = 1;
            foreach (var item in currency)
                Currencies.Add(new CurrencyRateRowViewModel(item, ci++));
        }
        catch
        {
            StatusMessage = "Không thể tải bảng giá. Kiểm tra kết nối dữ liệu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            StatusMessage = "Phiên đăng nhập đã hết hạn.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var goldUpdates = GoldPrices
                .Select(r => new GoldPriceUpdateEntry(r.Id, r.Code, r.Name, r.BuyPrice, r.SellPrice, _unitCode))
                .ToList();
            await _repository.SaveGoldPricesAsync(
                goldUpdates,
                $"Cập nhật theo đơn vị {GoldUnitConverter.GetUnit(_unitCode).Name}",
                session);

            var currencyUpdates = Currencies
                .Select(r => new CurrencyRateRecord(
                    r.Id, r.CurrencyCode, r.CurrencyName, r.BuyRate, r.SellRate, true, session.DisplayName, DateTime.Now))
                .ToList();
            await _repository.SaveCurrencyRatesAsync(currencyUpdates, session);

            MessageBox.Show(
                "Đã cập nhật bảng giá vàng và tỉ giá ngoại tệ thành công!\nGiá vốn nguyên vật liệu liên quan đã được đồng bộ.",
                "CẬP NHẬT THÀNH CÔNG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadAsync();
        }
        catch
        {
            StatusMessage = "Không thể lưu bảng giá. Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenHistory()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        _dialogs.ShowHistory(owner);
    }

    private async Task AddGoldAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
            return;

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (_dialogs.ShowAddGold(owner, _unitCode, out var result) != true || result is null)
            return;

        IsBusy = true;
        try
        {
            var buyChi = GoldUnitConverter.ConvertToChi(result.BuyPrice, _unitCode);
            var sellChi = GoldUnitConverter.ConvertToChi(result.SellPrice, _unitCode);
            var issueChi = GoldUnitConverter.ConvertToChi(result.IssuePrice, _unitCode);
            await _goldTypes.CreateAsync(
                new GoldTypeUpsertRequest(
                    result.Code,
                    result.Name,
                    result.PurityRate,
                    buyChi,
                    sellChi,
                    issueChi),
                session);
            await LoadAsync();
            StatusMessage = $"Đã thêm loại vàng {result.Code} (giá lẻ + giá kho + NVL).";
        }
        catch (Exception ex)
        {
            StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Không thể thêm loại vàng."
                : ex.Message;
            IsBusy = false;
        }
    }

    private async Task DeleteGoldAsync()
    {
        if (SelectedGold is null)
            return;
        var session = _sessionAccessor.Current;
        if (session is null)
            return;

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn ngưng dùng loại vàng '{SelectedGold.Name}' (Mã: {SelectedGold.Code})?\n\nSẽ ẩn khỏi bảng giá lẻ, giá kho và danh mục NVL. Chứng từ cũ giữ nguyên.",
            "XÁC NHẬN NGƯNG DÙNG LOẠI VÀNG",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var types = await _goldTypes.ListAsync(activeOnly: true);
            var type = types.FirstOrDefault(t =>
                string.Equals(t.Code, SelectedGold.Code, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                throw new InvalidOperationException("Không tìm thấy loại vàng trong danh mục GOLD_TYPES.");

            await _goldTypes.DeactivateAsync(type.Id, session);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Không thể ngưng dùng loại vàng."
                : ex.Message;
            IsBusy = false;
        }
    }

    private async Task AddCurrencyAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
            return;

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (_dialogs.ShowAddCurrency(owner, out var result) != true || result is null)
            return;

        IsBusy = true;
        try
        {
            await _repository.AddCurrencyRateAsync(result.Code, result.Name, result.BuyRate, result.SellRate, session);
            await LoadAsync();
        }
        catch
        {
            StatusMessage = "Không thể thêm ngoại tệ.";
            IsBusy = false;
        }
    }

    private async Task DeleteCurrencyAsync()
    {
        if (SelectedCurrency is null)
            return;
        var session = _sessionAccessor.Current;
        if (session is null)
            return;

        if (string.Equals(SelectedCurrency.CurrencyCode, "VND", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Không thể xóa đồng tiền mặc định (VND).", "KHÔNG THỂ XÓA",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa ngoại tệ '{SelectedCurrency.CurrencyCode} - {SelectedCurrency.CurrencyName}' khỏi bảng tỉ giá?",
            "XÁC NHẬN XÓA NGOẠI TỆ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _repository.DeleteCurrencyRateAsync(SelectedCurrency.Id, session);
            await LoadAsync();
        }
        catch
        {
            StatusMessage = "Không thể xóa ngoại tệ.";
            IsBusy = false;
        }
    }
}
