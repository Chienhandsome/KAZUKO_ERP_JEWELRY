using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesInvoicesByDayReportService : ISalesInvoicesByDayReportService
{
    public async Task<SalesInvoicesByDayReportResult> GetAsync(
        SalesInvoicesByDayReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesInvoicesByDayReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // Doanh thu = NET_AMOUNT_VND của checkout. Giá vốn = tổng TOTAL_COST các dòng bán.
        await using var command = new FbCommand("""
            SELECT SC.DOCUMENT_DATE,
                   COUNT(*),
                   SUM(SC.NET_AMOUNT_VND),
                   SUM(COALESCE(IT.COST, 0)),
                   SUM(SC.NET_AMOUNT_VND) - SUM(COALESCE(IT.COST, 0))
            FROM SALES_CHECKOUTS SC
            LEFT JOIN (
                SELECT SOI.ORDER_ID, SUM(COALESCE(FP.TOTAL_COST, 0)) AS COST
                FROM SALES_ORDER_ITEMS SOI
                LEFT JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
                GROUP BY SOI.ORDER_ID
            ) IT ON IT.ORDER_ID = SC.SALES_ORDER_ID
            WHERE SC.BRANCH_ID = @BRANCH_ID
              AND SC.CHECKOUT_TYPE IN ('SALE', 'TRADE_IN')
              AND SC.DOCUMENT_DATE BETWEEN @FROM_DATE AND @TO_DATE
            GROUP BY SC.DOCUMENT_DATE
            ORDER BY SC.DOCUMENT_DATE
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var totalInvoices = 0;
        decimal totalRevenue = 0m;
        decimal totalCost = 0m;
        decimal totalProfit = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = Convert.ToInt32(reader.GetValue(1));
            var revenue = reader.GetDecimal(2);
            var cost = reader.GetDecimal(3);
            var profit = reader.GetDecimal(4);
            totalInvoices += count;
            totalRevenue += revenue;
            totalCost += cost;
            totalProfit += profit;
            rows.Add(new SalesInvoicesByDayReportRow(
                reader.GetDateTime(0),
                count,
                revenue,
                cost,
                profit));
        }

        return new SalesInvoicesByDayReportResult(rows, totalInvoices, totalRevenue, totalCost, totalProfit);
    }
}
