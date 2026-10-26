using System.Text;
using System.Text.RegularExpressions;
using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public sealed record VoucherCategory(long Id, string VoucherType, string Code, string Name, bool IsActive)
{
    public override string ToString() => Name;
}

public sealed record VoucherCategorySaveRequest(long? Id, string VoucherType, string Code, string Name, bool IsActive);

public interface IVoucherCategoryService
{
    Task<IReadOnlyList<VoucherCategory>> ListAsync(string voucherType, bool includeInactive, AuthenticatedSession session, CancellationToken ct = default);
    Task<VoucherCategory> SaveAsync(VoucherCategorySaveRequest request, AuthenticatedSession session, CancellationToken ct = default);
    Task DeleteAsync(long id, string voucherType, AuthenticatedSession session, CancellationToken ct = default);
}

public static class VoucherCategoryRules
{
    public static string NormalizeType(string value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        FinancialVoucherTypes.In => FinancialVoucherTypes.In,
        FinancialVoucherTypes.Out => FinancialVoucherTypes.Out,
        _ => throw new ArgumentException("Loại phiếu phải là Thu hoặc Chi.")
    };
    public static VoucherCategorySaveRequest Normalize(VoucherCategorySaveRequest request)
    {
        var type = NormalizeType(request.VoucherType);
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        if (!Regex.IsMatch(code, "^[A-Z0-9_-]{1,40}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Mã loại gồm 1–40 ký tự chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.");
        if (name.Length is < 1 or > 150) throw new ArgumentException("Tên loại phải có từ 1 đến 150 ký tự.");
        if (request.Id is <= 0) throw new ArgumentException("Loại thu/chi không hợp lệ.");
        return request with { VoucherType = type, Code = code, Name = name };
    }
}
