using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdMasterSuppliersReportService : IMasterSuppliersReportService
{
    public async Task<MasterSuppliersReportResult> GetAsync(
        MasterSuppliersReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);

        var rows = new List<MasterSuppliersReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT S.ID,
                   S.CODE,
                   S.NAME,
                   COALESCE(S.PHONE, ''),
                   COALESCE(S.EMAIL, ''),
                   COALESCE(S.ADDRESS, ''),
                   COALESCE(S.TAX_CODE, ''),
                   COALESCE(S.CONTACT_NAME, '')
            FROM SUPPLIERS S
            WHERE S.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@HAS_SEARCH = 0
                   OR CAST(S.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR S.NAME CONTAINING @SEARCH
                   OR COALESCE(S.PHONE, '') CONTAINING @SEARCH
                   OR COALESCE(S.EMAIL, '') CONTAINING @SEARCH
                   OR COALESCE(S.CONTACT_NAME, '') CONTAINING @SEARCH)
            ORDER BY S.NAME, S.CODE
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();

        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MasterSuppliersReportRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return new MasterSuppliersReportResult(rows, rows.Count);
    }
}
