using System.Data;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

// Dashboard tài chính: đọc từ CASH_VOUCHERS + CASH_FUNDS + BANK_ACCOUNTS bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record đích.
//
// Các quyết định thiết kế (ghi chú theo yêu cầu spec):
// 1. Bảng CASH_FUNDS / BANK_ACCOUNTS KHÔNG có cột số dư (đã kiểm tra migration
//    0034 và toàn bộ migrations/ — không có ALTER nào thêm cột balance). Vì vậy
//    "tồn quỹ" / "số dư tài khoản" được suy ra = tổng thu (IN) trừ tổng chi (OUT)
//    của các phiếu STATUS_CODE='POSTED' gắn với quỹ/tài khoản đó.
// 2. Mọi truy vấn lọc theo COMPANY của chi nhánh trong session và
//    BRANCH_ID từ session — phiếu thu/chi luôn ghi theo chi nhánh (xem
//    FirebirdFinancialVoucherPostingService).
// 3. Cơ cấu chi phí: ưu tiên CATEGORY_NAME đã chụp lúc lập phiếu (migration88),
//    phiếu chưa phân loại tiếp tục nhóm theo SOURCE_DOCUMENT_TYPE
//    vì NOTES là text tự do không nhóm được. Các giá trị thực tế trong code:
//    STOCK_DOCUMENT (thanh toán nhập/xuất kho), PAYABLE_DEBT (trả công nợ NCC),
//    FINANCIAL_SETTLEMENT (quyết toán), MANUAL (phiếu tạo tay). Label tiếng Việt
//    map trong ExpenseCategoryLabel; loại chưa biết giữ nguyên mã gốc để không
//    giấu dữ liệu. Kỳ tính: tháng hiện tại (theo tab Tổng quan của spec).
// 4. Đối tượng thu/chi lưu trong NOTES theo quy ước CashVoucherNoteFormat
//    ("[Đối tượng: X] ...") vì spec v1 không thêm cột; đọc ra tách lại thành
//    PartyName / Content.
public sealed class FirebirdFinanceDashboardRepository : IFinanceDashboardRepository
{
    private const int SearchLimit = 200;

    public async Task<FinanceKpi> GetKpiAsync(AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);

        var now = DateTime.Now;
        var currentFrom = new DateTime(now.Year, now.Month, 1);
        var currentTo = currentFrom.AddMonths(1);
        var previousFrom = currentFrom.AddMonths(-1);

        var row = await connection.QuerySingleAsync<KpiRow>(
            new CommandDefinition("""
                SELECT
                    (SELECT COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED'
                        AND V.VOUCHER_DATE >= @CurrentFrom AND V.VOUCHER_DATE < @CurrentTo) AS IncomeMonth,
                    (SELECT COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'OUT' THEN V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED'
                        AND V.VOUCHER_DATE >= @CurrentFrom AND V.VOUCHER_DATE < @CurrentTo) AS ExpenseMonth,
                    (SELECT COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED'
                        AND V.VOUCHER_DATE >= @PreviousFrom AND V.VOUCHER_DATE < @CurrentFrom) AS IncomePrev,
                    (SELECT COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'OUT' THEN V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED'
                        AND V.VOUCHER_DATE >= @PreviousFrom AND V.VOUCHER_DATE < @CurrentFrom) AS ExpensePrev,
                    (SELECT COALESCE(SUM(CASE V.VOUCHER_TYPE WHEN 'IN' THEN V.AMOUNT_VND WHEN 'OUT' THEN -V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED' AND V.CASH_FUND_ID IS NOT NULL) AS CashFundBalance,
                    (SELECT COALESCE(SUM(CASE V.VOUCHER_TYPE WHEN 'IN' THEN V.AMOUNT_VND WHEN 'OUT' THEN -V.AMOUNT_VND ELSE 0 END), 0)
                       FROM CASH_VOUCHERS V
                      WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                        AND V.STATUS_CODE = 'POSTED' AND V.BANK_ACCOUNT_ID IS NOT NULL) AS BankBalanceTotal
                  FROM RDB$DATABASE
                """,
                new
                {
                    CompanyId = companyId,
                    BranchId = session.BranchId,
                    CurrentFrom = currentFrom,
                    CurrentTo = currentTo,
                    PreviousFrom = previousFrom,
                },
                cancellationToken: ct));

