namespace Kazuko.Core.UserManagement;

public enum PermissionAction { View, Add, Edit, Delete }

public sealed record PermissionRequirement(string Function, PermissionAction Action = PermissionAction.View);

public sealed class PermissionSnapshot(bool isSystemAdmin, IEnumerable<GroupPermissionRow> rows)
{
    private readonly Dictionary<string, GroupPermissionRow> _rows = rows.ToDictionary(x => x.FunctionCode, StringComparer.OrdinalIgnoreCase);
    public bool IsSystemAdmin { get; } = isSystemAdmin;
    public string Signature => IsSystemAdmin + ":" + string.Join(";", _rows.Values.OrderBy(x => x.FunctionCode).Select(x => x.ToString()));
    public bool Allows(string function, PermissionAction action = PermissionAction.View)
    {
        if (IsSystemAdmin) return true;
        if (!_rows.TryGetValue(function, out var row) || row.IsLocked || !row.CanView) return false;
        return action switch { PermissionAction.View => true, PermissionAction.Add => row.CanAdd,
            PermissionAction.Edit => row.CanEdit, PermissionAction.Delete => row.CanDelete, _ => false };
    }
    public static PermissionSnapshot Denied { get; } = new(false, []);
}

public interface IAuthorizationService
{
    PermissionSnapshot Current { get; }
    event EventHandler? Changed;
    Task RefreshAsync(CancellationToken cancellationToken = default);
    void Clear();
    Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default);
}

public static class NavigationAccess
{
    public static IReadOnlyList<string> Functions(string route) => route.ToUpperInvariant() switch
    {
        "DASHBOARD" => [],
        "PRODUCTION" => ["PROD_ORDER", "PROD_MATERIAL_ISSUE", "PROD_GOLD_RECONCILE"],
        "WAREHOUSE" => ["INV_STOCK_VIEW", "INV_HISTORY", "INV_MATERIAL_RECEIPT", "INV_FINISHED_RECEIPT", "INV_TRANSFER", "INV_STOCKTAKE", "INV_SALES_INTAKE"],
        "SALES" => ["SALES_RETAIL", "SALES_WHOLESALE", "OLD_GOLD_PURCHASE", "CRAFT_ORDER", "SALES_HISTORY"],
        "DEBT" => ["DEBT_CUSTOMER", "DEBT_WORKER_GOLD", "DEBT_SETTLEMENT"],
        "FINANCE" => ["FIN_FUND", "FIN_CASH_IN", "FIN_CASH_OUT"],
        "CUSTOMER" => ["CAT_CUSTOMERS"], "SUPPLIER" => ["CAT_SUPPLIERS"], "HR" => ["CAT_EMPLOYEES", "HR_PAYROLL"],
        "REPORT" => ["REP_VIEW"], "PRICE_BOARD" or "WAREHOUSE_GOLD_PRICES" => ["CAT_GOLD_PRICE"],
        "WAREHOUSE_EXTERNAL_WORKERS" => ["CAT_EXTERNAL_WORKERS"], "USER_MANAGEMENT" => ["SYS_USER_MANAGEMENT"],
        "SETTINGS" => ["SYS_SETTINGS", "SYS_DATABASE"], _ => []
    };
    public static bool Allows(PermissionSnapshot snapshot, string route) => route.Equals("DASHBOARD", StringComparison.OrdinalIgnoreCase)
        || Functions(route).Any(f => snapshot.Allows(f));
}
