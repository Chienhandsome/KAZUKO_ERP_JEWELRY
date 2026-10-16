using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

/// <summary>
/// Tổng hợp nhập hàng theo loại vàng: gom dòng phiếu nhập (STOCK_DOCUMENTS/STOCK_DOCUMENT_LINES)
/// đã ghi sổ (STATUS_CODE = 'POSTED') theo GOLD_TYPES.NAME (dự phòng MATERIALS.PURITY_CODE).
/// </summary>
public sealed class FirebirdWarehouseReceiptsByTypeReportService : IWarehouseReceiptsByTypeReportService
{
    public async Task<WarehouseReceiptsByTypeReportResult> GetAsync(
        WarehouseReceiptsByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<WarehouseReceiptsByTypeReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(G.NAME, COALESCE(M.PURITY_CODE, 'Khác')),
                   COUNT(DISTINCT D.ID),
                   COUNT(*),
                   SUM(COALESCE(L.QUANTITY, 0)),
                   SUM(COALESCE(L.AMOUNT_VND, 0))
            FROM STOCK_DOCUMENTS D
            JOIN STOCK_DOCUMENT_LINES L ON L.DOCUMENT_ID = D.ID
            JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
            LEFT JOIN GOLD_TYPES G ON G.ID = M.GOLD_TYPE_ID AND G.COMPANY_ID = D.COMPANY_ID
            WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BRANCH_ID
              AND D.DOCUMENT_TYPE = 'RECEIPT'
              AND D.STATUS_CODE = 'POSTED'
              AND CAST(D.DOCUMENT_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            GROUP BY 1
            ORDER BY 1
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        var documentCount = 0;
        var lineCount = 0;
        decimal totalQuantity = 0m;
        decimal totalAmount = 0m;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var docs = Convert.ToInt32(reader.GetValue(1));
            var lines = Convert.ToInt32(reader.GetValue(2));
            var quantity = reader.GetDecimal(3);
            var amount = reader.GetDecimal(4);
            documentCount += docs;
            lineCount += lines;
            totalQuantity += quantity;
            totalAmount += amount;
            rows.Add(new WarehouseReceiptsByTypeReportRow(
                reader.GetString(0),
                docs,
                lines,
                quantity,
                amount));
        }

        return new WarehouseReceiptsByTypeReportResult(
            rows, rows.Count, documentCount, lineCount, totalQuantity, totalAmount);
    }
}
