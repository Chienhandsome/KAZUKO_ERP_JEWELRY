using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

/// <summary>
/// Chi tiết nhập hàng chưa xét duyệt: dòng phiếu nhập (STOCK_DOCUMENTS/STOCK_DOCUMENT_LINES)
/// còn STATUS_CODE = 'DRAFT' (chưa ghi sổ — điều kiện gần nhất với "chưa xét duyệt" vì
/// schema hiện tại không có cột trạng thái duyệt riêng).
/// </summary>
public sealed class FirebirdWarehousePendingReceiptsReportService : IWarehousePendingReceiptsReportService
{
    public async Task<WarehousePendingReceiptsReportResult> GetAsync(
        WarehousePendingReceiptsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<WarehousePendingReceiptsReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT L.ID,
                   D.DOCUMENT_DATE,
                   D.CODE,
                   M.CODE,
                   M.NAME,
                   COALESCE(G.NAME, COALESCE(M.PURITY_CODE, '')),
                   COALESCE(L.STANDARD_UNIT_CODE, M.UNIT_CODE),
                   COALESCE(L.QUANTITY, 0),
                   COALESCE(L.UNIT_PRICE, 0),
                   COALESCE(L.AMOUNT_VND, 0),
                   COALESCE(S.NAME, COALESCE(D.COUNTERPARTY_NAME, ''))
            FROM STOCK_DOCUMENTS D
            JOIN STOCK_DOCUMENT_LINES L ON L.DOCUMENT_ID = D.ID
            JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
            LEFT JOIN GOLD_TYPES G ON G.ID = M.GOLD_TYPE_ID AND G.COMPANY_ID = D.COMPANY_ID
            LEFT JOIN SUPPLIERS S ON S.ID = D.SUPPLIER_ID AND S.COMPANY_ID = D.COMPANY_ID
            WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BRANCH_ID
              AND D.DOCUMENT_TYPE = 'RECEIPT'
              AND D.STATUS_CODE = 'DRAFT'
              AND CAST(D.DOCUMENT_DATE AS DATE) BETWEEN @FROM_DATE AND @TO_DATE
            ORDER BY D.DOCUMENT_DATE, D.CODE, L.LINE_NUMBER
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;

        var documentCodes = new HashSet<string>(StringComparer.Ordinal);
        decimal totalQuantity = 0m;
        decimal totalAmount = 0m;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var quantity = reader.GetDecimal(7);
            var amount = reader.GetDecimal(9);
            totalQuantity += quantity;
            totalAmount += amount;
            var code = reader.GetString(2);
            documentCodes.Add(code);
            rows.Add(new WarehousePendingReceiptsReportRow(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                code,
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                quantity,
                reader.GetDecimal(8),
                amount,
                reader.GetString(10)));
        }

        return new WarehousePendingReceiptsReportResult(
            rows, documentCodes.Count, rows.Count, totalQuantity, totalAmount);
    }
}
