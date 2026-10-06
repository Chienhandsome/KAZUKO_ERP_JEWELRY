using Kazuko.Core.Authentication;

namespace Kazuko.Core.Sales;

public interface ISalesCheckoutApplicationService
{
    Task<SalesCheckoutResult> PostAsync(SalesCheckoutDraft draft, AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public interface ISalesQueryRepository
{
    Task<IReadOnlyList<SalesCustomerOption>> SearchCustomersAsync(string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalespersonOption>> GetSalespeopleAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<SalesProductOption?> FindAvailableProductAsync(string productCode, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesProductOption>> SearchAvailableProductsAsync(string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<int> CountMatchingFinishedProductsAsync(string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OldGoldMaterialOption>> GetOldGoldMaterialsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesLocationOption>> GetOldGoldLocationsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<SalesCheckoutDetail?> GetCheckoutAsync(long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesCheckoutListItem>> SearchPostedCheckoutsAsync(
        SalesCheckoutSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
    Task<long?> GetAdjacentCheckoutIdAsync(long checkoutId, bool next, AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public interface ISalesAuthorizationService
{
    Task<SalesPermissionSnapshot> GetPermissionsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public interface ISalesPrintDataService
{
    Task<SalesReceiptDto> GetReceiptAsync(long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default);
}
