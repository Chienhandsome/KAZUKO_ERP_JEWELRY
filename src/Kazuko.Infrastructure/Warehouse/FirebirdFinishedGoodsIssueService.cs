using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdFinishedGoodsIssueService : IFinishedGoodsIssueService
{
    public async Task<long> CreateAndPostAsync(
        FinishedGoodsIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        var errors = FinishedGoodsIssueValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, transaction, cancellationToken);
            await EnsureOrderCanIssueAsync(
                connection, transaction, companyId, session.BranchId, draft.SalesOrderId, cancellationToken);
            var documentId = await FinishedGoodsPostingSupport.PostExistingOrderIssueAsync(
                connection, transaction, companyId, session, draft, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task ReverseAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        new FirebirdDocumentReversalService().ReverseStockDocumentAsync(
            documentId, reason, session, cancellationToken);

    private static async Task EnsureOrderCanIssueAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long branchId, long salesOrderId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT STATUS_CODE, FINISHED_GOODS_ISSUE_DOCUMENT_ID
            FROM SALES_ORDERS
            WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy đơn đặt chế tác.");
        var status = reader.GetString(0);
        long? existingIssue = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        if (!FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus(status))
            throw new InvalidOperationException("Đơn chưa xác nhận — không được xuất thành phẩm.");
        if (existingIssue is > 0)
            throw new InvalidOperationException("Đơn đã có phiếu xuất thành phẩm — không xuất lần hai.");
    }
}
