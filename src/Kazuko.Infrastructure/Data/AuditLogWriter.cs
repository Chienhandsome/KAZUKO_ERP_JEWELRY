using FirebirdSql.Data.FirebirdClient;

namespace Kazuko.Infrastructure.Data;

internal static class AuditLogWriter
{
    public static async Task WriteAsync(
        string eventType,
        long? userId = null,
        string? userName = null,
        long? branchId = null,
        string? entityType = null,
        string? entityId = null,
        string? eventData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction();
            await WriteAsync(
                connection,
                transaction,
                eventType,
                userId,
                userName,
                branchId,
                entityType,
                entityId,
                eventData,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuditLogWriteException) { throw; }
        catch (Exception ex)
        {
            throw new AuditLogWriteException(ex);
        }
    }

    public static async Task WriteAsync(
        FbConnection connection,
        FbTransaction transaction,
        string eventType,
        long? userId = null,
        string? userName = null,
        long? branchId = null,
        string? entityType = null,
        string? entityId = null,
        string? eventData = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        try
        {
            await using var command = new FbCommand("""
                    INSERT INTO AUDIT_LOG
                        (USER_ID, USERNAME, BRANCH_ID, EVENT_TYPE, ENTITY_TYPE, ENTITY_ID, EVENT_DATA, MACHINE_NAME)
                    VALUES
                        (@USER_ID, @USERNAME, @BRANCH_ID, @EVENT_TYPE, @ENTITY_TYPE, @ENTITY_ID, @EVENT_DATA, @MACHINE_NAME)
                    """, connection, transaction);
            command.Parameters.AddWithValue("@USER_ID", userId is > 0 ? userId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@USERNAME", userName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BRANCH_ID", branchId is > 0 ? branchId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@EVENT_TYPE", eventType);
            command.Parameters.AddWithValue("@ENTITY_TYPE", entityType ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ENTITY_ID", entityId ?? (object)DBNull.Value);
            command.Parameters.Add("@EVENT_DATA", FbDbType.Text).Value = eventData ?? (object)DBNull.Value;
            command.Parameters.AddWithValue("@MACHINE_NAME", Environment.MachineName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new AuditLogWriteException(ex);
        }
    }
}

internal sealed class AuditLogWriteException(Exception innerException)
    : InvalidOperationException("Không thể ghi nhật ký kiểm toán. Thao tác chưa được hoàn tất; vui lòng thử lại hoặc liên hệ quản trị viên.", innerException);
