using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.UserManagement;
using Dapper;
using FirebirdSql.Data.FirebirdClient;

public static class AuthorizationChecks
{
    public static async Task RunDatabaseAsync(FbConnection db, long userId, long groupId, long branchId, string branchCode, long adminId)
    {
        var session = new CurrentSessionAccessor();
        session.Set(new(userId, "verify.cashier", "Verify", "ADMIN", branchId, branchCode, branchCode));
        var auth = new FirebirdAuthorizationService(session);
        await auth.RefreshAsync();
        Check(!auth.Current.IsSystemAdmin && !auth.Current.Allows("SYS_USER_MANAGEMENT"), "database identity overrides claimed role");
        await db.ExecuteAsync("UPDATE GROUP_PERMISSIONS SET CAN_VIEW=FALSE, CAN_ADD=FALSE, CAN_EDIT=FALSE, CAN_DELETE=FALSE WHERE GROUP_ID=@groupId AND FUNCTION_CODE='CAT_EMPLOYEES'", new { groupId });
        await auth.RefreshAsync();
        Check(NavigationAccess.Allows(auth.Current, "SALES") && !NavigationAccess.Allows(auth.Current, "HR"), "cashier navigation honors actual group changes");
        var target = new FakeSettlement();
        var guarded = AuthorizedServiceProxy<IFinancialSettlementService>.Create(target, auth, session);
        await db.ExecuteAsync("UPDATE GROUP_PERMISSIONS SET CAN_VIEW=TRUE, CAN_ADD=TRUE, IS_LOCKED=FALSE WHERE GROUP_ID=@groupId AND FUNCTION_CODE='DEBT_SETTLEMENT'", new { groupId });
        await guarded.PostAsync(null!, session.Current!);
        Check(target.Calls == 1, "live database grant accepted");
        await db.ExecuteAsync("UPDATE GROUP_PERMISSIONS SET CAN_ADD=FALSE WHERE GROUP_ID=@groupId AND FUNCTION_CODE='DEBT_SETTLEMENT'", new { groupId });
        await DeniedAsync(() => guarded.PostAsync(null!, session.Current!));
        Check(target.Calls == 1, "live database revocation blocks before side effects");
        await db.ExecuteAsync("UPDATE USER_GROUPS SET IS_ACTIVE=FALSE WHERE ID=@groupId", new { groupId });
        await auth.RefreshAsync();
        Check(!auth.Current.Allows("SALES_RETAIL"), "inactive group denied");
        await db.ExecuteAsync("UPDATE USER_GROUPS SET IS_ACTIVE=TRUE WHERE ID=@groupId", new { groupId });
        session.Set(session.Current! with { UserId = adminId, RoleCode = "SALE" });
        await auth.RefreshAsync();
        Check(auth.Current.IsSystemAdmin && auth.Current.Allows("SYS_DATABASE", PermissionAction.Delete), "actual admin retained without group dependency");
        var repository = new FirebirdUserManagementRepository();
        var grants = await repository.GetGroupPermissionsAsync(groupId);
        await repository.SaveGroupPermissionsAsync(groupId, grants.Select(r => new PermissionSaveRow(r.FunctionCode, r.CanView, r.CanAdd, r.CanEdit, r.CanDelete, r.IsLocked)).ToArray(), adminId);
        Check(await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM AUDIT_LOG WHERE EVENT_TYPE='GROUP_PERMISSIONS_UPDATED'") > 0, "permission changes audited");
        var adminGroupId = await db.QuerySingleAsync<long>("SELECT ID FROM USER_GROUPS WHERE CODE='ADMIN'");
        await repository.SaveGroupPermissionsAsync(adminGroupId, [], adminId);
        Check((await repository.GetGroupPermissionsAsync(adminGroupId)).Count == SystemFunctionCatalog.All.Count, "admin group retains full catalog when submitted empty");
        Console.WriteLine("DATABASE_AUTHORIZATION_CHECKS_PASSED");
    }
    public static async Task RunAsync()
    {
        var add = Rights("CAT_CUSTOMERS", true, true, false, false);
        Check(add.Allows("CAT_CUSTOMERS", PermissionAction.Add), "add allowed");
        Check(!add.Allows("CAT_CUSTOMERS", PermissionAction.Edit) && !add.Allows("CAT_CUSTOMERS", PermissionAction.Delete), "add independent");
        Check(!Rights("CAT_CUSTOMERS", false, true, true, true).Allows("CAT_CUSTOMERS", PermissionAction.Add), "view prerequisite");
        Check(!Rights("CAT_CUSTOMERS", true, true, true, true, true).Allows("CAT_CUSTOMERS"), "locked overrides all grants");
        Check(!PermissionSnapshot.Denied.Allows("CAT_CUSTOMERS"), "missing permissions deny");
        Check(!NavigationAccess.Allows(add, "USER_MANAGEMENT") && NavigationAccess.Allows(add, "CUSTOMER"), "menu filter");
        Check(!NavigationAccess.Allows(add, "UNKNOWN"), "unknown navigation denied");
        Check(BackendAccess.Allows("IProductionApplicationService", "CompleteAsync", [], Rights("PROD_COMPLETE", true, false, true, false)), "complete use case needs no manual debt permission");
        Check(!BackendAccess.Allows("IProductionApplicationService", "CompleteAsync", [], Rights("PROD_ORDER", true, true, true, true)), "complete requires explicit specialized permission");
        var session = new CurrentSessionAccessor();
        session.Set(new(10, "test", "Test", "ADMIN", 1, "A", "A")); // claimed role must not bypass trusted snapshot
        var authorization = new FakeAuthorization { Current = Rights("DEBT_SETTLEMENT", true, false, false, false) };
        var target = new FakeSettlement();
        var guarded = AuthorizedServiceProxy<IFinancialSettlementService>.Create(target, authorization, session);
        await DeniedAsync(() => guarded.PostAsync(null!, session.Current!));
        Check(target.Calls == 0, "denial occurs before side effects and claimed admin ignored");
        authorization.Current = Rights("DEBT_SETTLEMENT", true, true, false, false);
        await guarded.PostAsync(null!, session.Current!);
        Check(target.Calls == 1, "granted initiating action runs without extra voucher/warehouse permissions");
        authorization.Current = PermissionSnapshot.Denied;
        await DeniedAsync(() => guarded.PostAsync(null!, session.Current!));
        Check(target.Calls == 1 && authorization.Refreshes == 3, "revoked grant rechecked on each call");
        authorization.Current = new(true, []);
        await guarded.PostAsync(null!, session.Current!);
        Check(target.Calls == 2, "trusted system admin bypass");
        await DeniedAsync(() => guarded.PostAsync(null!, session.Current! with { UserId = 11 }));
        Check(!BackendAccess.Allows("IUnmappedService", "SaveAsync", [], authorization.Current), "unmapped service denied");
        Console.WriteLine("AUTHORIZATION_CHECKS_PASSED");
    }
    private static PermissionSnapshot Rights(string f, bool view, bool add, bool edit, bool delete, bool locked = false)
        => new(false, [new(1, f, view, add, edit, delete, locked)]);
    private static async Task DeniedAsync(Func<Task> call)
    {
        try { await call(); } catch (UnauthorizedAccessException) { return; }
        throw new Exception("Expected permission denial.");
    }
    private static void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private sealed class FakeAuthorization : IAuthorizationService
    {
        public PermissionSnapshot Current { get; set; } = PermissionSnapshot.Denied;
        public int Refreshes { get; private set; }
        public event EventHandler? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) { Refreshes++; return Task.CompletedTask; }
        public void Clear() => Current = PermissionSnapshot.Denied;
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeSettlement : IFinancialSettlementService
    {
        public int Calls { get; private set; }
        public Task<FinancialSettlementResult> PostAsync(FinancialSettlementDraft draft, AuthenticatedSession session, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult<FinancialSettlementResult>(null!); }
    }
}
