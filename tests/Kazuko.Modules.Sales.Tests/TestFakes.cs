using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Tests;

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

    public Task<IReadOnlyList<SalesCustomerOption>> SearchCustomersAsync(
        string searchText, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalesCustomerOption>>(Customers);

    public Task<IReadOnlyList<SalespersonOption>> GetSalespeopleAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalespersonOption>>(Staff);

    public Task<SalesProductOption?> FindAvailableProductAsync(
        string productCode, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            Product is not null &&
            string.Equals(Product.ProductCode, productCode, StringComparison.OrdinalIgnoreCase)
                ? Product
                : null);

    public Task<IReadOnlyList<OldGoldMaterialOption>> GetOldGoldMaterialsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OldGoldMaterialOption>>(Materials);

    public Task<IReadOnlyList<SalesLocationOption>> GetOldGoldLocationsAsync(
        AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalesLocationOption>>(Locations);

    public Task<SalesCheckoutDetail?> GetCheckoutAsync(
        long checkoutId, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<SalesCheckoutDetail?>(null);

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
            session, payments, receipts);
        return (vm, checkout, payments, receipts, queries);
    }
}
