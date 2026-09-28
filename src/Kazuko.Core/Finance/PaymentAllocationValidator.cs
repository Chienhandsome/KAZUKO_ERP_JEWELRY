using Kazuko.Core.Finance;

namespace Kazuko.Core.Finance;

public static class PaymentAllocationValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal paidAmountVnd)
    {
        var errors = new List<string>();
        if (paidAmountVnd < 0)
            errors.Add("Số tiền đã thanh toán không được âm.");

        if (allocations is null || allocations.Count == 0)
        {
            if (paidAmountVnd > 0)
                errors.Add("Phải khai báo ít nhất một dòng thanh toán khi đã thanh toán > 0.");
            return errors;
        }

        decimal sum = 0;
        var line = 0;
        foreach (var item in allocations)
        {
            line++;
            if (item.AmountVnd <= 0)
                errors.Add($"Dòng thanh toán {line}: số tiền phải > 0.");

            var method = (item.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant();
            if (method == PaymentMethods.Cash)
            {
                if (item.CashFundId is null or <= 0)
                    errors.Add($"Dòng thanh toán {line}: tiền mặt bắt buộc chọn quỹ.");
                if (item.BankAccountId is > 0)
                    errors.Add($"Dòng thanh toán {line}: tiền mặt không được gắn tài khoản ngân hàng.");
            }
            else if (method == PaymentMethods.BankTransfer)
            {
                if (item.BankAccountId is null or <= 0)
                    errors.Add($"Dòng thanh toán {line}: chuyển khoản bắt buộc chọn tài khoản ngân hàng.");
                if (item.CashFundId is > 0)
                    errors.Add($"Dòng thanh toán {line}: chuyển khoản không được gắn quỹ tiền mặt.");
            }
            else
            {
                errors.Add($"Dòng thanh toán {line}: phương thức không hợp lệ.");
            }

            sum += item.AmountVnd;
        }

        if (sum != paidAmountVnd)
            errors.Add($"Tổng phân bổ thanh toán ({sum:N0}) phải bằng số đã thanh toán ({paidAmountVnd:N0}).");

        return errors;
    }
}
