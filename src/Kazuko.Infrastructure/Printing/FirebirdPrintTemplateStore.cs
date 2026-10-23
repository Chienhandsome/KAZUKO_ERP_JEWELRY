using System.Security.Cryptography;
using System.Text;
using FastReport;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Printing;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Printing;

public sealed class FirebirdPrintTemplateStore : IPrintTemplateStore
{
    private static readonly IReadOnlyDictionary<string, SeedSpec> KnownSeeds =
        new Dictionary<string, SeedSpec>(StringComparer.OrdinalIgnoreCase)
        {
            [PrintTemplateCodes.SalesReceiptA5] = new(
                "Kazuko.Infrastructure.PrintTemplates.phoi_a5_ngang.frx",
                "Bill bán hàng A5 ngang",
                "Seed từ phoi_a5_ngang.frx"),
            [PrintTemplateCodes.SalesReceiptA5Portrait] = new(
                "Kazuko.Infrastructure.PrintTemplates.phoi_a5_doc.frx",
                "Bill bán hàng A5 dọc",
                "Seed từ phoi_a5_doc.frx"),
            [PrintTemplateCodes.JewelryLabel28x12] = new(
                "Kazuko.Infrastructure.PrintTemplates.tem_nu_trang_28x12.frx",
                "Tem nữ trang 28x12",
                "Seed từ tem_nu_trang_28x12.frx"),
            [PrintTemplateCodes.JewelryLabelL28x12] = new(
                "Kazuko.Infrastructure.PrintTemplates.tem_nu_trang_l_28x12.frx",
                "Tem L.btw (QR) 28x12",
                "Seed từ tem_nu_trang_l_28x12.frx"),
        };

    public async Task<PrintTemplateDto> GetByCodeAsync(
        string code,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = NormalizeCode(code);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var existing = await TryLoadAsync(connection, null, normalized, session.BranchId, cancellationToken);
        if (existing is not null)
        {
            // Auto-upgrade only factory-seeded rows (not user imports) when embedded .frx changes.
            var refreshed = await TryRefreshSeededAsync(existing, normalized, session, cancellationToken);
            return refreshed ?? existing;
        }

        var seeded = await SeedDefaultIfKnownAsync(normalized, session, cancellationToken);
        if (seeded is not null)
            return seeded;

        throw new InvalidOperationException($"Không tìm thấy mẫu in '{normalized}'.");
    }