        return new FinanceKpi(
            row.IncomeMonth, GrowthPercent(row.IncomeMonth, row.IncomePrev),
            row.ExpenseMonth, GrowthPercent(row.ExpenseMonth, row.ExpensePrev),
            row.CashFundBalance, row.BankBalanceTotal);
    }

    public async Task<IReadOnlyList<MonthlyCashFlow>> GetMonthlyCashFlowAsync(
        int months, AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        months = Math.Clamp(months, 1, 36);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);

        var now = DateTime.Now;
        var from = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));

        // EXTRACT trả về SMALLINT -> CAST AS INTEGER để khớp constructor (int)
        // theo convention Dapper của repo (bind constructor cần kiểu khớp chính xác).
        var rows = await connection.QueryAsync<MonthFlowRow>(
            new CommandDefinition("""
                SELECT CAST(EXTRACT(YEAR FROM V.VOUCHER_DATE) AS INTEGER) AS YearNo,
                       CAST(EXTRACT(MONTH FROM V.VOUCHER_DATE) AS INTEGER) AS MonthNo,
                       COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'IN' THEN V.AMOUNT_VND ELSE 0 END), 0) AS Income,
                       COALESCE(SUM(CASE WHEN V.VOUCHER_TYPE = 'OUT' THEN V.AMOUNT_VND ELSE 0 END), 0) AS Expense
                  FROM CASH_VOUCHERS V
                 WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                   AND V.STATUS_CODE = 'POSTED'
                   AND V.VOUCHER_DATE >= @From
                 GROUP BY 1, 2
                 ORDER BY 1, 2
                """,
                new { CompanyId = companyId, BranchId = session.BranchId, From = from },
                cancellationToken: ct));

        var byMonth = rows.ToDictionary(r => (r.YearNo, r.MonthNo));
        var result = new List<MonthlyCashFlow>(months);
        for (var i = 0; i < months; i++)
        {
            var month = from.AddMonths(i);
            byMonth.TryGetValue((month.Year, month.Month), out var flow);
            result.Add(new MonthlyCashFlow(
                $"{month.Month:D2}/{month.Year}",
                flow?.Income ?? 0m,
                flow?.Expense ?? 0m));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExpenseBreakdown>> GetExpenseBreakdownAsync(
        AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);

        var now = DateTime.Now;
        var monthFrom = new DateTime(now.Year, now.Month, 1);
        var monthTo = monthFrom.AddMonths(1);

        var rows = await connection.QueryAsync<ExpenseRow>(
            new CommandDefinition("""
                SELECT V.SOURCE_DOCUMENT_TYPE AS SourceType, V.CATEGORY_NAME AS CategoryName,
                       COALESCE(SUM(V.AMOUNT_VND), 0) AS Amount
                  FROM CASH_VOUCHERS V
                 WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                   AND V.STATUS_CODE = 'POSTED' AND V.VOUCHER_TYPE = 'OUT'
                   AND V.VOUCHER_DATE >= @MonthFrom AND V.VOUCHER_DATE < @MonthTo
                 GROUP BY V.SOURCE_DOCUMENT_TYPE, V.CATEGORY_NAME
                 ORDER BY 2 DESC
                """,
                new
                {
                    CompanyId = companyId,
                    BranchId = session.BranchId,
                    MonthFrom = monthFrom,
                    MonthTo = monthTo,
                },
                cancellationToken: ct));

        var grouped = rows
            .GroupBy(r => r.CategoryName ?? ExpenseCategoryLabel(r.SourceType))
            .Select(g => new { Category = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Amount)
            .ToList();
        var total = grouped.Sum(g => g.Amount);
        if (total == 0)
            return Array.Empty<ExpenseBreakdown>();
        return grouped
            .Select(g => new ExpenseBreakdown(
                g.Category,
                g.Amount,
                Math.Round(g.Amount / total * 100m, 2)))
            .ToList();
    }

    public async Task<IReadOnlyList<FundBalance>> GetFundBalancesAsync(
        AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);
        var parameters = new { CompanyId = companyId, BranchId = session.BranchId };

        // Xem ghi chú (1) ở đầu file: số dư = thu - chi từ phiếu POSTED.
        var cashFunds = await connection.QueryAsync<FundBalance>(
            new CommandDefinition("""
                SELECT F.NAME AS FundName,
                       COALESCE(SUM(CASE V.VOUCHER_TYPE WHEN 'IN' THEN V.AMOUNT_VND WHEN 'OUT' THEN -V.AMOUNT_VND ELSE 0 END), 0) AS Balance,
                       'CASH' AS Kind
                  FROM CASH_FUNDS F
                  LEFT JOIN CASH_VOUCHERS V
                    ON V.CASH_FUND_ID = F.ID AND V.STATUS_CODE = 'POSTED' AND V.BRANCH_ID = F.BRANCH_ID
                 WHERE F.COMPANY_ID = @CompanyId AND F.BRANCH_ID = @BranchId AND F.IS_ACTIVE = TRUE
                 GROUP BY F.ID, F.NAME, F.IS_DEFAULT
                 ORDER BY F.IS_DEFAULT DESC, F.ID
                """, parameters, cancellationToken: ct));

        var bankAccounts = await connection.QueryAsync<FundBalance>(
            new CommandDefinition("""
                SELECT COALESCE(NULLIF(B.ACCOUNT_NAME, ''), B.BANK_NAME || ' - ' || B.ACCOUNT_NUMBER) AS FundName,
                       COALESCE(SUM(CASE V.VOUCHER_TYPE WHEN 'IN' THEN V.AMOUNT_VND WHEN 'OUT' THEN -V.AMOUNT_VND ELSE 0 END), 0) AS Balance,
                       'BANK' AS Kind
                  FROM BANK_ACCOUNTS B
                  LEFT JOIN CASH_VOUCHERS V
                    ON V.BANK_ACCOUNT_ID = B.ID AND V.STATUS_CODE = 'POSTED' AND V.BRANCH_ID = B.BRANCH_ID
                 WHERE B.COMPANY_ID = @CompanyId AND B.BRANCH_ID = @BranchId AND B.IS_ACTIVE = TRUE
                 GROUP BY B.ID, B.ACCOUNT_NAME, B.BANK_NAME, B.ACCOUNT_NUMBER
                 ORDER BY B.ID
                """, parameters, cancellationToken: ct));

        return cashFunds.Concat(bankAccounts).ToList();
    }

    public async Task<IReadOnlyList<CashVoucherListItem>> SearchVouchersAsync(
        string? voucherType, DateTime? from, DateTime? to,
        string? keyword, AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var type = (voucherType ?? string.Empty).Trim().ToUpperInvariant();
        var hasType = type is "IN" or "OUT" ? 1 : 0;
        var hasFrom = from.HasValue ? 1 : 0;
        var hasTo = to.HasValue ? 1 : 0;
        var search = (keyword ?? string.Empty).Trim();
        var hasSearch = search.Length > 0 ? 1 : 0;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);

        var rows = await connection.QueryAsync<VoucherRow>(
            new CommandDefinition($"""
                SELECT FIRST {SearchLimit}
                       V.ID AS Id, V.CODE AS Code, V.VOUCHER_DATE AS VoucherDate,
                       V.VOUCHER_TYPE AS VoucherType, V.NOTES AS RawNotes,
                       V.PAYMENT_METHOD AS PaymentMethod, V.AMOUNT_VND AS AmountVnd,
                       COALESCE(U.DISPLAY_NAME, '') AS CreatedByName,
                       V.STATUS_CODE AS StatusCode,
                       V.REFERENCE_NUMBER AS ReferenceNumber,
                       V.SOURCE_DOCUMENT_TYPE AS SourceDocumentType,
                       V.SOURCE_DOCUMENT_ID AS SourceDocumentId,
                       V.CATEGORY_ID AS CategoryId, V.CATEGORY_NAME AS CategoryName
                  FROM CASH_VOUCHERS V
                  LEFT JOIN USERS U ON U.ID = V.CREATED_BY
                 WHERE V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                   AND (@HasType = 0 OR V.VOUCHER_TYPE = @VoucherType)
                   AND (@HasFrom = 0 OR V.VOUCHER_DATE >= @From)
                   AND (@HasTo = 0 OR V.VOUCHER_DATE < @ToExclusive)
                   AND (@HasSearch = 0
                        OR CAST(V.CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @Keyword
                        OR CAST(COALESCE(V.NOTES, '') AS VARCHAR(500) CHARACTER SET UTF8) CONTAINING @Keyword
                        OR COALESCE(V.CATEGORY_NAME, '') CONTAINING @Keyword)
                 ORDER BY V.VOUCHER_DATE DESC, V.ID DESC
                """,
                new
                {
                    CompanyId = companyId,
                    BranchId = session.BranchId,
                    HasType = hasType,
                    VoucherType = type,
                    HasFrom = hasFrom,
                    From = from?.Date,
                    HasTo = hasTo,
                    ToExclusive = to?.Date.AddDays(1),
                    HasSearch = hasSearch,
                    Keyword = search,
                },
                cancellationToken: ct));

        return rows.Select(r =>
        {
            var (partyName, content) = CashVoucherNoteFormat.Parse(r.RawNotes);
            return new CashVoucherListItem(
                r.Id, r.Code, r.VoucherDate, r.VoucherType,
                content, partyName ?? string.Empty, r.PaymentMethod,
                r.AmountVnd, r.CreatedByName, r.StatusCode, r.CategoryId, r.CategoryName);
        }).ToList();
    }

    public async Task<CashVoucherDetail?> GetVoucherDetailAsync(
        long id, AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await GetCompanyIdAsync(connection, session.BranchId, ct);

        var row = await connection.QuerySingleOrDefaultAsync<VoucherRow>(
            new CommandDefinition("""
                SELECT V.ID AS Id, V.CODE AS Code, V.VOUCHER_DATE AS VoucherDate,
                       V.VOUCHER_TYPE AS VoucherType, V.NOTES AS RawNotes,
                       V.PAYMENT_METHOD AS PaymentMethod, V.AMOUNT_VND AS AmountVnd,
                       COALESCE(U.DISPLAY_NAME, '') AS CreatedByName,
                       V.STATUS_CODE AS StatusCode,
                       V.REFERENCE_NUMBER AS ReferenceNumber,
                       V.SOURCE_DOCUMENT_TYPE AS SourceDocumentType,
                       V.SOURCE_DOCUMENT_ID AS SourceDocumentId,
                       V.CATEGORY_ID AS CategoryId, V.CATEGORY_NAME AS CategoryName
                  FROM CASH_VOUCHERS V
                  LEFT JOIN USERS U ON U.ID = V.CREATED_BY
                 WHERE V.ID = @Id AND V.COMPANY_ID = @CompanyId AND V.BRANCH_ID = @BranchId
                """,
                new { Id = id, CompanyId = companyId, BranchId = session.BranchId },
                cancellationToken: ct));

        if (row is null)
            return null;

        var (partyName, content) = CashVoucherNoteFormat.Parse(row.RawNotes);
        return new CashVoucherDetail(
            row.Id, row.Code, row.VoucherDate, row.VoucherType,
            row.PaymentMethod, row.AmountVnd,
            partyName, content.Length == 0 ? null : content,
            row.ReferenceNumber, row.SourceDocumentType, row.SourceDocumentId,
            row.CreatedByName, row.StatusCode, row.CategoryId, row.CategoryName);
    }

    private static async Task<long> GetCompanyIdAsync(
        IDbConnection connection, long branchId, CancellationToken ct)
    {
        // Convention của repo (xem FirebirdPriceBoardRepository).
        var id = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { BranchId = branchId },
                cancellationToken: ct));
        if (id is null)
            throw new InvalidOperationException("Không tìm thấy doanh nghiệp của chi nhánh.");
        return id.Value;
    }

    private static decimal GrowthPercent(decimal current, decimal previous) =>
        previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 2);

    // Nhóm cơ cấu chi: xem ghi chú (3) ở đầu file.
    private static string ExpenseCategoryLabel(string? sourceType) =>
        (sourceType ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "MANUAL" => "Phiếu chi thủ công",
            "STOCK_DOCUMENT" => "Thanh toán nhập/xuất kho",
            "PAYABLE_DEBT" => "Trả công nợ nhà cung cấp",
            "FINANCIAL_SETTLEMENT" => "Quyết toán tài chính",
            var other => other.Length == 0 ? "Không rõ nguồn" : other,
        };

    private sealed record KpiRow(
        decimal IncomeMonth, decimal ExpenseMonth,
        decimal IncomePrev, decimal ExpensePrev,
        decimal CashFundBalance, decimal BankBalanceTotal);

    private sealed record MonthFlowRow(int YearNo, int MonthNo, decimal Income, decimal Expense);

    private sealed record ExpenseRow(string? SourceType, string? CategoryName, decimal Amount);

    private sealed record VoucherRow(
        long Id, string Code, DateTime VoucherDate, string VoucherType,
        string? RawNotes, string PaymentMethod, decimal AmountVnd,
        string CreatedByName, string StatusCode,
        string? ReferenceNumber, string? SourceDocumentType, long? SourceDocumentId, long? CategoryId, string? CategoryName);
}
