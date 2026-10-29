using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Settings;

/// <summary>Captures both session and connection, so delayed saves cannot target a new login/database.</summary>
public sealed class FirebirdTableColumnLayoutRepository : ITableColumnLayoutRepository
{
    private readonly AuthenticatedSession _session;
    private readonly string _connectionString;
    public string CacheScope { get; }

    public FirebirdTableColumnLayoutRepository(AuthenticatedSession session)
    {
        if (session.UserId <= 0 || session.BranchId <= 0) throw new ArgumentException("Chưa có phiên đăng nhập hợp lệ.");
        _session = session;
        _connectionString = FirebirdDatabase.ConnectionString;
        var profile = FirebirdDatabase.Profile;
        var identity = $"{profile.UseEmbedded}|{profile.Host.ToUpperInvariant()}|{profile.Port}|{Path.GetFullPath(profile.DatabasePath).ToUpperInvariant()}|{session.UserId}|{session.BranchId}";
        CacheScope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<IReadOnlyDictionary<string, DataGridColumnLayoutSettings>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new FbConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LayoutRow>(new CommandDefinition("""
            SELECT L.SETTINGS_KEY AS SettingsKey, L.PAYLOAD AS Payload
            FROM USER_TABLE_LAYOUTS L
            WHERE L.USER_ID=@UserId AND L.COMPANY_ID=(SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId)
            """, _session, cancellationToken: cancellationToken));
        return rows.ToDictionary(row => TableColumnLayoutValidation.NormalizeKey(row.SettingsKey),
            row => TableColumnLayoutValidation.Snapshot(JsonSerializer.Deserialize<DataGridColumnLayoutSettings>(row.Payload)
                ?? throw new InvalidDataException("Cấu hình cột trong database không hợp lệ.")), StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(string settingsKey, DataGridColumnLayoutSettings settings, CancellationToken cancellationToken = default)
    {
        var key = TableColumnLayoutValidation.NormalizeKey(settingsKey);
        var snapshot = TableColumnLayoutValidation.Snapshot(settings);
        await using var connection = new FbConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var companyId = await connection.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", _session, transaction, cancellationToken: cancellationToken));
        var payload = JsonSerializer.Serialize(snapshot);
        async Task<int> ExecuteAsync(string sql)
        {
            await using var command = new FbCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@companyId", companyId);
            command.Parameters.AddWithValue("@UserId", _session.UserId);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.Add("@payload", FbDbType.Text).Value = payload;
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var count = await ExecuteAsync("""
            UPDATE USER_TABLE_LAYOUTS SET PAYLOAD=@payload, REVISION=REVISION+1, UPDATED_AT=CURRENT_TIMESTAMP
            WHERE COMPANY_ID=@companyId AND USER_ID=@UserId AND SETTINGS_KEY=@key
            """);
        if (count == 0)
            await ExecuteAsync("""
                INSERT INTO USER_TABLE_LAYOUTS (COMPANY_ID,USER_ID,SETTINGS_KEY,PAYLOAD)
                VALUES (@companyId,@UserId,@key,@payload)
                """);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class LayoutRow
    {
        public string SettingsKey { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
