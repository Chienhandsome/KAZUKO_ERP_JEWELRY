using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdMasterCustomersReportService : IMasterCustomersReportService
{
    public async Task<MasterCustomersReportResult> GetAsync(
        MasterCustomersReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);

        var rows = new List<MasterCustomersReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID,
                   C.CODE,
                   C.NAME,
                   COALESCE(C.PHONE, ''),
                   COALESCE(C.EMAIL, ''),
                   COALESCE(C.ADDRESS, ''),
                   COALESCE(G.NAME, ''),
                   COALESCE(C.TAX_CODE, ''),
                   COALESCE(C.CREDIT_LIMIT, 0)
            FROM CUSTOMERS C
            LEFT JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
            WHERE C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@HAS_SEARCH = 0
                   OR CAST(C.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH
                   OR COALESCE(C.PHONE, '') CONTAINING @SEARCH
                   OR COALESCE(C.EMAIL, '') CONTAINING @SEARCH
                   OR COALESCE(C.ADDRESS, '') CONTAINING @SEARCH)
            ORDER BY C.NAME, C.CODE
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();

        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalCreditLimit = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var creditLimit = reader.GetDecimal(8);
            totalCreditLimit += creditLimit;
            rows.Add(new MasterCustomersReportRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                creditLimit));
        }

        return new MasterCustomersReportResult(rows, rows.Count, totalCreditLimit);
    }
}
