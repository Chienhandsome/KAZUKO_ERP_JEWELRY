using Kazuko.Core.Authentication;

namespace Kazuko.Core.Suppliers;

public sealed record SupplierListItem(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Email,
    string SupplierGroupName,
    string Region,
    decimal TotalPurchases,
    decimal CurrentDebt,
    DateTime? LastPurchaseDate,
    bool IsActive,
    string Address,
    string ContactName,
    string ContactTitle,
    string TaxCode,
    string Notes,
    long SupplierGroupId);

public sealed record SupplierDetail(
    long Id,
    string Code,
    string Name,
    long SupplierGroupId,
    string SupplierGroupName,
    string ContactName,
    string ContactTitle,
    string Phone,
    string Email,
    string Address,
    string Region,
    string TaxCode,
    string Notes,
    bool IsActive,
    string BrandCode,
    string Tccs);

public sealed record SupplierGroupOption(long Id, string Code, string Name, bool IsActive, bool IsDefault)
{
    public override string ToString() => Name;
}

public sealed record SupplierSearchFilter(
    string SearchText,
    long GroupId,
    string Region,
    bool? IsActive);

public sealed record SupplierSaveRequest(
    long? Id,
    string Name,
    long SupplierGroupId,
    string ContactName,
    string ContactTitle,
    string Phone,
    string Email,
    string Address,
    string Region,
    string TaxCode,
    string Notes,
    bool IsActive,
    string BrandCode,
    string Tccs);

public sealed record SupplierEditResult(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Address,
    bool IsActive);

public interface ISupplierEditorService
{
    SupplierEditResult? ShowCreate();
    SupplierEditResult? ShowEdit(long supplierId);
}

public interface ISupplierRepository
{
    Task<IReadOnlyList<SupplierGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<SupplierDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<string> FindDuplicateWarningAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierEditResult> SaveAsync(
        SupplierSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
