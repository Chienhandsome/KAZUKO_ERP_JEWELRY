using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Tests;

public class UnusedDependencyProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException($"Unexpected call to {targetMethod?.Name} in this sales test.");
}

internal sealed class FakeSessionAccessor : ICurrentSessionAccessor
{
    public AuthenticatedSession? Current { get; private set; }
    public void Set(AuthenticatedSession session) => Current = session;
    public void Clear() => Current = null;
}

internal sealed class FakePaymentDialog : ISalesPaymentDialogService
{
    public SalesPaymentDialogResult? NextResult { get; set; }
    public int ShowCount { get; private set; }
    public SalesPaymentDialogRequest? LastRequest { get; private set; }

    public SalesPaymentDialogResult? Show(SalesPaymentDialogRequest request)
    {
        ShowCount++;
        LastRequest = request;
        return NextResult;
    }
}

internal sealed class FakeCustomerEditor : ICustomerEditorService
{
    public CustomerEditResult? NextResult { get; set; }
    public CustomerEditResult? ShowCreate() => NextResult;
    public CustomerEditResult? ShowEdit(long customerId) => NextResult;
}

internal sealed class FakeCustomerLookupDialog : ISalesCustomerLookupDialogService
{
    public SalesCustomerOption? NextResult { get; set; }
    public int ShowCount { get; private set; }

    public SalesCustomerOption? Show(
        Func<string, Task<IReadOnlyList<SalesCustomerOption>>> searchAsync,
        SalesCustomerOption? current)
    {
        ShowCount++;
        return NextResult ?? current;
    }
}

internal sealed class FakePriceOverrideDialog : ISalesPriceOverrideDialogService
{
    public bool NextResult { get; set; }
    public int ShowCount { get; private set; }

    public bool TryEdit(SellLineRowViewModel line)
    {
        ShowCount++;
        return NextResult;
    }
}

internal sealed class FakeReceiptPresenter : ISalesReceiptPresenter
{
    public int ShowCount { get; private set; }
    public long LastCheckoutId { get; private set; }

