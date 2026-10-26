using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows;
using Kazuko.Core.UserManagement;
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

/// <summary>Một dòng phân bổ công nợ trong dialog (hiển thị + nhập số tiền).</summary>
public sealed class DebtAllocationRowViewModel : ObservableObject
{
    private decimal _paymentAmountVnd;

    public DebtAllocationRowViewModel(DebtSettlementAllocationLine line, bool allowEdit)
    {
        SourceType = line.SourceType;
        SourceId = line.SourceId;
        DisplayName = line.DisplayName;
        DocumentCode = line.DocumentCode;
        RemainingAmountVnd = line.RemainingAmountVnd;
        _paymentAmountVnd = line.PaymentAmountVnd;
        IsAmountEditable = allowEdit;
    }

    public string SourceType { get; }
    public long SourceId { get; }
    public string DisplayName { get; }
    public string? DocumentCode { get; }
    public decimal RemainingAmountVnd { get; }
    public bool IsAmountEditable { get; }
    public bool IsAmountReadOnly => !IsAmountEditable;

    public decimal PaymentAmountVnd
    {
        get => _paymentAmountVnd;
        set
        {
            if (SetProperty(ref _paymentAmountVnd, value))
                AmountChanged?.Invoke();
        }
    }

    public string PaymentAmountText
    {
        get => PaymentAmountVnd.ToString("N0", new CultureInfo("vi-VN"));
        set
        {
            if (TryParseAmount(value, out var amount))
                PaymentAmountVnd = amount;
        }
    }

    public Action? AmountChanged { get; set; }

    public DebtSettlementAllocationLine ToLine() => new(
        SourceType, SourceId, DisplayName, DocumentCode,
        RemainingAmountVnd, PaymentAmountVnd);

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        var normalized = text.Replace(" ", string.Empty).Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}

public sealed class CashVoucherDialogViewModel : ObservableObject
{
    private readonly IFinanceVoucherService _voucherService;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly string _voucherType;
    private readonly IVoucherCategoryService? _categories;
    private readonly IAuthorizationService? _permissions;
    private VoucherCategory? _selectedCategory;

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private DateTime _voucherDate = DateTime.Today;
    private string _cashAmountText = string.Empty;
    private string _bankTransferAmountText = string.Empty;
    private string _cardAmountText = string.Empty;
    private string _partyName = string.Empty;
    private string _notes = string.Empty;
    private string _referenceNumber = string.Empty;
    private CashFundOption? _selectedFund;
    private BankAccountOption? _selectedBank;

    /// <summary>
    /// Chế độ mở từ module Tài chính: lưu là tạo phiếu ngay (hành vi gốc).
    /// </summary>
    public CashVoucherDialogViewModel(
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        IReadOnlyList<CashFundOption> funds,
        IReadOnlyList<BankAccountOption> banks,
        string voucherType, IVoucherCategoryService? categories = null, IAuthorizationService? permissions = null)
        : this(voucherService, sessionAccessor, funds, banks, voucherType,
               createVoucherOnSave: true, prefill: null, categories, permissions)
    {
    }

    /// <summary>
    /// Chế độ mở từ module Công nợ: chỉ thu thập input, KHÔNG tự tạo voucher.
    /// Backend tạo settlement + voucher + cập nhật nợ trong 1 transaction.
    /// </summary>
    public CashVoucherDialogViewModel(
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        IReadOnlyList<CashFundOption> funds,
        IReadOnlyList<BankAccountOption> banks,
        DebtSettlementDialogRequest request)
        : this(voucherService, sessionAccessor, funds, banks, request.VoucherType,
               createVoucherOnSave: false, prefill: request)
    {
    }

