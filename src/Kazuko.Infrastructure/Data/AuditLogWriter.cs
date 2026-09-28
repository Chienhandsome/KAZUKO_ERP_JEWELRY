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
            await WriteAsync(
                connection,
                null,
                eventType,
                userId,
                userName,
                branchId,
                entityType,
                entityId,
                eventData,
                cancellationToken);
        }
        catch
        {
            // Audit must never hide the original business result.
        }
    }

    public static async Task WriteAsync(
        FbConnection connection,
        FbTransaction? transaction,
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
            await using var command = transaction is null
                ? new FbCommand("""
                    INSERT INTO AUDIT_LOG
                        (USER_ID, USERNAME, BRANCH_ID, EVENT_TYPE, ENTITY_TYPE, ENTITY_ID, EVENT_DATA, MACHINE_NAME)
                    VALUES
                        (@USER_ID, @USERNAME, @BRANCH_ID, @EVENT_TYPE, @ENTITY_TYPE, @ENTITY_ID, @EVENT_DATA, @MACHINE_NAME)
                    """, connection)
                : new FbCommand("""
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
            command.Parameters.AddWithValue("@EVENT_DATA", eventData ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@MACHINE_NAME", Environment.MachineName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Audit must never hide the original business result.
        }
    }
}