    public Task ShowAsync(long checkoutId, CancellationToken cancellationToken = default)
    {
        ShowCount++;
        LastCheckoutId = checkoutId;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCheckoutService : ISalesCheckoutApplicationService
{
    public int PostCount { get; private set; }
    public SalesCheckoutDraft? LastDraft { get; private set; }
    public Exception? Failure { get; set; }
    public SalesCheckoutResult Result { get; set; } = new(
        1, "MB26/00001", SalesCheckoutTypes.Sale,
        new SalesCheckoutTotals(100, 0, 100, 0, 0, 100, 100, 0, 0),
        10, null, false);

    public Task<SalesCheckoutResult> PostAsync(
        SalesCheckoutDraft draft, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        PostCount++;
        LastDraft = draft;
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeWholesaleSalesService : IWholesaleSalesApplicationService
{
    public Task<IReadOnlyList<WholesaleLocationOption>> GetSourceLocationsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WholesaleLocationOption>>(
            [new(1, "TP", "Kho thành phẩm", "FINISHED_GOODS", true)]);

    public Task<WholesaleProductOption?> FindAvailableProductAsync(
        string productCode, long? sourceLocationId, AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<WholesaleProductOption?>(null);

    public Task<IReadOnlyList<WholesaleProductOption>> SearchAvailableProductsAsync(
        string searchText, long? sourceLocationId, AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WholesaleProductOption>>([]);

    public Task<WholesaleSaleResult> PostAsync(
        WholesaleSaleDraft draft, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WholesaleSaleResult(1, "BSI-2026-000001", 2, 100, 100, 0));
}

internal sealed class FakeAuthService : ISalesAuthorizationService
{
    public SalesPermissionSnapshot Permissions { get; set; } =
        new(true, true, true, true, true, true);

    public Task<SalesPermissionSnapshot> GetPermissionsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(Permissions);
}

internal sealed class FakeQueryRepository : ISalesQueryRepository
{
    public List<SalesCustomerOption> Customers { get; } =
    [
        new(1, "WALK_IN", "Khách lẻ", null, true),
        new(2, "KH001", "Nguyễn Văn A", "0901", false)
    ];

    public List<SalespersonOption> Staff { get; } =
    [
        new(9, "admin", "Admin"),
        new(8, "sale", "NV Sale")
    ];

    public List<OldGoldMaterialOption> Materials { get; } =
    [
        new(5, "GOLD750", "Vàng 750", 50, 100, 77)
    ];

    public List<SalesLocationOption> Locations { get; } =
    [
        new(3, "RAW", "Kho NVL", true)
    ];

    public SalesProductOption? Product { get; set; } = new(
        100, "SP001", "Nhẫn", 50, "Giá 750", 20, "750", 1,
        3.25m, 0.75m, 2.5m, 111m, 25m, 8m);

    public List<SalesProductOption> Products { get; } = [];

    public FakeQueryRepository()
    {
        if (Product is not null)
            Products.Add(Product);
    }

    public Task<IReadOnlyList<SalesCustomerOption>> SearchCustomersAsync(
        string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalesCustomerOption>>(Customers);

    public Task<IReadOnlyList<SalespersonOption>> GetSalespeopleAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalespersonOption>>(Staff);

    public Task<SalesProductOption?> FindAvailableProductAsync(
        string productCode, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        var match = Products.FirstOrDefault(x =>
            string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (match is null
            && Product is not null
            && string.Equals(Product.ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
        {
            match = Product;
        }

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<SalesProductOption>> SearchAvailableProductsAsync(
        string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length == 0)
            return Task.FromResult<IReadOnlyList<SalesProductOption>>([]);

        var source = Products.Count > 0
            ? Products.AsEnumerable()
            : Product is null ? Enumerable.Empty<SalesProductOption>() : [Product];

        IReadOnlyList<SalesProductOption> matches = source
            .Where(x =>
                x.ProductCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToArray();
        return Task.FromResult(matches);
    }

    public Task<int> CountMatchingFinishedProductsAsync(
        string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        SearchAvailableProductsAsync(searchText, session, cancellationToken)
            .ContinueWith(t => t.Result.Count, cancellationToken);

    public Task<IReadOnlyList<OldGoldMaterialOption>> GetOldGoldMaterialsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OldGoldMaterialOption>>(Materials);

    public Task<IReadOnlyList<SalesLocationOption>> GetOldGoldLocationsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalesLocationOption>>(Locations);

    public Task<SalesCheckoutDetail?> GetCheckoutAsync(
        long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<SalesCheckoutDetail?>(null);

    public Task<IReadOnlyList<SalesCheckoutListItem>> SearchPostedCheckoutsAsync(
        SalesCheckoutSearchFilter filter, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalesCheckoutListItem>>([]);

    public Task<long?> GetAdjacentCheckoutIdAsync(
        long checkoutId, bool next, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(null);
}

internal sealed class FakeCashFunds : ICashFundRepository
{
    public Task<IReadOnlyList<CashFundOption>> GetActiveFundsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CashFundOption>>([new(1, "QTM", "Quỹ chính", true)]);
}

internal sealed class FakeBanks : IBankAccountRepository
{
    public Task<IReadOnlyList<BankAccountOption>> GetActiveAccountsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BankAccountOption>>([new(2, "BIDV", "BIDV", "001")]);
}

internal static class SalesTestFactory
{
    public static (SalesViewModel Vm, FakeCheckoutService Checkout, FakePaymentDialog Payments, FakeReceiptPresenter Receipts, FakeQueryRepository Queries)
        Create(
            SalesPermissionSnapshot? permissions = null,
            SalesPaymentDialogResult? paymentResult = null)
    {
        var session = new FakeSessionAccessor();
        session.Set(new AuthenticatedSession(9, "admin", "Admin", "ADMIN", 1, "BR", "Branch"));
        var queries = new FakeQueryRepository();
        var checkout = new FakeCheckoutService();
        var auth = new FakeAuthService { Permissions = permissions ?? new(true, true, true, true, true, true) };
        var payments = new FakePaymentDialog
        {
            NextResult = paymentResult ?? new SalesPaymentDialogResult(
                [new(PaymentMethods.Cash, 311m, 1, null, null)], 0)
        };
        var receipts = new FakeReceiptPresenter();
        var vm = new SalesViewModel(
            queries, checkout, auth, new FakeCashFunds(), new FakeBanks(),
            session, payments, new FakeCustomerEditor(), new FakePriceOverrideDialog(), receipts,
            DispatchProxy.Create<ICraftOrderApplicationService, UnusedDependencyProxy>(),
            new FakeWholesaleSalesService(),
            DispatchProxy.Create<IWarehouseRepository, UnusedDependencyProxy>(),
            DispatchProxy.Create<IProductionOrderEditorService, UnusedDependencyProxy>());
        return (vm, checkout, payments, receipts, queries);
    }
}
