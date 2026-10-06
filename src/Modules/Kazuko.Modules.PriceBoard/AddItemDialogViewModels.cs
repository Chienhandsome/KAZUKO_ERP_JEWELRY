using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class AddGoldTypeDialogViewModel : ObservableObject
{
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _buyText = "0";
    private string _sellText = "0";
    private string _issueText = "0";
    private string _error = string.Empty;

    public AddGoldTypeDialogViewModel(string unitCode)
    {
        UnitName = GoldUnitConverter.GetUnit(unitCode).Name;
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null));
    }

    public event Action<AddGoldTypeResult?>? RequestClose;

    public string UnitName { get; }
    public string BuyLabel => $"Giá mua lẻ ({UnitName}) *";
    public string SellLabel => $"Giá bán lẻ ({UnitName}) *";
    public string IssueLabel => $"Giá kho / chỉ ({UnitName}) *";

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BuyText
    {
        get => _buyText;
        set => SetProperty(ref _buyText, value);
    }

    public string SellText
    {
        get => _sellText;
        set => SetProperty(ref _sellText, value);
    }

    public string IssueText
    {
        get => _issueText;
        set => SetProperty(ref _issueText, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private void Save()
    {
        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(Code))
        {
            Error = "Vui lòng nhập mã loại vàng (ví dụ 680, 710...).";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Vui lòng nhập tên loại vàng.";
            return;
        }

        var purity = GoldTypePurity.InferFromCode(Code);
        if (purity <= 0 || purity > 1)
        {
            Error = "Không suy ra được tuổi vàng từ mã (ví dụ 750, 9999).";
            return;
        }

        if (!decimal.TryParse(BuyText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var buy) || buy <= 0
            || !decimal.TryParse(SellText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var sell) || sell <= 0)
        {
            Error = "Giá mua/bán lẻ phải lớn hơn 0.";
            return;
        }

        if (!decimal.TryParse(IssueText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var issue) || issue <= 0)
        {
            Error = "Giá kho / chỉ phải lớn hơn 0.";
            return;
        }

        RequestClose?.Invoke(new AddGoldTypeResult(Code.Trim(), Name.Trim(), purity, buy, sell, issue));
    }
}

public sealed class AddCurrencyDialogViewModel : ObservableObject
{
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _buyText = "0";
    private string _sellText = "0";
    private string _error = string.Empty;

    public AddCurrencyDialogViewModel()
    {
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null));
    }

    public event Action<AddCurrencyResult?>? RequestClose;

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BuyText
    {
        get => _buyText;
        set => SetProperty(ref _buyText, value);
    }

    public string SellText
    {
        get => _sellText;
        set => SetProperty(ref _sellText, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private void Save()
    {
        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(Code))
        {
            Error = "Vui lòng nhập mã ngoại tệ (ví dụ: AUD, SGD...).";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Vui lòng nhập tên ngoại tệ.";
            return;
        }

        if (!decimal.TryParse(BuyText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var buy) || buy < 0
            || !decimal.TryParse(SellText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var sell) || sell < 0)
        {
            Error = "Tỉ giá mua/bán không hợp lệ.";
            return;
        }

        RequestClose?.Invoke(new AddCurrencyResult(Code.Trim().ToUpperInvariant(), Name.Trim(), buy, sell));
    }
}
