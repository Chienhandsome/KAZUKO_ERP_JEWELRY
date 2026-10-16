using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesOldGoldPurchaseDetailByTypeReportService : ISalesOldGoldPurchaseDetailByTypeReportService
{
    public async Task<SalesOldGoldPurchaseDetailByTypeReportResult> GetAsync(
        SalesOldGoldPurchaseDetailByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesOldGoldPurchaseDetailByTypeReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(GT.NAME, COALESCE(M.PURITY_CODE, '')),
                   SC.DOCUMENT_DATE,
                   SC.CODE,
                   SOGL.GOLD_WEIGHT_CHI,
                   SOGL.APPLIED_BUY_PRICE_PER_CHI,
                   SOGL.AMOUNT_VND
            FROM SALES_OLD_GOLD_LINES SOGL
            JOIN SALES_CHECKOUTS SC ON SC.ID = SOGL.CHECKOUT_ID
            JOIN MATERIALS M ON M.ID = SOGL.MATERIAL_ID
            LEFT JOIN GOLD_TYPES GT ON GT.ID = M.GOLD_TYPE_ID
            WHERE SC.BRANCH_ID = @BRANCH_ID
              AND SC.CHECKOUT_TYPE IN ('TRADE_IN', 'OLD_GOLD_PURCHASE')
              AND SC.DOCUMENT_DATE BETWEEN @FROM_DATE AND @TO_DATE
            ORDER BY 1, SC.DOCUMENT_DATE, SC.CODE, SOGL.LINE_NUMBER
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalWeight = 0m;
        decimal totalAmount = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var weight = reader.GetDecimal(3);
            var amount = reader.GetDecimal(5);
            totalWeight += weight;
            totalAmount += amount;
            rows.Add(new SalesOldGoldPurchaseDetailByTypeReportRow(
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                weight,
                reader.GetDecimal(4),
                amount));
        }

        return new SalesOldGoldPurchaseDetailByTypeReportResult(rows, rows.Count, totalWeight, totalAmount);
    }
}
