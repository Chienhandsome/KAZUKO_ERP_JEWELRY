using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Debt;

public sealed class DebtPaymentLineRow : ObservableObject
{
    private string _paymentMethod = PaymentMethods.Cash;
    private decimal _amountVnd;
    private CashFundOption? _selectedFund;
    private BankAccountOption? _selectedBank;
    private string _referenceNo = string.Empty;

    public string PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                OnPropertyChanged(nameof(IsCash));
                OnPropertyChanged(nameof(IsBankTransfer));
            }
        }
    }

    public bool IsCash
    {
        get => string.Equals(PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                PaymentMethod = PaymentMethods.Cash;
        }
    }

    public bool IsBankTransfer
    {
        get => string.Equals(PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                PaymentMethod = PaymentMethods.BankTransfer;
        }
    }

    public decimal AmountVnd
    {
        get => _amountVnd;
        set
        {
            if (SetProperty(ref _amountVnd, value))
                AmountChanged?.Invoke();
        }
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

    public string ReferenceNo
    {
        get => _referenceNo;
        set => SetProperty(ref _referenceNo, value);
    }

    public Action? AmountChanged { get; set; }

    public PaymentAllocationDraft ToDraft()
    {
        var method = (PaymentMethod ?? PaymentMethods.Cash).Trim().ToUpperInvariant();
        return method == PaymentMethods.BankTransfer
            ? new PaymentAllocationDraft(method, AmountVnd, null, SelectedBank?.Id, NullIfEmpty(ReferenceNo))
            : new PaymentAllocationDraft(PaymentMethods.Cash, AmountVnd, SelectedFund?.Id, null, NullIfEmpty(ReferenceNo));
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DebtPaymentDialogViewModel : ObservableObject
{
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private DebtPaymentLineRow? _selectedLine;

    public DebtPaymentDialogViewModel(
        string title,
        string partyLabel,
        decimal maxAmount,
        IReadOnlyList<CashFundOption> funds,
        IReadOnlyList<BankAccountOption> banks)
    {
        Title = title;
        PartyLabel = partyLabel;
        MaxAmount = maxAmount;
        Funds = funds;
        Banks = banks;
        Lines = new ObservableCollection<DebtPaymentLineRow>();
        AddLineCommand = new RelayCommand(AddLine);
        RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine is not null);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        AddLine();
        if (Lines.Count > 0)
            Lines[0].AmountVnd = maxAmount;
    }

    public string Title { get; }
    public string PartyLabel { get; }
    public decimal MaxAmount { get; }
    public IReadOnlyList<CashFundOption> Funds { get; }
    public IReadOnlyList<BankAccountOption> Banks { get; }
    public ObservableCollection<DebtPaymentLineRow> Lines { get; }

    public DebtPaymentLineRow? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value) && RemoveLineCommand is RelayCommand remove)
                remove.NotifyCanExecuteChanged();
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

    public decimal TotalPayment => Lines.Sum(x => x.AmountVnd);
    public decimal RemainingAfterPayment => MaxAmount - TotalPayment;
    public bool Confirmed { get; private set; }

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public Action<bool>? RequestClose { get; set; }

    public IReadOnlyList<PaymentAllocationDraft> BuildAllocations() =>
        Lines.Select(x => x.ToDraft()).ToList();

    private void AddLine()
    {
        var row = new DebtPaymentLineRow
        {
            PaymentMethod = PaymentMethods.Cash,
            AmountVnd = 0,
            SelectedFund = Funds.FirstOrDefault(x => x.IsDefault) ?? Funds.FirstOrDefault(),
            SelectedBank = Banks.FirstOrDefault(),
            AmountChanged = RefreshTotals
        };
        Lines.Add(row);
        SelectedLine = row;
        RefreshTotals();
    }

    private void RemoveSelectedLine()
    {
        if (SelectedLine is null || Lines.Count <= 1)
            return;
        Lines.Remove(SelectedLine);
        SelectedLine = Lines.FirstOrDefault();
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(TotalPayment));
        OnPropertyChanged(nameof(RemainingAfterPayment));
        if (ConfirmCommand is RelayCommand confirm)
            confirm.NotifyCanExecuteChanged();
    }

    private bool CanConfirm() =>
        Lines.Count > 0 && TotalPayment > 0 && TotalPayment <= MaxAmount;

    private void Confirm()
    {
        ErrorMessage = string.Empty;
        var drafts = BuildAllocations();
        var total = drafts.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(drafts, total).ToList();
        if (total <= 0)
            errors.Add("Tổng thanh toán phải > 0.");
        if (total > MaxAmount)
            errors.Add($"Tổng thanh toán không được vượt {MaxAmount:N0}.");

        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(" ", errors);
            return;
        }

        Confirmed = true;
        RequestClose?.Invoke(true);
    }
}
