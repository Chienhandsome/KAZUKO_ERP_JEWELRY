using FirebirdSql.Data.FirebirdClient;

namespace Kazuko.Infrastructure.Data;

public static class FirebirdDatabase
{
    private static ConnectionProfile profile = ConnectionProfileStore.Load(ResolveDefaultDatabasePath());

    public static string DatabasePath => Path.GetFullPath(profile.DatabasePath);

    public static string ConnectionString => BuildConnectionString(profile);

    public static bool UseEmbedded => profile.UseEmbedded;

    public static ConnectionProfile Profile => profile;

    public static FbConnection CreateConnection() => new(ConnectionString);

    public static string BuildConnectionString(ConnectionProfile value)
    {
        EnsureClientLibrary(value);
        EnsureEnginePassword(value);

        var builder = new FbConnectionStringBuilder
        {
            Database = Path.GetFullPath(value.DatabasePath),
            UserID = value.UserName,
            Password = value.Password,
            Charset = "UTF8",
            Dialect = 3,
            Pooling = true,
            ConnectionTimeout = 8,
            ClientLibrary = FirebirdRuntime.ClientLibraryPath
        };

        if (value.UseEmbedded)
        {
            builder.ServerType = FbServerType.Embedded;
        }
        else
        {
            builder.DataSource = value.Host;
            builder.Port = value.Port;
            builder.ServerType = FbServerType.Default;
        }

        return builder.ConnectionString;
    }

    public static void SaveAndApplyProfile(ConnectionProfile value)
    {
        ConnectionProfileStore.Save(value);
        profile = value;
        FbConnection.ClearAllPools();
    }

    public static void ApplyProfileForCurrentProcess(ConnectionProfile value)
    {
        profile = value;
        FbConnection.ClearAllPools();
    }

    private static void EnsureEnginePassword(ConnectionProfile value)
    {
        if (string.IsNullOrWhiteSpace(value.Password))
            throw new InvalidOperationException(
                "Cấu hình kết nối cơ sở dữ liệu chưa đầy đủ. Vui lòng liên hệ quản trị viên.");
    }

    private static void EnsureClientLibrary(ConnectionProfile value)
    {
        if (!FirebirdRuntime.HasBundledClient)
            throw new FileNotFoundException("Không tìm thấy thư viện Firebird Embedded.");

        if (value.UseEmbedded)
            return;

        if (string.IsNullOrWhiteSpace(value.Host))
            throw new InvalidOperationException("Chế độ Server cần địa chỉ máy chủ Firebird.");
    }

    private static string ResolveDefaultDatabasePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "data.fdb");
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "data", "data.fdb");
    }
}
