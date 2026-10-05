using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdJewelryOnHandReportService : IJewelryOnHandReportService
{
    public async Task<JewelryOnHandReportResult> GetAsync(
        JewelryOnHandReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<JewelryOnHandReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT F.ID,
                   F.CODE,
                   F.PRODUCT_NAME,
                   COALESCE(F.GOLD_PURITY, ''),
                   COALESCE(F.GOLD_WEIGHT, 0),
                   COALESCE(F.TOTAL_WEIGHT, 0),
                   CASE WHEN F.RETAIL_PRICE > 0 THEN F.RETAIL_PRICE ELSE F.TOTAL_COST END,
                   F.LOCATION_ID,
                   COALESCE(L.NAME, ''),
                   COALESCE(L.INVENTORY_SCOPE, ''),
                   F.STATUS_CODE
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
              AND (@HAS_SEARCH = 0
                   OR CAST(F.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR F.PRODUCT_NAME CONTAINING @SEARCH)
            ORDER BY L.INVENTORY_SCOPE, F.PRODUCT_NAME, F.CODE
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
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
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalGold = 0m;
        decimal totalValue = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var goldWeight = reader.GetDecimal(4);
            var stockValue = reader.GetDecimal(6);
            totalGold += goldWeight;
            totalValue += stockValue;
            rows.Add(new JewelryOnHandReportRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                goldWeight,
                reader.GetDecimal(5),
                stockValue,
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return new JewelryOnHandReportResult(rows, rows.Count, totalGold, totalValue);
    }
}
