using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Finance;

namespace Kazuko.Infrastructure.HumanResources;

internal sealed record PayrollAwardSource(long Id, long EmployeeId, string Kind, decimal Amount, string Description);
internal static class EmployeeMoneyStore
{
    internal static async Task<long> LockCompanyAsync(FbConnection c, FbTransaction tx, AuthenticatedSession session, CancellationToken ct)
    {
        if (session.BranchId <= 0 || session.UserId <= 0) throw new InvalidOperationException("Phiên đăng nhập không hợp lệ.");
        var company = await c.QuerySingleAsync<long>(new CommandDefinition("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { session.BranchId }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE COMPANIES SET UPDATED_AT=UPDATED_AT WHERE ID=@Id", new { Id = company }, tx, cancellationToken: ct));
        return company;
    }
    internal static async Task<IReadOnlyList<PayrollAwardSource>> PayrollAwardsAsync(FbConnection c, FbTransaction tx, long company, int year, int month, CancellationToken ct) =>
        (await c.QueryAsync<PayrollAwardSource>(new CommandDefinition("""
            SELECT ID AS Id,EMPLOYEE_ID AS EmployeeId,KIND AS Kind,AMOUNT AS Amount,DESCRIPTION AS Description
            FROM HR_EMPLOYEE_AWARDS WHERE COMPANY_ID=@company AND PAY_YEAR=@year AND PAY_MONTH=@month
            AND HANDLING='PAYROLL' AND STATUS='ACTIVE' ORDER BY ID
            """, new { company, year, month }, tx, cancellationToken: ct))).AsList();

    internal static async Task<FinancialVoucherPostResult> PostAsync(FbConnection c, FbTransaction tx, long company,
        AuthenticatedSession session, string direction, string source, long sourceId, PaymentAllocationDraft allocation,
        long categoryId, DateTime date, string party, string notes, CancellationToken ct)
    {
        var category = await FirebirdVoucherCategoryService.ResolveForVoucherAsync(c, tx, company, direction, categoryId, ct);
        var formatted = CashVoucherNoteFormat.Format(party, notes);
        if (formatted.Length > 500) formatted = formatted[..500];
        var request = new FinancialVoucherPostRequest(allocation, source, sourceId, source, sourceId, formatted);
        var poster = new FirebirdFinancialVoucherPostingService();
        var result = direction == "OUT"
            ? await poster.PostPaymentInTransactionAsync(c, tx, company, session.BranchId, request, session.UserId, ct)
            : await poster.PostReceiptInTransactionAsync(c, tx, company, session.BranchId, request, session.UserId, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE CASH_VOUCHERS SET CATEGORY_ID=@categoryId,CATEGORY_NAME=@name,VOUCHER_DATE=@date WHERE ID=@id AND COMPANY_ID=@company",
            new { categoryId, name = category.Name, date = date.Date, id = result.VoucherId, company }, tx, cancellationToken: ct));
        return result;
    }
    internal static async Task CancelVoucherAsync(FbConnection c, FbTransaction tx, long company, AuthenticatedSession session,
        long voucherId, string source, long sourceId, string reason, CancellationToken ct)
    {
        var affected = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE CASH_VOUCHERS SET STATUS_CODE='CANCELLED',CANCELLED_BY=@By,CANCELLED_AT=CURRENT_TIMESTAMP,CANCELLATION_REASON=@reason
            WHERE ID=@voucherId AND COMPANY_ID=@company AND BRANCH_ID=@BranchId AND SOURCE_DOCUMENT_TYPE=@source
             AND SOURCE_DOCUMENT_ID=@sourceId AND STATUS_CODE='POSTED'
            """, new { By = session.UserId, reason, voucherId, company, session.BranchId, source, sourceId }, tx, cancellationToken: ct));
        if (affected != 1) throw new InvalidOperationException("Phiếu thu/chi không còn ở trạng thái ghi sổ hoặc thuộc chi nhánh khác. Tải lại để kiểm tra.");
    }
    internal static string Reason(string reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500
        ? reason.Trim() : throw new InvalidOperationException("Nhập lý do hủy từ 1 đến 500 ký tự.");
}
