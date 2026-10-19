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

public sealed record SupplierGroupDetail(
    long Id,
    string Code,
    string Name,
    string Description,
    bool IsActive,
    bool IsDefault,
    int SupplierCount);

public sealed record SupplierGroupSaveRequest(
    long? Id,
    string Name,
    string Description);

public sealed record SupplierPurchaseHistoryItem(
    long DebtId,
    DateTime PurchaseDate,
    string DocumentCode,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string StatusCode);

public sealed record SupplierTopPurchaseItem(
    long SupplierId,
    string Code,
    string Name,
    string Phone,
    string SupplierGroupName,
    int PurchaseCount,
    decimal TotalPurchases);

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

    Task<IReadOnlyList<SupplierGroupDetail>> GetGroupDetailsAsync(
        CancellationToken cancellationToken = default);

    Task<SupplierGroupDetail?> GetGroupByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<long> SaveGroupAsync(
        SupplierGroupSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierPurchaseHistoryItem>> GetPurchaseHistoryAsync(
        long supplierId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierTopPurchaseItem>> GetTopSuppliersAsync(
        DateTime fromDate,
        DateTime toDate,
        int topN,
        CancellationToken cancellationToken = default);
}
