using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdSalesAuthorizationService : ISalesAuthorizationService
{
    private static readonly SalesPermissionSnapshot NoPermissions =
        new(false, false, false, false, false, false);

    public async Task<SalesPermissionSnapshot> GetPermissionsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.UserId <= 0 || session.BranchId <= 0)
            return NoPermissions;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT R.CODE,
                   MAX(CASE WHEN P.CODE = @POST AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @OVERRIDE AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @DEBT AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @SALESPERSON AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @HISTORY AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @PRINT AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END)
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            JOIN BRANCHES B ON B.ID = @BRANCH_ID
                           AND B.COMPANY_ID = U.COMPANY_ID
                           AND B.IS_ACTIVE = TRUE
            LEFT JOIN ROLE_PERMISSIONS RP ON RP.ROLE_ID = R.ID
            LEFT JOIN PERMISSIONS P ON P.ID = RP.PERMISSION_ID
            WHERE U.ID = @USER_ID
              AND U.IS_ACTIVE = TRUE
            GROUP BY R.CODE
            """, connection);
        command.Parameters.Add("@USER_ID", FbDbType.BigInt).Value = session.UserId;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@POST", FbDbType.VarChar, 80).Value = SalesPermissions.CheckoutPost;
        command.Parameters.Add("@OVERRIDE", FbDbType.VarChar, 80).Value = SalesPermissions.PriceOverride;
        command.Parameters.Add("@DEBT", FbDbType.VarChar, 80).Value = SalesPermissions.DebtApprove;
        command.Parameters.Add("@SALESPERSON", FbDbType.VarChar, 80).Value = SalesPermissions.SalespersonChange;
        command.Parameters.Add("@HISTORY", FbDbType.VarChar, 80).Value = SalesPermissions.HistoryRead;
        command.Parameters.Add("@PRINT", FbDbType.VarChar, 80).Value = SalesPermissions.ReceiptPrint;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return NoPermissions;

        if (string.Equals(reader.GetString(0).Trim(), "ADMIN", StringComparison.OrdinalIgnoreCase))
            return new SalesPermissionSnapshot(true, true, true, true, true, true);

        return new SalesPermissionSnapshot(
            ReadAllowed(reader, 1),
            ReadAllowed(reader, 2),
            ReadAllowed(reader, 3),
            ReadAllowed(reader, 4),
            ReadAllowed(reader, 5),
            ReadAllowed(reader, 6));
    }

    private static bool ReadAllowed(FbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToInt32(reader.GetValue(ordinal)) == 1;
}
