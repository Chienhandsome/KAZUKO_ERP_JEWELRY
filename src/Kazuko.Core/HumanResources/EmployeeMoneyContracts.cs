using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Core.HumanResources;

public sealed record EmployeeMoneyOption(long Id, string Code, string FullName)
{
    public override string ToString() => $"{Code} · {FullName}";
}
public sealed record EmployeeAwardRequest(Guid RequestId, long EmployeeId, DateTime EventDate, string Kind,
    string Handling, decimal Amount, string Description, int? PayYear = null, int? PayMonth = null,
    PaymentAllocationDraft? Payment = null, long? CategoryId = null);
public sealed record EmployeeAward(long Id, long EmployeeId, string EmployeeCode, string EmployeeName, DateTime EventDate,
    string Kind, string Handling, decimal Amount, string Description, int? PayYear, int? PayMonth,
    string Status, long? VoucherId, string? VoucherCode, long? AppliedPeriodId, string? AppliedPeriodStatus)
{
    public string KindText => Kind == "REWARD" ? "Khen thưởng" : "Kỷ luật";
    public string HandlingText => Handling switch { "PAYROLL" => Kind == "REWARD" ? "Cộng vào lương" : "Trừ vào lương", "CASH" => Kind == "REWARD" ? "Chi riêng" : "Thu riêng", _ => "Chỉ ghi nhận" };
    public string PeriodText => PayYear is null ? "—" : $"{PayMonth:00}/{PayYear}";
    public string StatusText => Status == "CANCELLED" ? "Đã hủy" : Handling == "CASH" ? "Đã thu/chi" : AppliedPeriodId is not null ? AppliedPeriodStatus == "CLOSED" ? "Đã chốt vào lương" : "Đã tính vào lương" : Handling == "PAYROLL" ? "Chờ tính lương" : "Đã ghi nhận";
}
public sealed record PayrollPayRequest(long PeriodId, int Revision, long EmployeeId, DateTime PaymentDate, PaymentAllocationDraft Payment);
public sealed record EmployeeMoneyResult(long VoucherId, string VoucherCode, bool AlreadyPosted = false);

public interface IEmployeeMoneyService
{
    Task<IReadOnlyList<EmployeeMoneyOption>> GetEmployeesAsync(AuthenticatedSession session, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeAward>> ListAsync(DateTime from, DateTime to, AuthenticatedSession session, CancellationToken ct = default);
    Task<EmployeeAward> CreateAsync(EmployeeAwardRequest request, AuthenticatedSession session, CancellationToken ct = default);
    Task CancelAsync(long id, string reason, AuthenticatedSession session, CancellationToken ct = default);
    Task<EmployeeMoneyResult> PaySalaryAsync(PayrollPayRequest request, AuthenticatedSession session, CancellationToken ct = default);
    Task CancelSalaryPaymentAsync(long periodId, long employeeId, long voucherId, string reason, AuthenticatedSession session, CancellationToken ct = default);
}

public static class EmployeeMoneyRules
{
    public static EmployeeAwardRequest Validate(EmployeeAwardRequest request)
    {
        if (request.RequestId == Guid.Empty || request.EmployeeId <= 0) throw new InvalidOperationException("Chọn nhân viên và tạo lại yêu cầu.");
        if (request.Kind is not ("REWARD" or "DISCIPLINE") || request.Handling is not ("NONE" or "PAYROLL" or "CASH")) throw new InvalidOperationException("Loại khoản phát sinh hoặc cách xử lý không hợp lệ.");
        var description = (request.Description ?? "").Trim();
        if (description.Length is < 1 or > 500) throw new InvalidOperationException("Nhập nội dung từ 1 đến 500 ký tự.");
        if (request.EventDate.Year is < 1900 or > 9998) throw new InvalidOperationException("Chọn ngày phát sinh từ năm 1900 đến 9998.");
        if (request.Handling == "NONE")
        {
            if (request.Amount != 0 || request.Payment is not null || request.PayYear is not null || request.PayMonth is not null || request.CategoryId is not null)
                throw new InvalidOperationException("Chỉ ghi nhận không có tiền, kỳ lương hoặc thanh toán.");
        }
        else
        {
            ValidateAmount(request.Amount);
            if (request.Handling == "PAYROLL")
            {
                if (request.PayYear is null or < 1900 or > 9998 || request.PayMonth is null or < 1 or > 12 || request.Payment is not null || request.CategoryId is not null)
                    throw new InvalidOperationException("Chọn kỳ lương hợp lệ; khoản vào lương không thu/chi riêng.");
            }
            else
            {
                if (request.PayYear is not null || request.PayMonth is not null || request.CategoryId is not > 0) throw new InvalidOperationException("Thu/chi riêng cần loại thu/chi và không gắn kỳ lương.");
                ValidatePayment(request.Payment, request.Amount);
            }
        }
        return request with { Description = description, EventDate = request.EventDate.Date };
    }
    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > 999999999999999m || amount != decimal.Truncate(amount)) throw new InvalidOperationException("Số tiền VND phải là số nguyên lớn hơn 0 và không vượt 999.999.999.999.999.");
    }
    public static void ValidatePayment(PaymentAllocationDraft? payment, decimal expected)
    {
        ValidateAmount(expected);
        if (payment is null || payment.AmountVnd != expected) throw new InvalidOperationException("Số tiền thu/chi phải bằng toàn bộ số tiền cần thanh toán.");
        var errors = PaymentAllocationValidator.Validate([payment], expected);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }
}
