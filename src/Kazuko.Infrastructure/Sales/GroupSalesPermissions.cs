using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Sales;
using Kazuko.Core.UserManagement;

namespace Kazuko.Infrastructure.Sales;

internal static class GroupSalesPermissions
{
    internal static async Task<SalesPermissionSnapshot> RestrictAsync(FbConnection connection, FbTransaction? transaction,
        long userId, SalesPermissionSnapshot legacy, CancellationToken cancellationToken)
    {
        var isAdmin = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT CASE WHEN R.CODE = 'ADMIN' AND U.IS_ACTIVE = TRUE AND R.IS_ACTIVE = TRUE THEN TRUE ELSE FALSE END
            FROM USERS U JOIN ROLES R ON R.ID = U.ROLE_ID WHERE U.ID = @Id
            """, new { Id = userId }, transaction, cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<GroupPermissionRow>(new CommandDefinition("""
            SELECT P.GROUP_ID AS GroupId, P.FUNCTION_CODE AS FunctionCode, P.CAN_VIEW AS CanView,
                   P.CAN_ADD AS CanAdd, P.CAN_EDIT AS CanEdit, P.CAN_DELETE AS CanDelete, P.IS_LOCKED AS IsLocked
            FROM USERS U JOIN USER_GROUPS G ON G.ID = U.GROUP_ID AND G.IS_ACTIVE = TRUE AND G.COMPANY_ID = U.COMPANY_ID
            JOIN GROUP_PERMISSIONS P ON P.GROUP_ID = G.ID AND P.COMPANY_ID = G.COMPANY_ID
            WHERE U.ID = @Id AND U.IS_ACTIVE = TRUE
            """, new { Id = userId }, transaction, cancellationToken: cancellationToken));
        var group = new PermissionSnapshot(isAdmin, rows);
        return legacy with
        {
            CanPost = legacy.CanPost && (group.Allows("SALES_RETAIL", PermissionAction.Add) || group.Allows("OLD_GOLD_PURCHASE", PermissionAction.Add)),
            CanPostWholesale = legacy.CanPostWholesale && group.Allows("SALES_WHOLESALE", PermissionAction.Add),
            CanOverridePrice = legacy.CanOverridePrice && group.Allows("SALES_PRICE_OVERRIDE", PermissionAction.Edit),
            CanApproveDebt = legacy.CanApproveDebt && group.Allows("SALES_DEBT_APPROVE", PermissionAction.Edit),
            CanChangeSalesperson = legacy.CanChangeSalesperson && group.Allows("SALES_CHANGE_EMPLOYEE", PermissionAction.Edit),
            CanReadHistory = legacy.CanReadHistory && group.Allows("SALES_HISTORY"),
            CanPrintReceipt = legacy.CanPrintReceipt && (group.Allows("SALES_HISTORY") || group.Allows("SALES_RETAIL") || group.Allows("SALES_WHOLESALE") || group.Allows("OLD_GOLD_PURCHASE"))
        };
    }
}