    private CashVoucherDialogViewModel(
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        IReadOnlyList<CashFundOption> funds,
        IReadOnlyList<BankAccountOption> banks,
        string voucherType,
        bool createVoucherOnSave,
        DebtSettlementDialogRequest? prefill, IVoucherCategoryService? categories = null, IAuthorizationService? permissions = null)
    {
        _voucherService = voucherService;
        _sessionAccessor = sessionAccessor;
        _voucherType = voucherType;
        _categories = categories; _permissions = permissions;
        Funds = funds;
        Banks = banks;
        CreateVoucherOnSave = createVoucherOnSave;
        IsDebtMode = prefill is not null;
        IsAmountEditable = prefill?.AllowAmountEdit ?? true;

        SubtitleText = createVoucherOnSave
            ? "Phiếu được ghi sổ ngay sau khi lưu."
            : "Xác nhận phân bổ công nợ và phương thức thanh toán.";

        if (prefill is not null)
        {
            _partyName = prefill.PartyDisplayName;
            _notes = prefill.DefaultNotes ?? string.Empty;
            foreach (var line in prefill.Allocations)
            {
                var row = new DebtAllocationRowViewModel(line, IsAmountEditable);
                row.AmountChanged = RefreshTotals;
                DebtAllocations.Add(row);
            }
        }

        var isIncome = voucherType == FinancialVoucherTypes.In;
        Title = isIncome ? "Phiếu thu tiền" : "Phiếu chi tiền";
        VoucherTypeName = isIncome ? "Thu" : "Chi";

        _selectedFund = Funds.FirstOrDefault(x => x.IsDefault) ?? Funds.FirstOrDefault();
        _selectedBank = Banks.FirstOrDefault();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        ManageCategoriesCommand = new AsyncRelayCommand(ManageCategoriesAsync, () => !IsBusy && SupportsCategories);
    }

