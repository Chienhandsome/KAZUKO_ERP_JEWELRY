using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Finance;

public sealed class PaymentMethodOption(string code, string name)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class CashVoucherDialogViewModel : ObservableObject
{
    private readonly IFinanceVoucherService _voucherService;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly string _voucherType;

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private DateTime _voucherDate = DateTime.Today;
    private string _amountText = string.Empty;
    private PaymentMethodOption? _selectedPaymentMethod;
    private string _partyName = string.Empty;
    private string _notes = string.Empty;
    private string _referenceNumber = string.Empty;

    public CashVoucherDialogViewModel(
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        string voucherType)
    {
        _voucherService = voucherService;
        _sessionAccessor = sessionAccessor;
        _voucherType = voucherType;

        var isIncome = voucherType == FinancialVoucherTypes.In;
        Title = isIncome ? "Phiếu thu tiền" : "Phiếu chi tiền";
        VoucherTypeName = isIncome ? "Thu" : "Chi";

        PaymentMethodOptions.Add(new PaymentMethodOption(FinancePaymentMethods.Cash, "Tiền mặt"));
        PaymentMethodOptions.Add(new PaymentMethodOption(FinancePaymentMethods.BankTransfer, "Chuyển khoản"));
        PaymentMethodOptions.Add(new PaymentMethodOption(FinancePaymentMethods.Card, "Thẻ"));
        SelectedPaymentMethod = PaymentMethodOptions[0];

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public string Title { get; }
    public string VoucherTypeName { get; }

    public System.Collections.ObjectModel.ObservableCollection<PaymentMethodOption> PaymentMethodOptions { get; } = [];

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Action<bool>? RequestClose { get; set; }

    /// <summary>Phiếu vừa được tạo (null nếu người dùng hủy).</summary>
    public CashVoucherDetail? SavedVoucher { get; private set; }

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

    public DateTime VoucherDate
    {
        get => _voucherDate;
        set => SetProperty(ref _voucherDate, value);
    }

    public string AmountText
    {
        get => _amountText;
        set => SetProperty(ref _amountText, value);
    }

    public PaymentMethodOption? SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set => SetProperty(ref _selectedPaymentMethod, value);
    }

    public string PartyName
    {
        get => _partyName;
        set => SetProperty(ref _partyName, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string ReferenceNumber
    {
        get => _referenceNumber;
        set => SetProperty(ref _referenceNumber, value);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (!TryParseAmount(AmountText, out var amount) || amount <= 0)
        {
            ErrorMessage = "Số tiền phải là số dương (VD: 1.250.000).";
            return;
        }

        if (SelectedPaymentMethod is null)
        {
            ErrorMessage = "Vui lòng chọn phương thức thanh toán.";
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập không hợp lệ.";
            return;
        }

        IsBusy = true;
        try
        {
            var request = new ManualVoucherRequest(
                _voucherType,
                VoucherDate,
                SelectedPaymentMethod.Code,
                amount,
                NullIfEmpty(PartyName),
                NullIfEmpty(Notes),
                NullIfEmpty(ReferenceNumber));

            SavedVoucher = await _voucherService.CreateManualVoucherAsync(request, session);
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không lưu được phiếu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Chấp nhận "1.250.000" (phân cách nghìn kiểu VN) lẫn "1250000".
        var normalized = text.Replace(" ", string.Empty)
                             .Replace(".", string.Empty)
                             .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