    private async Task<PrintTemplateDto?> TryRefreshSeededAsync(
        PrintTemplateDto existing,
        string code,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (!KnownSeeds.TryGetValue(code, out var seed))
            return null;
        if (existing.Notes is null
            || !existing.Notes.StartsWith("Seed từ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var embedded = LoadEmbeddedResource(seed.ResourceName);
        var embeddedHash = Sha256Hex(embedded);
        if (string.Equals(existing.ContentHash, embeddedHash, StringComparison.OrdinalIgnoreCase))
            return null;

        return await SaveAsync(
            code,
            existing.Name,
            embedded,
            seed.Notes,
            session,
            cancellationToken);
    }

    private readonly record struct SeedSpec(string ResourceName, string DisplayName, string Notes);

    public async Task<PrintTemplateDto> SaveAsync(
        string code,
        string name,
        byte[] content,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new InvalidOperationException("Nội dung mẫu in trống.");
        if (content.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("Mẫu in vượt quá 8 MB.");

        var normalized = NormalizeCode(code);
        var templateName = string.IsNullOrWhiteSpace(name) ? normalized : name.Trim();
        if (templateName.Length > 200)
            templateName = templateName[..200];
        var hash = Sha256Hex(content);
        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (note is { Length: > 500 })
            note = note[..500];

        ValidateFrx(content);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var companyId = await GetCompanyIdAsync(connection, transaction, session.BranchId, cancellationToken);
            var existingId = await TryGetIdAsync(connection, transaction, companyId, normalized, cancellationToken);

            long id;
            if (existingId is null)
            {
                await using var insert = new FbCommand("""
                    INSERT INTO PRINT_TEMPLATES
                        (COMPANY_ID, CODE, NAME, CONTENT, CONTENT_HASH, NOTES, UPDATED_BY, UPDATED_AT)
                    VALUES
                        (@CID, @CODE, @NAME, @CONTENT, @HASH, @NOTES, @BY, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, transaction);
                insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = normalized;
                insert.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = templateName;
                insert.Parameters.Add("@CONTENT", FbDbType.Binary).Value = content;
                insert.Parameters.Add("@HASH", FbDbType.VarChar, 64).Value = hash;
                insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = (object?)note ?? DBNull.Value;
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }
            else
            {
                id = existingId.Value;
                await using var update = new FbCommand("""
                    UPDATE PRINT_TEMPLATES
                       SET NAME = @NAME,
                           CONTENT = @CONTENT,
                           CONTENT_HASH = @HASH,
                           NOTES = @NOTES,
                           UPDATED_BY = @BY,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID
                       AND COMPANY_ID = @CID
                    """, connection, transaction);
                update.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = templateName;
                update.Parameters.Add("@CONTENT", FbDbType.Binary).Value = content;
                update.Parameters.Add("@HASH", FbDbType.VarChar, 64).Value = hash;
                update.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = (object?)note ?? DBNull.Value;
                update.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = id;
                update.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không cập nhật được mẫu in.");
            }

            await AuditLogWriter.WriteAsync(
                connection, transaction,
                "PRINT_TEMPLATE_SAVED",
                session.UserId, session.Username, session.BranchId,
                "PRINT_TEMPLATES", id.ToString(),
                $"{{\"code\":\"{normalized}\",\"hash\":\"{hash}\",\"bytes\":{content.Length}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new PrintTemplateDto(id, normalized, templateName, content, hash, DateTime.Now, note);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<PrintTemplateDto?> SeedDefaultIfKnownAsync(
        string code,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (!KnownSeeds.TryGetValue(code, out var seed))
            return null;

        var content = LoadEmbeddedResource(seed.ResourceName);
        return await SaveAsync(
            code,
            seed.DisplayName,
            content,
            seed.Notes,
            session,
            cancellationToken);
    }

    private static async Task<PrintTemplateDto?> TryLoadAsync(
        FbConnection connection,
        FbTransaction? transaction,
        string code,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = transaction is null
            ? new FbCommand("""
                SELECT T.ID, T.CODE, T.NAME, T.CONTENT, T.CONTENT_HASH, T.UPDATED_AT, COALESCE(T.NOTES, '')
                FROM PRINT_TEMPLATES T
                JOIN BRANCHES B ON B.COMPANY_ID = T.COMPANY_ID
                WHERE B.ID = @BID
                  AND T.CODE = @CODE
                """, connection)
            : new FbCommand("""
                SELECT T.ID, T.CODE, T.NAME, T.CONTENT, T.CONTENT_HASH, T.UPDATED_AT, COALESCE(T.NOTES, '')
                FROM PRINT_TEMPLATES T
                JOIN BRANCHES B ON B.COMPANY_ID = T.COMPANY_ID
                WHERE B.ID = @BID
                  AND T.CODE = @CODE
                """, connection, transaction);

        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            var blob = (byte[])reader[3];
            var notes = reader.GetString(6);
            return new PrintTemplateDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                blob,
                reader.GetString(4),
                reader.GetDateTime(5),
                string.IsNullOrWhiteSpace(notes) ? null : notes);
        }
        catch (FbException ex) when (
            ex.Message.Contains("PRINT_TEMPLATES", StringComparison.OrdinalIgnoreCase)
            && (ex.Message.Contains("not exist", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Table unknown", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Schema mẫu in chưa sẵn sàng. Vui lòng chạy migration 57 (PRINT_TEMPLATES).", ex);
        }
    }

    private static async Task<long?> TryGetIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT ID FROM PRINT_TEMPLATES
            WHERE COMPANY_ID = @CID AND CODE = @CODE
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<long> GetCompanyIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COMPANY_ID FROM BRANCHES
            WHERE ID = @BID AND IS_ACTIVE = TRUE
            """, connection, transaction);
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
            throw new InvalidOperationException("Không xác định được công ty của chi nhánh.");
        return Convert.ToInt64(value);
    }

    private static byte[] LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(FirebirdPrintTemplateStore).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Thiếu embedded template '{resourceName}'.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string NormalizeCode(string? code)
    {
        var text = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Length == 0)
            throw new InvalidOperationException("Mã mẫu in không được trống.");
        if (text.Length > 40)
            throw new InvalidOperationException("Mã mẫu in tối đa 40 ký tự.");
        return text;
    }

    private static void ValidateFrx(byte[] content)
    {
        var head = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 200));
        if (!head.Contains("<Report", StringComparison.OrdinalIgnoreCase)
            && !head.Contains("<?xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("File không phải mẫu FastReport (.frx) hợp lệ.");

        try
        {
            using var report = new Report();
            using var stream = new MemoryStream(content);
            report.Load(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Không đọc được file .frx bằng FastReport Open Source. " + ex.Message, ex);
        }
    }

    private static string Sha256Hex(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash);
    }
}
