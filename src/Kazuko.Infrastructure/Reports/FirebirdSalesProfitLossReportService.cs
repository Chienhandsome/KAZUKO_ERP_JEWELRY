using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesProfitLossReportService : ISalesProfitLossReportService
{
    public async Task<SalesProfitLossReportResult> GetAsync(
        SalesProfitLossReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesProfitLossReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // Giá vốn lấy từ FINISHED_PRODUCTS.TOTAL_COST của sản phẩm đã bán.
        // Dòng hàng lẻ không gắn sản phẩm (FINISHED_PRODUCT_ID NULL) có giá vốn 0.
        await using var command = new FbCommand("""
            SELECT CAST(SO.ORDER_DATE AS DATE),
                   SO.CODE,
                   SOI.PRODUCT_NAME,
                   COALESCE(GT.NAME, COALESCE(SOI.GOLD_PURITY, '')),
                   COALESCE(FP.TOTAL_COST, 0),
                   SOI.LINE_TOTAL,
                   SOI.LINE_TOTAL - COALESCE(FP.TOTAL_COST, 0)
            FROM SALES_ORDERS SO
            JOIN SALES_ORDER_ITEMS SOI ON SOI.ORDER_ID = SO.ID
            LEFT JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
            LEFT JOIN GOLD_TYPES GT ON GT.ID = FP.GOLD_TYPE_ID
            WHERE SO.BRANCH_ID = @BRANCH_ID
              AND SO.ORDER_TYPE = 'RETAIL'
              AND SO.STATUS_CODE = 'COMPLETED'
              AND CAST(SO.ORDER_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            ORDER BY SO.ORDER_DATE, SO.CODE, SOI.LINE_NUMBER
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalCost = 0m;
        decimal totalSale = 0m;
        decimal totalProfit = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var cost = reader.GetDecimal(4);
            var sale = reader.GetDecimal(5);
            var profit = reader.GetDecimal(6);
            totalCost += cost;
            totalSale += sale;
            totalProfit += profit;
            rows.Add(new SalesProfitLossReportRow(
                reader.GetDateTime(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                cost,
                sale,
                profit));
        }

        return new SalesProfitLossReportResult(rows, rows.Count, totalCost, totalSale, totalProfit);
    }
}
