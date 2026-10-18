using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdSalesCheckoutApplicationService : ISalesCheckoutApplicationService
{
    public async Task<SalesCheckoutResult> PostAsync(
        SalesCheckoutDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (draft.SaleLines is null || draft.OldGoldLines is null || draft.Allocations is null)
            throw new InvalidOperationException("Dữ liệu checkout không đầy đủ.");
        if (draft.SaleLines.Count == 0 && draft.OldGoldLines.Count == 0)
            throw new InvalidOperationException("Checkout phải có ít nhất một dòng bán hoặc vàng cũ.");

        var requestHash = SalesCheckoutPostingSupport.ComputeRequestHash(draft);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var replay = await TryLoadExistingAsync(
                connection, transaction, session.BranchId, draft.ClientRequestId,
                requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return replay;
            }

            var identity = await ReloadIdentityAndPermissionsAsync(
                connection, transaction, session, cancellationToken);
            var prepared = await SalesCheckoutPostingSupport.PrepareCheckoutAsync(
                connection, transaction, identity.CompanyId, draft,
                identity.Permissions, session, cancellationToken);
            var sale = await SalesCheckoutPostingSupport.InsertCheckoutAsync(
                connection, transaction, identity.CompanyId, draft, prepared, session, cancellationToken);

            long? issueId = null;
            if (prepared.SaleLines.Count > 0)
            {
                var orderId = await SalesCheckoutPostingSupport.PostSaleOrderAsync(
                    connection, transaction, identity.CompanyId, sale.CheckoutId, draft,
                    prepared, session, cancellationToken);
                sale = sale with { SalesOrderId = orderId };
                issueId = await FinishedGoodsPostingSupport.PostRetailIssueAsync(
                    connection, transaction, identity.CompanyId, session, orderId,
                    draft.SourceLocationId, draft.SaleLines.Select(x => x.FinishedProductId).ToArray(),
                    cancellationToken);
            }

            OldGoldPostResult? oldGold = null;
            if (prepared.OldGoldLines.Count > 0)
            {
                oldGold = await SalesCheckoutPostingSupport.PostOldGoldReceiptAsync(
                    connection, transaction, identity.CompanyId, sale.CheckoutId, draft,
                    prepared, session, cancellationToken);
            }

            if (sale.SalesOrderId is not null && oldGold is not null && prepared.Totals.OffsetAmountVnd > 0)
            {
                await SalesCheckoutPostingSupport.PostOffsetAsync(
                    connection, transaction, sale.CheckoutId, sale.SalesOrderId.Value,
                    oldGold.StockDocumentId, prepared.Totals.OffsetAmountVnd, cancellationToken);
            }

            FinancialSettlementResultProxy? settlement = null;
            long? debtId = null;
            if (prepared.Totals.NetAmountVnd > 0)
            {
                var posted = await SalesCheckoutPostingSupport.PostReceiptAsync(
                    connection, transaction, identity.CompanyId, sale.CheckoutId, sale,
                    draft.Allocations, session, cancellationToken);
                settlement = posted is null ? null : new FinancialSettlementResultProxy(posted.SettlementId);
                debtId = await SalesCheckoutPostingSupport.CreateReceivableAsync(
                    connection, transaction, identity.CompanyId, sale.CheckoutId, sale,
                    draft.ReceivableAmountVnd, session, cancellationToken);
            }
            else if (prepared.Totals.NetAmountVnd < 0)
            {
                if (oldGold is null)
                    throw new InvalidOperationException("Thiếu phiếu nhập vàng cũ cho khoản chi.");
                await SalesCheckoutPostingSupport.PostPayoutAsync(
                    connection, transaction, identity.CompanyId, sale.CheckoutId, oldGold,
                    draft.Allocations, session, cancellationToken);
            }

            await SalesCheckoutPostingSupport.WriteAuditAsync(
                connection, transaction, "SALES_CHECKOUT_POSTED", session, sale.CheckoutId,
                $"{{\"salesOrderId\":{JsonNumber(sale.SalesOrderId)},\"issueDocumentId\":{JsonNumber(issueId)}," +
                $"\"oldGoldStockDocumentId\":{JsonNumber(oldGold?.StockDocumentId)}," +
                $"\"settlementId\":{JsonNumber(settlement?.SettlementId)},\"receivableDebtId\":{JsonNumber(debtId)}," +
                $"\"saleGrossVnd\":{sale.Totals.SaleGrossVnd.ToString(CultureInfo.InvariantCulture)}," +
                $"\"oldGoldTotalVnd\":{sale.Totals.OldGoldTotalVnd.ToString(CultureInfo.InvariantCulture)}," +
                $"\"netAmountVnd\":{sale.Totals.NetAmountVnd.ToString(CultureInfo.InvariantCulture)}}}",
                cancellationToken);

            if (prepared.SaleLines.Any(x => x.Amounts.IsPriceOverride) ||
                prepared.OldGoldLines.Any(x => x.IsPriceOverride))
                await SalesCheckoutPostingSupport.WriteAuditAsync(
                    connection, transaction, "SALES_PRICE_OVERRIDE", session, sale.CheckoutId,
                    BuildOverrideAudit(prepared), cancellationToken);
            if (draft.ReceivableAmountVnd > 0)
                await SalesCheckoutPostingSupport.WriteAuditAsync(
                    connection, transaction, "SALES_DEBT_APPROVED", session, sale.CheckoutId,
                    $"{{\"amountVnd\":{draft.ReceivableAmountVnd.ToString(CultureInfo.InvariantCulture)}," +
                    $"\"approvedBy\":{session.UserId}}}", cancellationToken);
            if (draft.SalespersonId != session.UserId)
                await SalesCheckoutPostingSupport.WriteAuditAsync(
                    connection, transaction, "SALES_SALESPERSON_CHANGED", session, sale.CheckoutId,
                    $"{{\"salespersonId\":{draft.SalespersonId},\"executedBy\":{session.UserId}}}",
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new SalesCheckoutResult(
                sale.CheckoutId, sale.CheckoutCode, prepared.CheckoutType, sale.Totals,
                sale.SalesOrderId, oldGold?.StockDocumentId, AlreadyPosted: false);
        }
        catch (FbException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var raced = await LoadExistingAfterRaceAsync(
                session.BranchId, draft.ClientRequestId, requestHash, cancellationToken);
            if (raced is not null)
                return raced;
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<IdentityContext> ReloadIdentityAndPermissionsAsync(
        FbConnection connection,
        FbTransaction transaction,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT U.COMPANY_ID, R.CODE,
                   MAX(CASE WHEN P.CODE = @POST AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @OVERRIDE AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @DEBT AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @SALESPERSON AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @HISTORY AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @PRINT AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END)
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            JOIN BRANCHES B ON B.ID = @BID AND B.COMPANY_ID = U.COMPANY_ID AND B.IS_ACTIVE = TRUE
            LEFT JOIN ROLE_PERMISSIONS RP ON RP.ROLE_ID = R.ID
            LEFT JOIN PERMISSIONS P ON P.ID = RP.PERMISSION_ID
            WHERE U.ID = @UID AND U.IS_ACTIVE = TRUE
              AND (U.DEFAULT_BRANCH_ID = @BID OR EXISTS (
                  SELECT 1 FROM USER_SCOPES S WHERE S.USER_ID = U.ID
                    AND S.SCOPE_TYPE = 'BRANCH' AND S.SCOPE_ID = @BID AND S.CAN_ACCESS = TRUE))
            GROUP BY U.COMPANY_ID, R.CODE
            """, connection, transaction);
        command.Parameters.Add("@UID", FbDbType.BigInt).Value = session.UserId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@POST", FbDbType.VarChar, 80).Value = SalesPermissions.CheckoutPost;
        command.Parameters.Add("@OVERRIDE", FbDbType.VarChar, 80).Value = SalesPermissions.PriceOverride;
        command.Parameters.Add("@DEBT", FbDbType.VarChar, 80).Value = SalesPermissions.DebtApprove;
        command.Parameters.Add("@SALESPERSON", FbDbType.VarChar, 80).Value = SalesPermissions.SalespersonChange;
        command.Parameters.Add("@HISTORY", FbDbType.VarChar, 80).Value = SalesPermissions.HistoryRead;
        command.Parameters.Add("@PRINT", FbDbType.VarChar, 80).Value = SalesPermissions.ReceiptPrint;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Phiên người dùng không còn hợp lệ cho chi nhánh hiện tại.");
        var isAdmin = string.Equals(reader.GetString(1).Trim(), "ADMIN", StringComparison.OrdinalIgnoreCase);
        var permissions = isAdmin
            ? new SalesPermissionSnapshot(true, true, true, true, true, true)
            : new SalesPermissionSnapshot(
                Allowed(reader, 2), Allowed(reader, 3), Allowed(reader, 4),
                Allowed(reader, 5), Allowed(reader, 6), Allowed(reader, 7));
        var companyId = reader.GetInt64(0);
        await reader.CloseAsync();
        permissions = await GroupSalesPermissions.RestrictAsync(connection, transaction, session.UserId, permissions, cancellationToken);
        return new IdentityContext(companyId, permissions);
    }

    private static async Task<SalesCheckoutResult?> TryLoadExistingAsync(
        FbConnection connection,
        FbTransaction transaction,
        long branchId,
        Guid requestId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT C.ID, C.CODE, C.CHECKOUT_TYPE, C.SALE_GROSS_VND,
                   C.DISCOUNT_AMOUNT_VND, C.OLD_GOLD_TOTAL_VND, C.NET_AMOUNT_VND,
                   C.SALES_ORDER_ID, C.OLD_GOLD_STOCK_DOCUMENT_ID, C.REQUEST_HASH,
                   COALESCE(D.ORIGINAL_AMOUNT_VND, 0)
            FROM SALES_CHECKOUTS C
            LEFT JOIN RECEIVABLE_DEBTS D ON D.ID = C.RECEIVABLE_DEBT_ID
            WHERE C.BRANCH_ID = @BID AND C.CLIENT_REQUEST_ID = @RID
            """, connection, transaction);
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@RID", FbDbType.Char, 36).Value = requestId.ToString("D");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        if (!string.Equals(reader.GetString(9).Trim(), requestHash, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Mã yêu cầu checkout đã được dùng với nội dung khác.");
        var gross = reader.GetDecimal(3);
        var discount = reader.GetDecimal(4);
        var oldGold = reader.GetDecimal(5);
        var net = reader.GetDecimal(6);
        var debt = reader.GetDecimal(10);
        var saleNet = gross - discount;
        var totals = new SalesCheckoutTotals(
            gross, discount, saleNet, oldGold, Math.Min(saleNet, oldGold), net,
            net > 0 ? net - debt : 0, net < 0 ? -net : 0, debt);
        return new SalesCheckoutResult(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), totals,
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8), AlreadyPosted: true);
    }

    private static async Task<SalesCheckoutResult?> LoadExistingAfterRaceAsync(
        long branchId, Guid requestId, string requestHash, CancellationToken cancellationToken)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await TryLoadExistingAsync(
                connection, transaction, branchId, requestId, requestHash, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string BuildOverrideAudit(PreparedCheckout prepared)
    {
        var saleParts = prepared.SaleLines.Where(x => x.Amounts.IsPriceOverride).Select(x =>
            $"{{\"finishedProductId\":{x.Draft.FinishedProductId}," +
            $"\"sourceGold\":{x.Pricing.SourceGoldPricePerChi.ToString(CultureInfo.InvariantCulture)}," +
            $"\"appliedGold\":{x.Draft.AppliedGoldPricePerChi.ToString(CultureInfo.InvariantCulture)}," +
            $"\"reason\":\"{EscapeJson(x.Draft.OverrideReason ?? string.Empty)}\"}}");
        var oldGoldParts = prepared.OldGoldLines.Where(x => x.IsPriceOverride).Select(x =>
            $"{{\"materialId\":{x.Draft.MaterialId}," +
            $"\"sourceBuy\":{x.SourceBuyPricePerChi.ToString(CultureInfo.InvariantCulture)}," +
            $"\"appliedBuy\":{x.Draft.AppliedBuyPricePerChi.ToString(CultureInfo.InvariantCulture)}," +
            $"\"reason\":\"{EscapeJson(x.Draft.OverrideReason ?? string.Empty)}\"}}");
        return "[" + string.Join(',', saleParts.Concat(oldGoldParts)) + "]";
    }

    private static bool Allowed(FbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToInt32(reader.GetValue(ordinal)) == 1;

    private static bool IsUniqueViolation(FbException exception) =>
        exception.ErrorCode == 335544665 ||
        (exception.Message?.Contains("violation of PRIMARY or UNIQUE KEY", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string JsonNumber(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record IdentityContext(long CompanyId, SalesPermissionSnapshot Permissions);
    private sealed record FinancialSettlementResultProxy(long SettlementId);
}
