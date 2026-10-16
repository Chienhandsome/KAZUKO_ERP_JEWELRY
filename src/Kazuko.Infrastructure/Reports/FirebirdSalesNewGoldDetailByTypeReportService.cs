using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesNewGoldDetailByTypeReportService : ISalesNewGoldDetailByTypeReportService
{
    public async Task<SalesNewGoldDetailByTypeReportResult> GetAsync(
        SalesNewGoldDetailByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesNewGoldDetailByTypeReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(GT.NAME, COALESCE(SOI.GOLD_PURITY, '')),
                   CAST(SO.ORDER_DATE AS DATE),
                   SO.CODE,
                   COALESCE(FP.CODE, ''),
                   COALESCE(SOI.ESTIMATED_WEIGHT, 0),
                   SOI.LINE_TOTAL
            FROM SALES_ORDERS SO
            JOIN SALES_ORDER_ITEMS SOI ON SOI.ORDER_ID = SO.ID
            LEFT JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
            LEFT JOIN GOLD_TYPES GT ON GT.ID = FP.GOLD_TYPE_ID
            WHERE SO.BRANCH_ID = @BRANCH_ID
              AND SO.ORDER_TYPE = 'RETAIL'
              AND SO.STATUS_CODE = 'COMPLETED'
              AND CAST(SO.ORDER_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            ORDER BY 1, CAST(SO.ORDER_DATE AS DATE), SO.CODE, SOI.LINE_NUMBER
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalWeight = 0m;
        decimal totalAmount = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var weight = reader.GetDecimal(4);
            var amount = reader.GetDecimal(5);
            totalWeight += weight;
            totalAmount += amount;
            rows.Add(new SalesNewGoldDetailByTypeReportRow(
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetString(3),
                weight,
                amount));
        }

        return new SalesNewGoldDetailByTypeReportResult(rows, rows.Count, totalWeight, totalAmount);
    }
}
