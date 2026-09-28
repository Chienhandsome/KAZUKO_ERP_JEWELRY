using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Debt;

public sealed class DebtPaymentDialogViewModel : ObservableObject
{
    private decimal _amount;
    private string _payMethod = PaymentMethods.Cash;
    private string _referenceNo = string.Empty;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private CashFundOption? _selectedFund;
    private BankAccountOption? _selectedBank;

    public DebtPaymentDialogViewModel(
        string title,
        string partyLabel,
        decimal maxAmount,
        IReadOnlyList<CashFundOption> funds,
        IReadOnlyList<BankAccountOption> banks,
        bool requireCashOrBankSelection = true)
    {
        Title = title;
        PartyLabel = partyLabel;
        MaxAmount = maxAmount;
        Funds = funds;
        Banks = banks;
        RequireCashOrBankSelection = requireCashOrBankSelection;
        SelectedFund = funds.FirstOrDefault(x => x.IsDefault) ?? funds.FirstOrDefault();
        SelectedBank = banks.FirstOrDefault();
        ConfirmCommand = new RelayCommand(Confirm, () => Amount > 0 && Amount <= MaxAmount);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        Amount = maxAmount;
    }

    public string Title { get; }
    public string PartyLabel { get; }
    public decimal MaxAmount { get; }
    public bool RequireCashOrBankSelection { get; }
    public IReadOnlyList<CashFundOption> Funds { get; }
    public IReadOnlyList<BankAccountOption> Banks { get; }

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value) && ConfirmCommand is RelayCommand relay)
                relay.NotifyCanExecuteChanged();
        }
    }

    public string PayMethod
    {
        get => _payMethod;
        set => SetProperty(ref _payMethod, value);
    }

    public string ReferenceNo
    {
        get => _referenceNo;
        set => SetProperty(ref _referenceNo, value);
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

    public CashFundOption? SelectedFund
    {
        get => _selectedFund;
        set => SetProperty(ref _selectedFund, value);
    }

    public BankAccountOption? SelectedBank
    {
        get => _selectedBank;
        set => SetProperty(ref _selectedBank, value);
    }

    public bool IsCash => string.Equals(PayMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);
    public bool Confirmed { get; private set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public Action<bool>? RequestClose { get; set; }

    public PaymentAllocationDraft BuildAllocation()
    {
        var method = (PayMethod ?? PaymentMethods.Cash).Trim().ToUpperInvariant();
        return method == PaymentMethods.BankTransfer
            ? new PaymentAllocationDraft(method, Amount, null, SelectedBank?.Id, NullIfEmpty(ReferenceNo))
            : new PaymentAllocationDraft(PaymentMethods.Cash, Amount, SelectedFund?.Id, null, NullIfEmpty(ReferenceNo));
    }

    private void Confirm()
    {
        ErrorMessage = string.Empty;
        if (Amount <= 0)
        {
            ErrorMessage = "Số tiền phải > 0.";
            return;
        }

        if (Amount > MaxAmount)
        {
            ErrorMessage = $"Số tiền không được vượt {MaxAmount:N0}.";
            return;
        }

        var method = (PayMethod ?? string.Empty).Trim().ToUpperInvariant();
        if (RequireCashOrBankSelection)
        {
            if (method == PaymentMethods.Cash && SelectedFund is null)
            {
                ErrorMessage = "Chọn quỹ tiền mặt.";
                return;
            }

            if (method == PaymentMethods.BankTransfer && SelectedBank is null)
            {
                ErrorMessage = "Chọn tài khoản ngân hàng.";
                return;
            }
        }

        Confirmed = true;
        RequestClose?.Invoke(true);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
