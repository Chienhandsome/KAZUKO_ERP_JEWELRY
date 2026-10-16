using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesTopItemsReportService : ISalesTopItemsReportService
{
    public async Task<SalesTopItemsReportResult> GetAsync(
        SalesTopItemsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesTopItemsReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT FIRST 20
                   COALESCE(FP.CODE, ''),
                   SOI.PRODUCT_NAME,
                   COALESCE(GT.NAME, COALESCE(SOI.GOLD_PURITY, '')),
                   CAST(SUM(COALESCE(SOI.QUANTITY, 1)) AS INTEGER),
                   SUM(SOI.LINE_TOTAL)
            FROM SALES_ORDERS SO
            JOIN SALES_ORDER_ITEMS SOI ON SOI.ORDER_ID = SO.ID
            LEFT JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
            LEFT JOIN GOLD_TYPES GT ON GT.ID = FP.GOLD_TYPE_ID
            WHERE SO.BRANCH_ID = @BRANCH_ID
              AND SO.ORDER_TYPE = 'RETAIL'
              AND SO.STATUS_CODE = 'COMPLETED'
              AND CAST(SO.ORDER_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            GROUP BY COALESCE(FP.CODE, ''), SOI.PRODUCT_NAME, COALESCE(GT.NAME, COALESCE(SOI.GOLD_PURITY, ''))
            ORDER BY 4 DESC, 5 DESC
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var totalQuantity = 0;
        decimal totalRevenue = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var quantity = reader.GetInt32(3);
            var revenue = reader.GetDecimal(4);
            totalQuantity += quantity;
            totalRevenue += revenue;
            rows.Add(new SalesTopItemsReportRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                quantity,
                revenue));
        }

        return new SalesTopItemsReportResult(rows, totalQuantity, totalRevenue);
    }
}
