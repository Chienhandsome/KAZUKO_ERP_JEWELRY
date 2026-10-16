using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesNewGoldSummaryByTypeReportService : ISalesNewGoldSummaryByTypeReportService
{
    public async Task<SalesNewGoldSummaryByTypeReportResult> GetAsync(
        SalesNewGoldSummaryByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesNewGoldSummaryByTypeReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(GT.NAME, COALESCE(SOI.GOLD_PURITY, '')),
                   COUNT(*),
                   SUM(COALESCE(SOI.ESTIMATED_WEIGHT, 0)),
                   SUM(SOI.LINE_TOTAL)
            FROM SALES_ORDERS SO
            JOIN SALES_ORDER_ITEMS SOI ON SOI.ORDER_ID = SO.ID
            LEFT JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
            LEFT JOIN GOLD_TYPES GT ON GT.ID = FP.GOLD_TYPE_ID
            WHERE SO.BRANCH_ID = @BRANCH_ID
              AND SO.ORDER_TYPE = 'RETAIL'
              AND SO.STATUS_CODE = 'COMPLETED'
              AND CAST(SO.ORDER_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            GROUP BY 1
            ORDER BY 1
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var totalItems = 0;
        decimal totalWeight = 0m;
        decimal totalAmount = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = Convert.ToInt32(reader.GetValue(1));
            var weight = reader.GetDecimal(2);
            var amount = reader.GetDecimal(3);
            totalItems += count;
            totalWeight += weight;
            totalAmount += amount;
            rows.Add(new SalesNewGoldSummaryByTypeReportRow(
                reader.GetString(0),
                count,
                weight,
                amount));
        }

        return new SalesNewGoldSummaryByTypeReportResult(rows, totalItems, totalWeight, totalAmount);
    }
}
