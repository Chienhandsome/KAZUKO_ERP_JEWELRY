using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed record PaymentMethodOption(string Code, string Label)
{
    public override string ToString() => Label;
}

public sealed class SalesPaymentAllocationRow : ObservableObject
{
    private readonly Action? _onChanged;
    private string _paymentMethod = PaymentMethods.Cash;
    private decimal _amountVnd;
    private CashFundOption? _cashFund;
    private BankAccountOption? _bankAccount;
    private string _referenceNumber = string.Empty;

    public SalesPaymentAllocationRow(Action? onChanged = null) => _onChanged = onChanged;

    public string PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (!SetProperty(ref _paymentMethod, value))
                return;
            OnPropertyChanged(nameof(RequiresCashFund));
            OnPropertyChanged(nameof(RequiresBankAccount));
            OnPropertyChanged(nameof(PaymentMethodLabel));
            if (RequiresCashFund)
                BankAccount = null;
            if (RequiresBankAccount)
                CashFund = null;
            _onChanged?.Invoke();
        }
    }

    public string PaymentMethodLabel => PaymentMethod switch
    {
        PaymentMethods.Cash => "Tiền mặt",
        PaymentMethods.BankTransfer => "Chuyển khoản",
        PaymentMethods.Card => "Thẻ",
        _ => PaymentMethod
    };

    public decimal AmountVnd
    {
        get => _amountVnd;
        set
        {
            if (SetProperty(ref _amountVnd, value))
                _onChanged?.Invoke();
        }
    }

    public CashFundOption? CashFund
    {
        get => _cashFund;
        set
        {
            if (SetProperty(ref _cashFund, value))
                _onChanged?.Invoke();
        }
    }

    public BankAccountOption? BankAccount
    {
        get => _bankAccount;
        set
        {
            if (SetProperty(ref _bankAccount, value))
                _onChanged?.Invoke();
        }
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
        DocumentHint = string.IsNullOrWhiteSpace(documentHint) ? "Thanh toán tại quầy" : documentHint;
        foreach (var fund in cashFunds) CashFunds.Add(fund);
        foreach (var bank in bankAccounts) BankAccounts.Add(bank);

        MethodOptions.Add(new PaymentMethodOption(PaymentMethods.Cash, "Tiền mặt"));
        MethodOptions.Add(new PaymentMethodOption(PaymentMethods.BankTransfer, "Chuyển khoản"));
        if (!isPayout)
            MethodOptions.Add(new PaymentMethodOption(PaymentMethods.Card, "Thẻ"));

        DefaultCashFund = CashFunds.FirstOrDefault(x => x.IsDefault) ?? CashFunds.FirstOrDefault();
        DefaultBankAccount = PreferPrimaryBankAccount(BankAccounts) ?? BankAccounts.FirstOrDefault();

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
    public string ReferenceHint =>
        "Mã giao dịch / số tham chiếu (tuỳ chọn): số CK ngân hàng, mã UTR, hoặc mã chuẩn chi thẻ — để đối soát sổ quỹ.";

    public ObservableCollection<PaymentMethodOption> MethodOptions { get; } = [];
    public ObservableCollection<CashFundOption> CashFunds { get; } = [];
    public ObservableCollection<BankAccountOption> BankAccounts { get; } = [];
    public ObservableCollection<SalesPaymentAllocationRow> Allocations { get; } = [];
    public CashFundOption? DefaultCashFund { get; }
    public BankAccountOption? DefaultBankAccount { get; }

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
    public string RemainingText =>
        Math.Max(0, AbsoluteAmount - (_isPayout ? AllocatedTotal : AllocatedPlusReceivable)).ToString("N0", Vi);

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
        OnPropertyChanged(nameof(RemainingText));
        ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
    }

    private void AddAllocation()
    {
        var remaining = _isPayout
            ? AbsoluteAmount - AllocatedTotal
            : AbsoluteAmount - AllocatedPlusReceivable;
        var row = new SalesPaymentAllocationRow(NotifyAllocationChanged)
        {
            PaymentMethod = MethodOptions[0].Code,
            AmountVnd = remaining > 0 ? remaining : 0,
            CashFund = DefaultCashFund,
            BankAccount = null
        };
        // Default cash uses quỹ; bank/card rows get primary account when method changes.
        ApplyMethodDefaults(row);
        Allocations.Add(row);
        SelectedAllocation = row;
        NotifyAllocationChanged();
    }

    private void ApplyMethodDefaults(SalesPaymentAllocationRow row)
    {
        if (row.RequiresCashFund)
        {
            row.CashFund ??= DefaultCashFund;
            row.BankAccount = null;
        }
        else if (row.RequiresBankAccount)
        {
            row.BankAccount ??= DefaultBankAccount;
            row.CashFund = null;
        }
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
        foreach (var row in Allocations)
            ApplyMethodDefaults(row);

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

    internal static BankAccountOption? PreferPrimaryBankAccount(IEnumerable<BankAccountOption> banks)
    {
        var list = banks.ToList();
        return list.FirstOrDefault(IsPrimaryBankAccount) ?? list.FirstOrDefault();
    }

    private static bool IsPrimaryBankAccount(BankAccountOption bank)
    {
        var haystack = $"{bank.Code} {bank.BankName} {bank.AccountNumber}";
        return haystack.Contains("Tài khoản chính", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("tai khoan chinh", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("CHINH", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("MAIN", StringComparison.OrdinalIgnoreCase);
    }
}
