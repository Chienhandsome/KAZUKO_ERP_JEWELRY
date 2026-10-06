using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed class SalesPaymentAllocationRow : ObservableObject
{
    private string _paymentMethod = PaymentMethods.Cash;
    private decimal _amountVnd;
    private CashFundOption? _cashFund;
    private BankAccountOption? _bankAccount;
    private string _referenceNumber = string.Empty;

    public string PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                OnPropertyChanged(nameof(RequiresCashFund));
                OnPropertyChanged(nameof(RequiresBankAccount));
                if (RequiresCashFund)
                    BankAccount = null;
                if (RequiresBankAccount)
                    CashFund = null;
            }
        }
    }

    public decimal AmountVnd
    {
        get => _amountVnd;
        set => SetProperty(ref _amountVnd, value);
    }

    public CashFundOption? CashFund
    {
        get => _cashFund;
        set => SetProperty(ref _cashFund, value);
    }

    public BankAccountOption? BankAccount
    {
        get => _bankAccount;
        set => SetProperty(ref _bankAccount, value);
    }

    public string ReferenceNumber
    {
        get => _referenceNumber;
        set => SetProperty(ref _referenceNumber, value);
    }

    public bool RequiresCashFund =>
        string.Equals(PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);

    public bool RequiresBankAccount =>
        string.Equals(PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(PaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase);

    public PaymentAllocationDraft ToDraft() =>
        new(PaymentMethod, AmountVnd,
            RequiresCashFund ? CashFund?.Id : null,
            RequiresBankAccount ? BankAccount?.Id : null,
            string.IsNullOrWhiteSpace(ReferenceNumber) ? null : ReferenceNumber.Trim());
}

public sealed class SalesPaymentDialogViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private readonly decimal _netAmountVnd;
    private readonly bool _isPayout;
    private readonly bool _canApproveDebt;
    private readonly bool _allowReceivable;
    private decimal _receivableAmountVnd;
    private string _errorMessage = string.Empty;
    private SalesPaymentAllocationRow? _selectedAllocation;
    private bool? _dialogResult;

    public SalesPaymentDialogViewModel(
        decimal netAmountVnd,
        bool isPayout,
        bool canApproveDebt,
        bool allowReceivable,
        IReadOnlyList<CashFundOption> cashFunds,
        IReadOnlyList<BankAccountOption> bankAccounts,
        string documentHint)
    {
        _netAmountVnd = netAmountVnd;
        _isPayout = isPayout;
        _canApproveDebt = canApproveDebt;
        _allowReceivable = allowReceivable && !isPayout && canApproveDebt;
        DocumentHint = documentHint;
        foreach (var fund in cashFunds) CashFunds.Add(fund);
        foreach (var bank in bankAccounts) BankAccounts.Add(bank);

        if (isPayout)
        {
            AvailableMethods.Add(PaymentMethods.Cash);
            AvailableMethods.Add(PaymentMethods.BankTransfer);
        }
        else
        {
            AvailableMethods.Add(PaymentMethods.Cash);
            AvailableMethods.Add(PaymentMethods.BankTransfer);
            AvailableMethods.Add(PaymentMethods.Card);
        }

        AddAllocationCommand = new RelayCommand(AddAllocation);
        RemoveAllocationCommand = new RelayCommand(RemoveAllocation, () => SelectedAllocation is not null);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(() => DialogResult = false);

        if (Math.Abs(netAmountVnd) > 0)
            AddAllocation();
    }

    public string DocumentHint { get; }
    public bool IsPayout => _isPayout;
    public bool CanUseReceivable => _allowReceivable;
    public decimal AbsoluteAmount => Math.Abs(_netAmountVnd);
    public string AbsoluteAmountText => AbsoluteAmount.ToString("N0", Vi);
    public string ModeLabel => _isPayout ? "Chi cho khách" : "Thu từ khách";

    public ObservableCollection<string> AvailableMethods { get; } = [];
    public ObservableCollection<CashFundOption> CashFunds { get; } = [];
    public ObservableCollection<BankAccountOption> BankAccounts { get; } = [];
    public ObservableCollection<SalesPaymentAllocationRow> Allocations { get; } = [];

    public SalesPaymentAllocationRow? SelectedAllocation
    {
        get => _selectedAllocation;
        set
        {
            if (SetProperty(ref _selectedAllocation, value))
                ((RelayCommand)RemoveAllocationCommand).NotifyCanExecuteChanged();
        }
    }

    public decimal ReceivableAmountVnd
    {
        get => _receivableAmountVnd;
        set
        {
            if (SetProperty(ref _receivableAmountVnd, value))
            {
                OnPropertyChanged(nameof(AllocatedPlusReceivable));
                ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public decimal AllocatedTotal => Allocations.Sum(x => x.AmountVnd);
    public decimal AllocatedPlusReceivable => AllocatedTotal + ReceivableAmountVnd;
    public string AllocatedTotalText => AllocatedTotal.ToString("N0", Vi);

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

    public SalesPaymentDialogResult? Result { get; private set; }

    public ICommand AddAllocationCommand { get; }
    public ICommand RemoveAllocationCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public void NotifyAllocationChanged()
    {
        OnPropertyChanged(nameof(AllocatedTotal));
        OnPropertyChanged(nameof(AllocatedPlusReceivable));
        OnPropertyChanged(nameof(AllocatedTotalText));
        ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
    }

    private void AddAllocation()
    {
        var remaining = _isPayout
            ? AbsoluteAmount - AllocatedTotal
            : AbsoluteAmount - AllocatedPlusReceivable;
        var row = new SalesPaymentAllocationRow
        {
            PaymentMethod = AvailableMethods[0],
            AmountVnd = remaining > 0 ? remaining : 0,
            CashFund = CashFunds.FirstOrDefault()
        };
        Allocations.Add(row);
        SelectedAllocation = row;
        NotifyAllocationChanged();
    }

    private void RemoveAllocation()
    {
        if (SelectedAllocation is null) return;
        Allocations.Remove(SelectedAllocation);
        SelectedAllocation = null;
        NotifyAllocationChanged();
    }

    private bool CanConfirm() => string.IsNullOrEmpty(Validate());

    private void Confirm()
    {
        var error = Validate();
        if (!string.IsNullOrEmpty(error))
        {
            ErrorMessage = error;
            return;
        }

        Result = new SalesPaymentDialogResult(
            Allocations.Select(x => x.ToDraft()).ToArray(),
            _isPayout ? 0 : ReceivableAmountVnd);
        DialogResult = true;
    }

    private string Validate()
    {
        if (_isPayout)
        {
            if (Allocations.Any(x => string.Equals(x.PaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase)))
                return "Không được dùng thẻ cho khoản chi.";
            if (AllocatedTotal != AbsoluteAmount)
                return "Phải phân bổ đủ số tiền chi.";
            if (ReceivableAmountVnd != 0)
                return "Không tạo công nợ khi chi tiền mua vàng cũ.";
        }
        else
        {
            if (!_canApproveDebt && ReceivableAmountVnd > 0)
                return "Không có quyền phê duyệt công nợ.";
            if (!_allowReceivable && ReceivableAmountVnd > 0)
                return "Khách lẻ không được phát sinh công nợ.";
            if (AllocatedPlusReceivable != AbsoluteAmount)
                return "Tổng thanh toán cộng công nợ phải bằng số tiền ròng.";
        }

        var paymentErrors = PaymentAllocationValidator.Validate(
            Allocations.Select(x => x.ToDraft()).ToArray(),
            AllocatedTotal);
        return paymentErrors.Count > 0 ? string.Join(" ", paymentErrors) : string.Empty;
    }
}
