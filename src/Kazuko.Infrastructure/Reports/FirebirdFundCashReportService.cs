using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdFundCashReportService : IFundCashReportService
{
    public async Task<FundCashReportResult> GetAsync(
        FundCashReportFilter filter,
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

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        decimal openingBalance;
        await using (var openingCmd = new FbCommand("""
            SELECT COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE -V.AMOUNT_VND END), 0)
            FROM CASH_VOUCHERS V
            WHERE V.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND V.BRANCH_ID = @BRANCH_ID
              AND V.PAYMENT_METHOD = 'CASH'
              AND V.STATUS_CODE = 'POSTED'
              AND V.VOUCHER_DATE < @FROM_START
            """, connection))
        {
            openingCmd.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
            openingCmd.Parameters.Add("@FROM_START", FbDbType.TimeStamp).Value = fromStart;
            var openingObj = await openingCmd.ExecuteScalarAsync(cancellationToken);
            openingBalance = openingObj is null or DBNull ? 0m : Convert.ToDecimal(openingObj);
        }

        var rows = new List<FundCashReportRow>();
        decimal totalIn = 0m;
        decimal totalOut = 0m;
        decimal running = openingBalance;
        await using (var dailyCmd = new FbCommand("""
            SELECT CAST(V.VOUCHER_DATE AS DATE) AS VDATE,
                   SUM(CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE 0 END),
                   SUM(CASE WHEN V.VOUCHER_TYPE = 'OUT' THEN V.AMOUNT_VND ELSE 0 END)
            FROM CASH_VOUCHERS V
            WHERE V.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND V.BRANCH_ID = @BRANCH_ID
              AND V.PAYMENT_METHOD = 'CASH'
              AND V.STATUS_CODE = 'POSTED'
              AND V.VOUCHER_DATE >= @FROM_START
              AND V.VOUCHER_DATE < @TO_EXCLUSIVE
            GROUP BY CAST(V.VOUCHER_DATE AS DATE)
            ORDER BY CAST(V.VOUCHER_DATE AS DATE)
            """, connection))
        {
            dailyCmd.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
            dailyCmd.Parameters.Add("@FROM_START", FbDbType.TimeStamp).Value = fromStart;
            dailyCmd.Parameters.Add("@TO_EXCLUSIVE", FbDbType.TimeStamp).Value = toExclusive;

            await using var reader = await dailyCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dayIn = reader.GetDecimal(1);
                var dayOut = reader.GetDecimal(2);
                running += dayIn - dayOut;
                totalIn += dayIn;
                totalOut += dayOut;
                rows.Add(new FundCashReportRow(reader.GetDateTime(0), dayIn, dayOut, running));
            }
        }

        return new FundCashReportResult(rows, openingBalance, totalIn, totalOut, running);
    }
}
