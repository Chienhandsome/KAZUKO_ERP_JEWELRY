using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdJewelryOnHandSummaryReportService : IJewelryOnHandSummaryReportService
{
    public async Task<JewelryOnHandSummaryResult> GetAsync(
        JewelryOnHandSummaryFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<JewelryOnHandSummaryRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(L.INVENTORY_SCOPE, ''),
                   COALESCE(F.GOLD_PURITY, ''),
                   COUNT(*),
                   SUM(COALESCE(F.GOLD_WEIGHT, 0)),
                   SUM(COALESCE(F.TOTAL_WEIGHT, 0)),
                   SUM(CASE WHEN F.RETAIL_PRICE > 0 THEN F.RETAIL_PRICE ELSE F.TOTAL_COST END)
            FROM FINISHED_PRODUCTS F
            LEFT JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
            WHERE F.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND F.BRANCH_ID = @BRANCH_ID
              AND (
                    (F.STATUS_CODE IN ('IN_STOCK', 'RESERVED') AND L.INVENTORY_SCOPE = 'FINISHED_GOODS')
                 OR (F.STATUS_CODE = 'DISPLAYED' AND L.INVENTORY_SCOPE = 'SALES_FLOOR')
              )
              AND (@HAS_SCOPE = 0 OR L.INVENTORY_SCOPE = @SCOPE)
              AND (@HAS_STATUS = 0 OR F.STATUS_CODE = @STATUS)
              AND (@HAS_PURITY = 0 OR UPPER(COALESCE(F.GOLD_PURITY, '')) = @PURITY)
            GROUP BY L.INVENTORY_SCOPE, F.GOLD_PURITY
            ORDER BY L.INVENTORY_SCOPE, F.GOLD_PURITY
            """, connection);

        var purity = (filter.GoldPurity ?? string.Empty).Trim().ToUpperInvariant();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();
        var scope = (filter.InventoryScope ?? string.Empty).Trim().ToUpperInvariant();

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_SCOPE", FbDbType.Integer).Value = scope.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SCOPE", FbDbType.VarChar, 40).Value = scope;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = status;
        command.Parameters.Add("@HAS_PURITY", FbDbType.Integer).Value = purity.Length > 0 ? 1 : 0;
        command.Parameters.Add("@PURITY", FbDbType.VarChar, 40).Value = purity;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var itemCount = 0;
        decimal totalGold = 0m;
        decimal totalValue = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = Convert.ToInt32(reader.GetValue(2));
            var goldWeight = reader.GetDecimal(3);
            var stockValue = reader.GetDecimal(5);
            itemCount += count;
            totalGold += goldWeight;
            totalValue += stockValue;
            rows.Add(new JewelryOnHandSummaryRow(
                reader.GetString(0),
                reader.GetString(1),
                count,
                goldWeight,
                reader.GetDecimal(4),
                stockValue));
        }

        return new JewelryOnHandSummaryResult(rows, rows.Count, itemCount, totalGold, totalValue);
    }
}