    public string Title { get; }
    public string VoucherTypeName { get; }
    public string CategoryLabel => _voucherType == FinancialVoucherTypes.In ? "Loại thu" : "Loại chi";
    public bool SupportsCategories => CreateVoucherOnSave && _categories is not null;
    public ObservableCollection<VoucherCategory> Categories { get; } = [];
    public VoucherCategory? SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }
    public ICommand ManageCategoriesCommand { get; }
    public async Task LoadCategoriesAsync()
    {
        if (!SupportsCategories || _sessionAccessor.Current is not { } session) return;
        IsBusy = true;
        try
        {
            var selected = SelectedCategory?.Id;
            var rows = await _categories!.ListAsync(_voucherType, false, session);
            Categories.Clear(); foreach (var row in rows) Categories.Add(row);
            SelectedCategory = Categories.FirstOrDefault(x => x.Id == selected) ?? Categories.FirstOrDefault(x => x.Code == "OTHER") ?? Categories.FirstOrDefault();
            ErrorMessage = string.Empty;
        }
        catch (Exception ex) { Categories.Clear(); SelectedCategory = null; ErrorMessage = $"Không tải được loại thu/chi: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private async Task ManageCategoriesAsync()
    {
        if (_categories is null || _sessionAccessor.Current is not { } session) return;
        var vm = new VoucherCategoryViewModel(_categories, session, _voucherType, _permissions);
        new VoucherCategoryDialog(vm) { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) }.ShowDialog();
        await LoadCategoriesAsync();
    }

    /// <summary>true = lưu tạo phiếu ngay (module Tài chính); false = chỉ trả input (module Công nợ).</summary>
    public bool CreateVoucherOnSave { get; }

    /// <summary>true khi mở từ module Công nợ (hiện section phân bổ).</summary>
    public bool IsDebtMode { get; }

    /// <summary>false khi request khóa ô số tiền phân bổ.</summary>
    public bool IsAmountEditable { get; }

    public string SubtitleText { get; }

    public IReadOnlyList<CashFundOption> Funds { get; }
    public IReadOnlyList<BankAccountOption> Banks { get; }

    public ObservableCollection<DebtAllocationRowViewModel> DebtAllocations { get; } = [];

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Action<bool>? RequestClose { get; set; }

    /// <summary>Phiếu vừa được tạo (null nếu người dùng hủy). Chỉ có ở chế độ tạo voucher.</summary>
    public CashVoucherDetail? SavedVoucher { get; private set; }

    /// <summary>Input đã xác nhận (chỉ có ở chế độ thu thập input từ Công nợ).</summary>
    public DebtSettlementDialogResult? SettlementResult { get; private set; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ManageCategoriesCommand).NotifyCanExecuteChanged();
        }
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

    public string CashAmountText
    {
        get => _cashAmountText;
        set { if (SetProperty(ref _cashAmountText, value)) RefreshTotals(); }
    }

    public string BankTransferAmountText
    {
        get => _bankTransferAmountText;
        set { if (SetProperty(ref _bankTransferAmountText, value)) RefreshTotals(); }
    }

    public string CardAmountText
    {
        get => _cardAmountText;
        set { if (SetProperty(ref _cardAmountText, value)) RefreshTotals(); }
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

    public decimal TotalAllocationAmount => DebtAllocations.Sum(x => x.PaymentAmountVnd);

    public decimal TotalPaymentAmount =>
        ParseAmount(CashAmountText) + ParseAmount(BankTransferAmountText) + ParseAmount(CardAmountText);

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(TotalAllocationAmount));
        OnPropertyChanged(nameof(TotalPaymentAmount));
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (SupportsCategories && (SelectedCategory is null || !SelectedCategory.IsActive || SelectedCategory.VoucherType != _voucherType))
        {
            ErrorMessage = "Vui lòng chọn loại thu/chi đang sử dụng.";
            return;
        }

        var cash = ParseAmount(CashAmountText);
        var bank = ParseAmount(BankTransferAmountText);
        var card = ParseAmount(CardAmountText);
        var totalPayment = cash + bank + card;

        if (totalPayment <= 0)
        {
            ErrorMessage = "Tổng thanh toán phải > 0 (nhập ít nhất một ô phương thức).";
            return;
        }

        if (IsDebtMode)
        {
            var totalAlloc = TotalAllocationAmount;
            if (totalAlloc <= 0)
            {
                ErrorMessage = "Tổng phân bổ công nợ phải > 0.";
                return;
            }
            if (totalPayment != totalAlloc)
            {
                ErrorMessage = $"Tổng thanh toán ({totalPayment:N0}) phải bằng tổng phân bổ ({totalAlloc:N0}).";
                return;
            }
            foreach (var row in DebtAllocations)
            {
                if (row.PaymentAmountVnd < 0 || row.PaymentAmountVnd > row.RemainingAmountVnd)
                {
                    ErrorMessage = $"Số tiền dòng '{row.DisplayName}' phải từ 0 đến {row.RemainingAmountVnd:N0}.";
                    return;
                }
            }
        }

        if (cash > 0 && SelectedFund is null)
        {
            ErrorMessage = "Tiền mặt phải chọn quỹ.";
            return;
        }
        if ((bank > 0 || card > 0) && SelectedBank is null)
        {
            ErrorMessage = "Chuyển khoản/thẻ phải chọn tài khoản ngân hàng.";
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
            if (CreateVoucherOnSave)
            {
                // Chế độ Tài chính: xác định 1 phương thức (hoặc MIXED nếu nhiều ô).
                var method = DetermineSingleMethod(cash, bank, card);
                var request = new ManualVoucherRequest(
                    _voucherType,
                    VoucherDate,
                    method,
                    totalPayment,
                    NullIfEmpty(PartyName),
                    NullIfEmpty(Notes),
                    NullIfEmpty(ReferenceNumber), SelectedCategory?.Id);
                SavedVoucher = await _voucherService.CreateManualVoucherAsync(request, session);
            }
            else
            {
                SettlementResult = new DebtSettlementDialogResult(
                    _voucherType,
                    VoucherDate,
                    DebtAllocations.Select(x => x.ToLine()).ToList(),
                    cash, bank, card,
                    SelectedFund?.Id,
                    SelectedBank?.Id,
                    NullIfEmpty(PartyName),
                    NullIfEmpty(Notes),
                    NullIfEmpty(ReferenceNumber));
            }

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

    private static string DetermineSingleMethod(decimal cash, decimal bank, decimal card)
    {
        var count = (cash > 0 ? 1 : 0) + (bank > 0 ? 1 : 0) + (card > 0 ? 1 : 0);
        if (count > 1)
            return PaymentMethods.Mixed;
        if (cash > 0)
            return PaymentMethods.Cash;
        if (bank > 0)
            return PaymentMethods.BankTransfer;
        return PaymentMethods.Card;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var normalized = text.Replace(" ", string.Empty).Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount : 0;
    }
}
