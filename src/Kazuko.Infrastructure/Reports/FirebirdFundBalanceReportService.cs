using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdFundBalanceReportService : IFundBalanceReportService
{
    public async Task<FundBalanceReportResult> GetAsync(
        FundBalanceReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");

        var fromStart = filter.FromDate.Date;
        var toExclusive = filter.ToDate.Date.AddDays(1);
        if (fromStart > toExclusive.AddTicks(-1))
            throw new InvalidOperationException("Từ ngày phải không lớn hơn đến ngày.");

        var rows = new List<FundBalanceReportRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT F.KIND,
                   F.FUND_ID,
                   F.FUND_NAME,
                   COALESCE(SUM(CASE WHEN V.VOUCHER_DATE < @FROM_START
                                    THEN CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE -V.AMOUNT_VND END
                                    ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN V.VOUCHER_DATE >= @FROM_START
                                      AND V.VOUCHER_DATE < @TO_EXCLUSIVE
                                      AND V.VOUCHER_TYPE = 'IN'
                                    THEN V.AMOUNT_VND ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN V.VOUCHER_DATE >= @FROM_START
                                      AND V.VOUCHER_DATE < @TO_EXCLUSIVE
                                      AND V.VOUCHER_TYPE = 'OUT'
                                    THEN V.AMOUNT_VND ELSE 0 END), 0)
            FROM (
                SELECT ID AS FUND_ID,
                       NAME AS FUND_NAME,
                       'CASH' AS KIND
                FROM CASH_FUNDS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND BRANCH_ID = @BRANCH_ID
                  AND IS_ACTIVE = TRUE
                UNION ALL
                SELECT ID,
                       BANK_NAME || ' - ' || ACCOUNT_NUMBER,
                       'BANK'
                FROM BANK_ACCOUNTS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND BRANCH_ID = @BRANCH_ID
                  AND IS_ACTIVE = TRUE
            ) F
            LEFT JOIN CASH_VOUCHERS V
                   ON V.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND V.BRANCH_ID = @BRANCH_ID
                  AND V.STATUS_CODE = 'POSTED'
                  AND ((F.KIND = 'CASH' AND V.PAYMENT_METHOD = 'CASH'
                        AND V.CASH_FUND_ID = F.FUND_ID)
                       OR (F.KIND = 'BANK' AND V.PAYMENT_METHOD = 'BANK_TRANSFER'
                           AND V.BANK_ACCOUNT_ID = F.FUND_ID))
            GROUP BY F.KIND, F.FUND_ID, F.FUND_NAME
            ORDER BY F.KIND, F.FUND_NAME
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_START", FbDbType.TimeStamp).Value = fromStart;
        command.Parameters.Add("@TO_EXCLUSIVE", FbDbType.TimeStamp).Value = toExclusive;

        decimal totalOpening = 0m;
        decimal totalIn = 0m;
        decimal totalOut = 0m;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var opening = reader.GetDecimal(3);
            var periodIn = reader.GetDecimal(4);
            var periodOut = reader.GetDecimal(5);
            var closing = opening + periodIn - periodOut;
            totalOpening += opening;
            totalIn += periodIn;
            totalOut += periodOut;
            rows.Add(new FundBalanceReportRow(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                opening,
                periodIn,
                periodOut,
                closing));
        }

        return new FundBalanceReportResult(rows, totalOpening, totalIn, totalOut, totalOpening + totalIn - totalOut);
    }
}
