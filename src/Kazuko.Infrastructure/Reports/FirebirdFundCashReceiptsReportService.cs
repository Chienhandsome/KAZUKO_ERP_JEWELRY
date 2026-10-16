using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdFundCashReceiptsReportService : IFundCashReceiptsReportService
{
    public async Task<FundCashReceiptsReportResult> GetAsync(
        FundCashReceiptsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        var rawRows = await FundCashVoucherQueryHelper.QueryAsync(
            "IN", filter.FromDate, filter.ToDate, session, cancellationToken);

        var rows = new List<FundCashReceiptsReportRow>(rawRows.Count);
        decimal total = 0m;
        foreach (var raw in rawRows)
        {
            total += raw.AmountVnd;
            var (partyName, content) = CashVoucherNoteFormat.Parse(raw.RawNotes);
            rows.Add(new FundCashReceiptsReportRow(
                raw.Id,
                raw.Code,
                raw.VoucherDate,
                partyName ?? string.Empty,
                raw.AmountVnd,
                content,
                raw.CreatedByName,
                raw.FundName));
        }

        return new FundCashReceiptsReportResult(rows, rows.Count, total);
    }
}

/// <summary>
/// Dùng chung cho danh sách phiếu thu và phiếu chi tiền mặt
/// (CASH_VOUCHERS, PAYMENT_METHOD = 'CASH', STATUS_CODE = 'POSTED').
/// Đối tượng thu/chi tách từ marker "[Đối tượng: ...]" trong NOTES
/// theo quy ước <see cref="CashVoucherNoteFormat"/>.
/// </summary>
internal static class FundCashVoucherQueryHelper
{
    internal sealed record RawVoucherRow(
        long Id,
        string Code,
        DateTime VoucherDate,
        string RawNotes,
        decimal AmountVnd,
        string CreatedByName,
        string FundName);

    internal static async Task<IReadOnlyList<RawVoucherRow>> QueryAsync(
        string voucherType,
        DateTime fromDate,
        DateTime toDate,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");
        if (voucherType is not ("IN" or "OUT"))
            throw new ArgumentException("Loại phiếu phải là IN hoặc OUT.", nameof(voucherType));

        var fromStart = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);
        if (fromStart > toExclusive.AddTicks(-1))
            throw new InvalidOperationException("Từ ngày phải không lớn hơn đến ngày.");

        var rows = new List<RawVoucherRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT V.ID,
                   V.CODE,
                   V.VOUCHER_DATE,
                   COALESCE(V.NOTES, ''),
                   V.AMOUNT_VND,
                   COALESCE(U.DISPLAY_NAME, ''),
                   COALESCE(F.NAME, '')
            FROM CASH_VOUCHERS V
            LEFT JOIN USERS U ON U.ID = V.CREATED_BY
            LEFT JOIN CASH_FUNDS F ON F.ID = V.CASH_FUND_ID
            WHERE V.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND V.BRANCH_ID = @BRANCH_ID
              AND V.VOUCHER_TYPE = @VOUCHER_TYPE
              AND V.PAYMENT_METHOD = 'CASH'
              AND V.STATUS_CODE = 'POSTED'
              AND V.VOUCHER_DATE >= @FROM_START
              AND V.VOUCHER_DATE < @TO_EXCLUSIVE
            ORDER BY V.VOUCHER_DATE, V.ID
            """, connection);

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@VOUCHER_TYPE", FbDbType.VarChar, 10).Value = voucherType;
        command.Parameters.Add("@FROM_START", FbDbType.TimeStamp).Value = fromStart;
        command.Parameters.Add("@TO_EXCLUSIVE", FbDbType.TimeStamp).Value = toExclusive;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RawVoucherRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return rows;
    }
}
