using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.UserManagement;

public sealed class FirebirdAuthorizationService(ICurrentSessionAccessor session) : IAuthorizationService
{
    public PermissionSnapshot Current { get; private set; } = PermissionSnapshot.Denied;
    public event EventHandler? Changed;
    public void Clear() { Current = PermissionSnapshot.Denied; Changed?.Invoke(this, EventArgs.Empty); }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var userId = session.Current?.UserId;
        var snapshot = PermissionSnapshot.Denied;
        if (userId is > 0)
        {
            await using var connection = FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var identity = await connection.QuerySingleOrDefaultAsync<Identity>(new CommandDefinition("""
                SELECT U.GROUP_ID AS GroupId, R.CODE AS RoleCode, G.IS_ACTIVE AS GroupActive
                FROM USERS U LEFT JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
                LEFT JOIN USER_GROUPS G ON G.ID = U.GROUP_ID AND G.COMPANY_ID = U.COMPANY_ID
                WHERE U.ID = @Id AND U.IS_ACTIVE = TRUE
                  AND U.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """, new { Id = userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (identity?.RoleCode == "ADMIN") snapshot = new(true, []);
            else if (identity is { GroupId: > 0, GroupActive: true, RoleCode: not null })
            {
                var rows = await connection.QueryAsync<GroupPermissionRow>(new CommandDefinition("""
                    SELECT GROUP_ID AS GroupId, FUNCTION_CODE AS FunctionCode, CAN_VIEW AS CanView,
                           CAN_ADD AS CanAdd, CAN_EDIT AS CanEdit, CAN_DELETE AS CanDelete, IS_LOCKED AS IsLocked
                    FROM GROUP_PERMISSIONS WHERE GROUP_ID = @Id
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, new { Id = identity.GroupId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
                snapshot = new(false, rows);
            }
        }
        if (session.Current?.UserId != userId) return;
        var changed = Current.Signature != snapshot.Signature;
        Current = snapshot;
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default)
    {
        try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
        catch { Clear(); throw; }
        foreach (var requirement in requirements)
            if (!Current.Allows(requirement.Function, requirement.Action))
                throw new UnauthorizedAccessException($"Bạn không có quyền {requirement.Action switch { PermissionAction.View => "xem", PermissionAction.Add => "thêm", PermissionAction.Edit => "sửa", _ => "xóa/hủy" }}: {SystemFunctionCatalog.Find(requirement.Function)?.Name ?? requirement.Function}.");
    }
    private sealed class Identity
    {
        public long? GroupId { get; set; }
        public string? RoleCode { get; set; }
        public bool? GroupActive { get; set; }
    }
}
