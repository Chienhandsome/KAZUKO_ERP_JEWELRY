using System.Reflection;
using System.Runtime.ExceptionServices;
using Kazuko.Core.Authentication;
using Kazuko.Core.UserManagement;

namespace Kazuko.Infrastructure.UserManagement;

/// <summary>Application-boundary guard. Internal posting helpers remain inside the originating authorized use case.</summary>
public class AuthorizedServiceProxy<T> : DispatchProxy where T : class
{
    private T _target = null!;
    private IAuthorizationService _authorization = null!;
    private ICurrentSessionAccessor _session = null!;
    public static T Create(T target, IAuthorizationService authorization, ICurrentSessionAccessor session)
    {
        var result = Create<T, AuthorizedServiceProxy<T>>();
        var proxy = (AuthorizedServiceProxy<T>)(object)result;
        proxy._target = target; proxy._authorization = authorization; proxy._session = session;
        return result;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(method);
        var args = arguments ?? [];
        if (method.ReturnType == typeof(Task)) return InvokeTaskAsync(method, args);
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(AuthorizedServiceProxy<T>).GetMethod(nameof(InvokeResultAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(method.ReturnType.GenericTypeArguments).Invoke(this, [method, args]);
        DemandAsync(method, args).GetAwaiter().GetResult();
        return InvokeTarget(method, args);
    }
    private async Task DemandAsync(MethodInfo method, object?[] args)
    {
        var cancellationToken = args.OfType<CancellationToken>().FirstOrDefault();
        await _authorization.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var actual = _session.Current;
        var supplied = args.OfType<AuthenticatedSession>().FirstOrDefault();
        var branchArgument = Array.FindIndex(method.GetParameters(), p => p.Name == "branchId");
        if (actual is null || supplied is not null && (supplied.UserId != actual.UserId || supplied.BranchId != actual.BranchId)
            || branchArgument >= 0 && args[branchArgument] is long branchId && branchId != actual.BranchId
            || !BackendAccess.Allows(typeof(T).Name, method.Name, args, _authorization.Current))
        {
            if (_authorization is FirebirdAuthorizationService && actual is not null)
                await Kazuko.Infrastructure.Data.AuditLogWriter.WriteAsync("PERMISSION_DENIED", actual.UserId,
                    actual.Username, actual.BranchId, typeof(T).Name, eventData: method.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này. Vui lòng liên hệ quản trị viên.");
        }
        if (typeof(T).Name == "IUserManagementRepository" && !_authorization.Current.IsSystemAdmin)
        {
            var first = args.FirstOrDefault();
            var groupId = method.Name == "SaveUserAsync" ? first?.GetType().GetProperty("GroupId")?.GetValue(first)
                : method.Name == "SaveGroupAsync" ? first?.GetType().GetProperty("Id")?.GetValue(first)
                : method.Name == "SaveGroupPermissionsAsync" ? first : null;
            if (groupId is long id)
            {
                await using var connection = Kazuko.Infrastructure.Data.FirebirdDatabase.CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new FirebirdSql.Data.FirebirdClient.FbCommand(
                    "SELECT CODE FROM USER_GROUPS WHERE ID = @ID", connection);
                command.Parameters.AddWithValue("@ID", id);
                if ((await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string)?.Trim() == "ADMIN")
                    throw new UnauthorizedAccessException("Chỉ Admin hệ thống được cấp hoặc thay đổi nhóm quản trị.");
            }
        }
        if (typeof(T).Name == "IFinanceDashboardRepository" && method.Name == "GetVoucherDetailAsync")
        {
            await using var connection = Kazuko.Infrastructure.Data.FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new FirebirdSql.Data.FirebirdClient.FbCommand(
                "SELECT VOUCHER_TYPE FROM CASH_VOUCHERS WHERE ID = @ID AND BRANCH_ID = @BRANCH", connection);
            command.Parameters.AddWithValue("@ID", args[0]); command.Parameters.AddWithValue("@BRANCH", actual.BranchId);
            var kind = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (kind is not null && !_authorization.Current.Allows(kind == "IN" ? "FIN_CASH_IN" : "FIN_CASH_OUT"))
                throw new UnauthorizedAccessException("Bạn không có quyền xem loại phiếu thu/chi này.");
        }
    }
    private async Task InvokeTaskAsync(MethodInfo method, object?[] args)
    {
        await DemandAsync(method, args).ConfigureAwait(false);
        await (Task)InvokeTarget(method, args)!;
    }
    private async Task<TResult> InvokeResultAsync<TResult>(MethodInfo method, object?[] args)
    {
        await DemandAsync(method, args).ConfigureAwait(false);
        return await (Task<TResult>)InvokeTarget(method, args)!;
    }
    private object? InvokeTarget(MethodInfo method, object?[] args)
    {
        try { return method.Invoke(_target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}
