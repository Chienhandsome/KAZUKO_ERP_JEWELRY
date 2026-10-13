using Kazuko.Core.Finance;

namespace Kazuko.Modules.Finance;

public static class FinanceDisplayTexts
{
    public static string PaymentMethod(string? code) => code switch
    {
        FinancePaymentMethods.Cash => "Tiền mặt",
        FinancePaymentMethods.BankTransfer => "Chuyển khoản",
        FinancePaymentMethods.Card => "Thẻ",
        _ => code ?? string.Empty
    };

    public static string VoucherStatus(string? code) => code switch
    {
        FinancialVoucherStatuses.Posted => "Đã ghi sổ",
        FinancialVoucherStatuses.Cancelled => "Đã hủy",
        _ => code ?? string.Empty
    };

    public static string VoucherType(string? code) => code switch
    {
        FinancialVoucherTypes.In => "Thu",
        FinancialVoucherTypes.Out => "Chi",
        _ => code ?? string.Empty
    };
}

public static class FinancePaymentMethods
{
    public const string Cash = "CASH";
    public const string BankTransfer = "BANK_TRANSFER";
    public const string Card = "CARD";
}
