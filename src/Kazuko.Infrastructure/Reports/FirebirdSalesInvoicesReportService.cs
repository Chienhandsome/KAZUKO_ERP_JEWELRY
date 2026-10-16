using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdSalesInvoicesReportService : ISalesInvoicesReportService
{
    public async Task<SalesInvoicesReportResult> GetAsync(
        SalesInvoicesReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var rows = new List<SalesInvoicesReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT SC.ID,
                   SC.CODE,
                   SC.DOCUMENT_DATE,
                   COALESCE(C.NAME, ''),
                   SC.NET_AMOUNT_VND,
                   COALESCE(FS.TOTAL_AMOUNT_VND, 0),
                   COALESCE(RD.REMAINING_AMOUNT_VND, 0)
            FROM SALES_CHECKOUTS SC
            LEFT JOIN CUSTOMERS C ON C.COMPANY_ID = SC.COMPANY_ID AND C.ID = SC.CUSTOMER_ID
            LEFT JOIN FINANCIAL_SETTLEMENTS FS ON FS.ID = SC.FINANCIAL_SETTLEMENT_ID
            LEFT JOIN RECEIVABLE_DEBTS RD ON RD.ID = SC.RECEIVABLE_DEBT_ID
            WHERE SC.BRANCH_ID = @BRANCH_ID
              AND SC.CHECKOUT_TYPE IN ('SALE', 'TRADE_IN')
              AND SC.DOCUMENT_DATE BETWEEN @FROM_DATE AND @TO_DATE
              AND (@HAS_CUSTOMER = 0
                   OR UPPER(COALESCE(C.NAME, '')) CONTAINING @CUSTOMER
                   OR UPPER(COALESCE(C.CODE, '')) CONTAINING @CUSTOMER)
            ORDER BY SC.DOCUMENT_DATE, SC.CODE
            """, connection);

        var customer = (filter.CustomerText ?? string.Empty).Trim().ToUpperInvariant();

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate.Date;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate.Date;
        command.Parameters.Add("@HAS_CUSTOMER", FbDbType.Integer).Value = customer.Length > 0 ? 1 : 0;
        command.Parameters.Add("@CUSTOMER", FbDbType.VarChar, 255).Value = customer;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal totalAmount = 0m;
        decimal totalPaid = 0m;
        decimal totalRemaining = 0m;
        while (await reader.ReadAsync(cancellationToken))
        {
            var amount = reader.GetDecimal(4);
            var paid = reader.GetDecimal(5);
            var remaining = reader.GetDecimal(6);
            totalAmount += amount;
            totalPaid += paid;
            totalRemaining += remaining;
            rows.Add(new SalesInvoicesReportRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                amount,
                paid,
                remaining));
        }

        return new SalesInvoicesReportResult(rows, rows.Count, totalAmount, totalPaid, totalRemaining);
    }
}
