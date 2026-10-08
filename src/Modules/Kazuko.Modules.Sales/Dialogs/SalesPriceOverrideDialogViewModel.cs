using System.Windows.Input;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed class SalesPriceOverrideDialogViewModel : ObservableObject
{
    private decimal _goldPrice;
    private decimal _laborAmount;
    private decimal _stoneAmount;
    private string _overrideReason = string.Empty;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public SalesPriceOverrideDialogViewModel(
        string productCode,
        string productName,
        decimal sourceGold,
        decimal sourceLabor,
        decimal sourceStone,
        decimal appliedGold,
        decimal appliedLabor,
        decimal appliedStone,
        string? overrideReason)
    {
        ProductCode = productCode;
        ProductName = productName;
        SourceGoldPricePerChi = sourceGold;
        SourceLaborAmountVnd = sourceLabor;
        SourceStoneAmountVnd = sourceStone;
        _goldPrice = appliedGold;
        _laborAmount = appliedLabor;
        _stoneAmount = appliedStone;
        _overrideReason = overrideReason ?? string.Empty;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        ResetCommand = new RelayCommand(ResetToSource);
    }

    public string ProductCode { get; }
    public string ProductName { get; }
    public decimal SourceGoldPricePerChi { get; }
    public decimal SourceLaborAmountVnd { get; }
    public decimal SourceStoneAmountVnd { get; }

    public decimal GoldPrice
    {
        get => _goldPrice;
        set => SetProperty(ref _goldPrice, value);
    }

    public decimal LaborAmount
    {
        get => _laborAmount;
        set => SetProperty(ref _laborAmount, value);
    }

    public decimal StoneAmount
    {
        get => _stoneAmount;
        set => SetProperty(ref _stoneAmount, value);
    }

    public string OverrideReason
    {
        get => _overrideReason;
        set => SetProperty(ref _overrideReason, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public bool Accepted { get; private set; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetCommand { get; }

    private void ResetToSource()
    {
        GoldPrice = SourceGoldPricePerChi;
        LaborAmount = SourceLaborAmountVnd;
        StoneAmount = SourceStoneAmountVnd;
        OverrideReason = string.Empty;
        ErrorMessage = string.Empty;
    }

    private void Confirm()
    {
        if (GoldPrice < 0 || LaborAmount < 0 || StoneAmount < 0)
        {
            ErrorMessage = "Giá / công / đá không được âm.";
            return;
        }

        Accepted = true;
        DialogResult = true;
    }
}
