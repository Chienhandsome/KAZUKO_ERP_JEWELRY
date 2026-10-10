using System.Text.Json;

namespace Kazuko.Infrastructure.Data;

public sealed record ConnectionProfile(
    string Host,
    int Port,
    string DatabasePath,
    string UserName,
    string Password,
    bool UseEmbedded = true);

/// <summary>
/// Loads Firebird connection settings from environment variables and an optional LocalAppData profile.
/// The Firebird engine password is never written to source control. It must be supplied via
/// <c>KAZUKO_FIREBIRD_PASSWORD</c>. Optional LocalAppData JSON may store non-secret connection fields only.
/// </summary>
public static class ConnectionProfileStore
{
    private sealed record StoredProfile(
        string Host,
        int Port,
        string DatabasePath,
        string UserName,
        bool? UseEmbedded);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERPJewelry",
        "connection.json");

    public static ConnectionProfile Load(string defaultDatabasePath)
    {
        var environment = FromEnvironment(defaultDatabasePath);
        if (IsPortableMode())
            return environment;

        try
        {
            if (!File.Exists(FilePath))
                return environment;

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return environment;

            var host = root.TryGetProperty("Host", out var hostNode) ? hostNode.GetString() : null;
            var databasePath = root.TryGetProperty("DatabasePath", out var dbNode) ? dbNode.GetString() : null;
            var userName = root.TryGetProperty("UserName", out var userNode) ? userNode.GetString() : null;
            var port = root.TryGetProperty("Port", out var portNode) && portNode.TryGetInt32(out var parsedPort)
                ? parsedPort
                : environment.Port;
            var useEmbedded = root.TryGetProperty("UseEmbedded", out var embeddedNode) &&
                              embeddedNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? embeddedNode.GetBoolean()
                : environment.UseEmbedded;

            return new ConnectionProfile(
                string.IsNullOrWhiteSpace(host) ? environment.Host : host,
                port,
                string.IsNullOrWhiteSpace(databasePath) ? environment.DatabasePath : databasePath,
                string.IsNullOrWhiteSpace(userName) ? environment.UserName : userName,
                environment.Password,
                useEmbedded);
        }
        catch
        {
            return environment;
        }
    }

    public static void Save(ConnectionProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var stored = new StoredProfile(
            profile.Host.Trim(),
            profile.Port,
            profile.DatabasePath.Trim(),
            profile.UserName.Trim(),
            profile.UseEmbedded);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ConnectionProfile FromEnvironment(string defaultDatabasePath)
    {
        var embeddedEnv = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_EMBEDDED");
        var useEmbedded = string.IsNullOrWhiteSpace(embeddedEnv)
            ? true
            : embeddedEnv is "1" or "true" or "TRUE" or "yes" or "YES";

        return new ConnectionProfile(
            Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_HOST") ?? "localhost",
            int.TryParse(Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PORT"), out var port) ? port : 3050,
            Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_DATABASE") ?? defaultDatabasePath,
            Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_USER") ?? "SYSDBA",
            Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD") ?? string.Empty,
            useEmbedded);
    }

    private static bool IsPortableMode()
    {
        var value = Environment.GetEnvironmentVariable("KAZUKO_PORTABLE_MODE");
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }
}
