using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdSalesPrintDataService : ISalesPrintDataService
{
    private readonly ISalesQueryRepository _queries;

    public FirebirdSalesPrintDataService(ISalesQueryRepository queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public async Task<SalesReceiptDto> GetReceiptAsync(
        long checkoutId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (checkoutId <= 0)
            throw new InvalidOperationException("Không tìm thấy chứng từ bán hàng.");

        var detail = await _queries.GetCheckoutAsync(checkoutId, session, cancellationToken);
        if (detail is null)
            throw new InvalidOperationException("Không tìm thấy chứng từ hoặc không có quyền xem ở chi nhánh hiện tại.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.NAME, B.NAME, B.ADDRESS, B.PHONE
            FROM BRANCHES B
            JOIN COMPANIES C ON C.ID = B.COMPANY_ID
            WHERE B.ID = @BRANCH_ID
              AND B.IS_ACTIVE = TRUE
            """, connection);
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tải được thông tin công ty/chi nhánh để in phiếu.");

        return new SalesReceiptDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            detail);
    }
}
