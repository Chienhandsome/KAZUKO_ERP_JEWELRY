using System.Data;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

// Tạo/hủy phiếu thu-chi thủ công (CASH_VOUCHERS), dùng chung cho dialog
// CashVoucherDialog và các module khác gọi trực tiếp.
// Quy ước (theo spec + constraint DB đã kiểm tra):
// - Mã phiếu: StockPostingSupport.NextDocumentCodeAsync, type CASH_VOUCHER_IN/OUT,
//   prefix PT/PC (giống FirebirdFinancialVoucherPostingService).
// - SOURCE_DOCUMENT_TYPE='MANUAL', SOURCE_DOCUMENT_ID=0. Cột
//   SOURCE_ALLOCATION_TYPE/ID để NULL — partial unique index
//   UQ_CV_SOURCE_ALLOC (migration 0035) chỉ áp dụng khi cả 2 cột NOT NULL nên
//   không xung đột.
// - Đối tượng thu/chi lưu trong NOTES theo CashVoucherNoteFormat (spec v1).
// - PaymentMethod: CASH/BANK_TRANSFER/CARD. Migration 0051 đã mở rộng check
//   CK_CV_METHOD cho CARD, và CK_CV_METHOD_TARGET bắt CARD đi với BANK_ACCOUNT_ID
//   (như BANK_TRANSFER). CASH bắt buộc CASH_FUND_ID.
// - ManualVoucherRequest không mang fund/bank id nên service tự chọn:
//   CASH -> quỹ mặc định (IS_DEFAULT) của chi nhánh, fallback quỹ active đầu tiên;
//   BANK_TRANSFER/CARD -> tài khoản ngân hàng active đầu tiên của chi nhánh.
//   Chưa cấu hình -> throw InvalidOperationException với message tiếng Việt rõ ràng.
public sealed class FirebirdFinanceVoucherService : IFinanceVoucherService
{
    private const string ManualSourceType = "MANUAL";

    private readonly FirebirdFinanceDashboardRepository _dashboard = new();

    public async Task<CashVoucherDetail> CreateManualVoucherAsync(
        ManualVoucherRequest request, AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var voucherType = (request.VoucherType ?? string.Empty).Trim().ToUpperInvariant();
        if (voucherType is not (FinancialVoucherTypes.In or FinancialVoucherTypes.Out))
            throw new ArgumentException("Loại phiếu phải là IN (thu) hoặc OUT (chi).", nameof(request));
        var method = (request.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        if (method is not (PaymentMethods.Cash or PaymentMethods.BankTransfer or PaymentMethods.Card))
            throw new ArgumentException("Phương thức phải là CASH, BANK_TRANSFER hoặc CARD.", nameof(request));
        if (request.AmountVnd <= 0)
            throw new ArgumentException("Số tiền phải lớn hơn 0.", nameof(request));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { session.BranchId }, fbTx, cancellationToken: ct));
            var category = await FirebirdVoucherCategoryService.ResolveForVoucherAsync(
                connection, fbTx, companyId, voucherType, request.CategoryId, ct);

            long? cashFundId = null;
            long? bankAccountId = null;
            if (method == PaymentMethods.Cash)
                cashFundId = await ResolveDefaultCashFundAsync(connection, fbTx, companyId, session.BranchId, ct);
            else
                bankAccountId = await ResolveDefaultBankAccountAsync(connection, fbTx, companyId, session.BranchId, ct);

