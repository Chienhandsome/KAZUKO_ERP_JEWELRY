using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Customers.Tests;

internal sealed class CustomerRepositoryFake : ICustomerRepository
{
    public static CustomerListItem Sample(long id = 1, DateTime? lastPurchase = null) => new(
        id, $"KH{id:000}", "Nguyễn Thị Minh Châu", "0901234567", "", "Thân thiết", "TP. Hồ Chí Minh",
        25470000m, 1000000m, lastPurchase, true, "", "", "", "", "", "", 0, 0, null, null, 1, null);

    public IReadOnlyList<CustomerListItem> Customers { get; set; } = [Sample()];
    public CustomerDebtSnapshot Snapshot { get; set; } = new(25470000m, 24470000m, 1000000m, 500000m);
    public IReadOnlyList<CustomerTopSalesItem> Top { get; set; } = [new(1, "KH001", "Nguyễn Thị Minh Châu", "0901234567", "Thân thiết", 2, 25470000m)];
    public TaskCompletionSource<IReadOnlyList<CustomerTopSalesItem>>? PendingTop { get; set; }
    public int TopCalls { get; private set; }

    public Task<IReadOnlyList<CustomerGroupOption>> GetGroupsAsync(bool activeOnly = true, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerGroupOption>>([new(1, "TT", "Thân thiết", true, true)]);
    public Task<IReadOnlyList<CustomerListItem>> SearchAsync(CustomerSearchFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerListItem>>(Customers.Where(x => (filter.IsActive is null || x.IsActive == filter.IsActive) && (filter.IsWalkIn is null || x.IsWalkIn == filter.IsWalkIn)).ToList());
    public Task<IReadOnlyList<CustomerPurchaseHistoryItem>> GetPurchaseHistoryAsync(long customerId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerPurchaseHistoryItem>>([new(1, DateTime.Today, "MB26/00001", 25470000m, 24470000m)]);
    public Task<CustomerDebtSnapshot> GetDebtSnapshotAsync(long customerId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    public Task<IReadOnlyList<CustomerTopSalesItem>> GetTopCustomersAsync(DateTime fromDate, DateTime toDate, int topN, CancellationToken cancellationToken = default)
    {
        TopCalls++;
        return PendingTop?.Task ?? Task.FromResult(Top);
    }
    public Task<IReadOnlyList<CustomerSalesSummary>> GetSalesSummaryAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerSalesSummary>>([new(false, 2, 25470000m), new(true, 1, 100000m)]);
    public Task<CustomerGroupOption> CreateGroupAsync(string name, AuthenticatedSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CustomerDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> FindDuplicateWarningAsync(CustomerSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CustomerEditResult> SaveAsync(CustomerSaveRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetActiveAsync(long id, bool isActive, AuthenticatedSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class CustomerEditorFake : ICustomerEditorService
{
    public CustomerEditResult? ShowCreate() => null;
    public CustomerEditResult? ShowEdit(long customerId) => null;
}

internal sealed class SettlementDialogFake : IDebtSettlementDialogService
{
    public DebtSettlementDialogRequest? Request { get; private set; }
    public bool Confirm { get; set; }
    public DebtSettlementDialogResult? ShowDialog(DebtSettlementDialogRequest request)
    {
        Request = request;
        return Confirm ? new(request.VoucherType, DateTime.Today, request.Allocations, 1000000m, 0, 0, 1, null, null, "Thu thử", null) : null;
    }
}

public class DebtRepositoryProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);

    internal static IDebtWorkbenchRepository Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = Create<IDebtWorkbenchRepository, DebtRepositoryProxy>();
        ((DebtRepositoryProxy)proxy).Handler = handler;
        return proxy;
    }
}

internal static class CustomerTestContext
{
    public static CurrentSessionAccessor Session()
    {
        var session = new CurrentSessionAccessor();
        session.Set(new(1, "tester", "Tester", "ADMIN", 1, "CN01", "Chi nhánh 1"));
        return session;
    }

    public static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new TimeoutException("Customer WPF check timed out.");
        if (failure is not null) throw new Exception("Customer check failed.", failure);
    }
}
