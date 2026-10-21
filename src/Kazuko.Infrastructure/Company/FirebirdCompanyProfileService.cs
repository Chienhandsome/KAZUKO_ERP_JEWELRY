using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Company;

public sealed class FirebirdCompanyProfileService : ICompanyProfileService
{
    public async Task<CompanyProfileDto> GetAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID,
                   C.NAME,
                   COALESCE(C.PHONE, ''),
                   COALESCE(C.ADDRESS, ''),
                   COALESCE(C.LOGO_PATH, ''),
                   COALESCE(C.TCCS, '')
            FROM BRANCHES B
            JOIN COMPANIES C ON C.ID = B.COMPANY_ID
            WHERE B.ID = @BRANCH_ID
              AND B.IS_ACTIVE = TRUE
              AND C.IS_ACTIVE = TRUE
            """, connection);
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tải được thông tin doanh nghiệp.");

        return new CompanyProfileDto(
            reader.GetInt64(0),
            reader.GetString(1).Trim(),
            NullIfEmpty(reader.GetString(2)),
            NullIfEmpty(reader.GetString(3)),
            NullIfEmpty(reader.GetString(4)),
            NullIfEmpty(reader.GetString(5)));
    }

    public async Task SaveAsync(
        CompanyProfileUpdateRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên doanh nghiệp không được để trống.");
        if (name.Length > 200)
            throw new InvalidOperationException("Tên doanh nghiệp tối đa 200 ký tự.");

        var phone = NormalizeOptional(request.Phone, 40);
        var address = NormalizeOptional(request.Address, 500);
        var logoPath = NormalizeOptional(request.LogoPath, 500);
        var facilityStandard = NormalizeOptional(request.FacilityStandard, 100, "TCCS");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            long companyId;
            await using (var lookup = new FbCommand("""
                SELECT C.ID
                FROM BRANCHES B
                JOIN COMPANIES C ON C.ID = B.COMPANY_ID
                WHERE B.ID = @BRANCH_ID
                  AND B.IS_ACTIVE = TRUE
                  AND C.IS_ACTIVE = TRUE
                """, connection, transaction))
            {
                lookup.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
                var result = await lookup.ExecuteScalarAsync(cancellationToken);
                if (result is null or DBNull)
                    throw new InvalidOperationException("Không tìm thấy doanh nghiệp của chi nhánh hiện tại.");
                companyId = Convert.ToInt64(result);
            }

            await using (var update = new FbCommand("""
                UPDATE COMPANIES
                   SET NAME = @NAME,
                       PHONE = @PHONE,
                       ADDRESS = @ADDRESS,
                       LOGO_PATH = @LOGO_PATH,
                       TCCS = @TCCS,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                   AND IS_ACTIVE = TRUE
                """, connection, transaction))
            {
                update.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                update.Parameters.Add("@PHONE", FbDbType.VarChar, 40).Value = (object?)phone ?? DBNull.Value;
                update.Parameters.Add("@ADDRESS", FbDbType.VarChar, 500).Value = (object?)address ?? DBNull.Value;
                update.Parameters.Add("@LOGO_PATH", FbDbType.VarChar, 500).Value = (object?)logoPath ?? DBNull.Value;
                update.Parameters.Add("@TCCS", FbDbType.VarChar, 100).Value = (object?)facilityStandard ?? DBNull.Value;
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = companyId;

                var affected = await update.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException("Không cập nhật được thông tin doanh nghiệp.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureBranch(AuthenticatedSession session)
    {
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên làm việc thiếu chi nhánh.");
    }

    private static string? NormalizeOptional(string? value, int maxLength, string? fieldName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException(
                fieldName is null
                    ? $"Giá trị vượt quá {maxLength} ký tự."
                    : $"{fieldName} tối đa {maxLength} ký tự.");
        return trimmed;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