            var isIn = voucherType == FinancialVoucherTypes.In;
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx,
                isIn ? "CASH_VOUCHER_IN" : "CASH_VOUCHER_OUT",
                session.BranchId,
                isIn ? "PT" : "PC",
                ct);

            var voucherDate = request.VoucherDate == default ? DateTime.Now : request.VoucherDate;
            var notes = CashVoucherNoteFormat.Format(request.PartyName, request.Notes);
            var reference = (request.ReferenceNumber ?? string.Empty).Trim();

            // INSERT ... RETURNING: FirebirdClient hỗ trợ qua ExecuteReader nên
            // Dapper QuerySingleAsync đọc được ID mới.
            var id = await connection.QuerySingleAsync<long>(
                new CommandDefinition("""
                    INSERT INTO CASH_VOUCHERS
                        (COMPANY_ID, BRANCH_ID, CODE, VOUCHER_DATE, VOUCHER_TYPE, PAYMENT_METHOD,
                         AMOUNT_VND, CURRENCY_CODE, EXCHANGE_RATE, CASH_FUND_ID, BANK_ACCOUNT_ID,
                         REFERENCE_NUMBER, NOTES, SOURCE_DOCUMENT_TYPE, SOURCE_DOCUMENT_ID,
                         STATUS_CODE, CREATED_BY, CATEGORY_ID, CATEGORY_NAME)
                    VALUES
                        (@CompanyId, @BranchId, @Code, @VoucherDate, @VoucherType, @Method,
                         @Amount, 'VND', 1, @CashFundId, @BankAccountId,
                         @Reference, @Notes, @SourceType, 0,
                         'POSTED', @CreatedBy, @CategoryId, @CategoryName)
                    RETURNING ID
                    """,
                    new
                    {
                        CompanyId = companyId,
                        BranchId = session.BranchId,
                        Code = code,
                        VoucherDate = voucherDate,
                        VoucherType = voucherType,
                        Method = method,
                        Amount = request.AmountVnd,
                        CashFundId = cashFundId,
                        BankAccountId = bankAccountId,
                        Reference = reference.Length == 0 ? null : reference,
                        Notes = notes.Length == 0 ? null : notes,
                        SourceType = ManualSourceType,
                        CreatedBy = session.UserId > 0 ? session.UserId : (long?)null,
                        CategoryId = category.Id,
                        CategoryName = category.Name,
                    },
                    transaction,
                    cancellationToken: ct));

            await AuditLogWriter.WriteAsync(connection, fbTx, "MANUAL_VOUCHER_CREATED", session.UserId, session.Username,
                session.BranchId, "CASH_VOUCHERS", id.ToString(), JsonSerializer.Serialize(new { Code = code, CategoryId = category.Id, CategoryName = category.Name, request.AmountVnd }), ct);
            await transaction.CommitAsync(ct);
            return await _dashboard.GetVoucherDetailAsync(id, session, ct)
                ?? throw new InvalidOperationException($"Không đọc được phiếu vừa tạo #{id}.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task CancelVoucherAsync(
        long id, string reason, AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (id <= 0)
            throw new ArgumentException("Id phiếu không hợp lệ.", nameof(id));
        var cancelReason = (reason ?? string.Empty).Trim();
        if (cancelReason.Length == 0)
            throw new ArgumentException("Cần nhập lý do hủy phiếu.", nameof(reason));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(ct);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            var linkedSource = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT SOURCE_DOCUMENT_TYPE FROM CASH_VOUCHERS WHERE ID=@Id AND COMPANY_ID=@CompanyId AND BRANCH_ID=@BranchId",
                new { Id = id, CompanyId = companyId, BranchId = session.BranchId }, transaction, cancellationToken: ct));
            if (linkedSource is "HR_AWARD" or "HR_PAYROLL")
                throw new InvalidOperationException("Phiếu liên kết nhân sự. Hủy tại tab Khen thưởng - Kỷ luật hoặc Tính lương để cập nhật đồng thời.");
            var affected = await connection.ExecuteAsync(
                new CommandDefinition("""
                    UPDATE CASH_VOUCHERS
                       SET STATUS_CODE = 'CANCELLED',
                           CANCELLED_BY = @By,
                           CANCELLED_AT = CURRENT_TIMESTAMP,
                           CANCELLATION_REASON = @Reason
                     WHERE ID = @Id
                       AND COMPANY_ID = @CompanyId
                       AND BRANCH_ID = @BranchId
                       AND STATUS_CODE = 'POSTED'
                    """,
                    new
                    {
                        Id = id,
                        CompanyId = companyId,
                        BranchId = session.BranchId,
                        By = session.UserId > 0 ? session.UserId : (long?)null,
                        Reason = cancelReason,
                    },
                    transaction,
                    cancellationToken: ct));

            if (affected == 0)
            {
                var status = await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        "SELECT STATUS_CODE FROM CASH_VOUCHERS WHERE ID = @Id AND COMPANY_ID = @CompanyId AND BRANCH_ID = @BranchId",
                        new
                        {
                            Id = id,
                            CompanyId = companyId,
                            BranchId = session.BranchId,
                        },
                        transaction,
                        cancellationToken: ct));
                if (status is null)
                    throw new InvalidOperationException($"Không tìm thấy phiếu thu/chi #{id}.");
                if (status != FinancialVoucherStatuses.Cancelled)
                    throw new InvalidOperationException($"Không hủy được phiếu #{id} — trạng thái hiện tại: {status}.");
                // Đã CANCELLED từ trước: idempotent, coi như thành công (tránh lỗi
                // khi user double-click nút Hủy), giống cách FirebirdDocumentReversalService
                // bỏ qua phiếu đã hủy trong cùng chuỗi.
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<long?> ResolveDefaultCashFundAsync(
        FbConnection connection, FbTransaction transaction,
        long companyId, long branchId, CancellationToken ct)
    {
        var id = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("""
                SELECT FIRST 1 F.ID
                  FROM CASH_FUNDS F
                 WHERE F.COMPANY_ID = @CompanyId AND F.BRANCH_ID = @BranchId AND F.IS_ACTIVE = TRUE
                 ORDER BY F.IS_DEFAULT DESC, F.ID
                """,
                new { CompanyId = companyId, BranchId = branchId },
                transaction,
                cancellationToken: ct));
        if (id is null)
            throw new InvalidOperationException(
                "Chi nhánh chưa cấu hình quỹ tiền mặt nào đang hoạt động — không thể lập phiếu tiền mặt.");
        return id;
    }

    private static async Task<long?> ResolveDefaultBankAccountAsync(
        FbConnection connection, FbTransaction transaction,
        long companyId, long branchId, CancellationToken ct)
    {
        var id = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("""
                SELECT FIRST 1 B.ID
                  FROM BANK_ACCOUNTS B
                 WHERE B.COMPANY_ID = @CompanyId AND B.BRANCH_ID = @BranchId AND B.IS_ACTIVE = TRUE
                 ORDER BY B.ID
                """,
                new { CompanyId = companyId, BranchId = branchId },
                transaction,
                cancellationToken: ct));
        if (id is null)
            throw new InvalidOperationException(
                "Chi nhánh chưa cấu hình tài khoản ngân hàng nào đang hoạt động — không thể lập phiếu chuyển khoản/thẻ.");
        return id;
    }

    private static async Task<long> ResolveCompanyIdAsync(
        IDbConnection connection, long branchId, CancellationToken ct)
    {
        var id = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { BranchId = branchId },
                cancellationToken: ct));
        if (id is null)
            throw new InvalidOperationException("Không tìm thấy doanh nghiệp của chi nhánh.");
        return id.Value;
    }
}
