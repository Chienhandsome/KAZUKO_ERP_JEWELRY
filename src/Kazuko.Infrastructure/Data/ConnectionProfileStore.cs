using System.Text.Json;
using System.Security.Cryptography;

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
/// Legacy connections use KAZUKO_FIREBIRD_PASSWORD; saved and automatically created connections
/// use DPAPI-protected credentials. JSON stores only non-secret connection fields.
/// </summary>
public static class ConnectionProfileStore
{
    private sealed record StoredProfile(
        string Host,
        int Port,
        string DatabasePath,
        string UserName,
        bool? UseEmbedded);

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERPJewelry");
    public static bool HasStoredProfile => File.Exists(Path.Combine(DirectoryPath,"connection.json"));

    public static ConnectionProfile Load(string defaultDatabasePath, string? profileDirectory = null)
    {
        var directory = profileDirectory ?? DirectoryPath;
        var filePath = Path.Combine(directory,"connection.json");
        var environment = FromEnvironment(defaultDatabasePath);
        if (IsPortableMode())
            return environment;

        try
        {
            if (!File.Exists(filePath))
                return environment;

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
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

            string? selectedPassword = null;
            if (root.TryGetProperty("CredentialFile", out var credentialNode))
            {
                if (credentialNode.ValueKind != JsonValueKind.String)
                    throw new CryptographicException("Invalid database credential reference.");
                var name = credentialNode.GetString();
                if (name is null || !System.Text.RegularExpressions.Regex.IsMatch(name, @"\Aconnection-[0-9a-f]{32}\.key\z"))
                    throw new CryptographicException("Invalid database credential reference.");
                selectedPassword = LocalDatabaseSecrets.Read(directory, name)
                    ?? throw new CryptographicException("Missing selected database key.");
            }

            return new ConnectionProfile(
                string.IsNullOrWhiteSpace(host) ? environment.Host : host,
                port,
                string.IsNullOrWhiteSpace(databasePath) ? environment.DatabasePath : databasePath,
                string.IsNullOrWhiteSpace(userName) ? environment.UserName : userName,
                selectedPassword ?? (root.TryGetProperty("UseLocalCredential",out var local) && local.ValueKind == JsonValueKind.True
                    ? LocalDatabaseSecrets.Read(directory,"local-database.key") ?? throw new CryptographicException("Missing local database key.")
                    : environment.Password),
                useEmbedded);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Không mở được khóa dữ liệu của tài khoản Windows này.");
        }
        catch
        {
            return environment;
        }
    }

    public static void Save(ConnectionProfile profile, string? profileDirectory = null)
    {
        var directory = profileDirectory ?? DirectoryPath;
        Directory.CreateDirectory(directory);
        var stored = new StoredProfile(
            profile.Host.Trim(),
            profile.Port,
            profile.DatabasePath.Trim(),
            profile.UserName.Trim(),
            profile.UseEmbedded);
        // Keep the local credential reference when editing the automatically created local DB.
        var localPath = Path.Combine(directory,"data","DATA.FDB");
        var local = profile.UseEmbedded && Path.GetFullPath(profile.DatabasePath).Equals(localPath,StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(directory,"local-database.key"))
            && LocalDatabaseSecrets.Read(directory,"local-database.key") == profile.Password;
        var json = JsonSerializer.SerializeToNode(stored)!;
        if(local) json["UseLocalCredential"]=true;
        string? credentialFile = null;
        var temporary = Path.Combine(directory, "connection." + Guid.NewGuid().ToString("N") + ".tmp");
        var published = false;
        try
        {
            if (!local)
            {
                credentialFile = "connection-" + Guid.NewGuid().ToString("N") + ".key";
                LocalDatabaseSecrets.Create(directory, credentialFile, profile.Password);
                json["CredentialFile"] = credentialFile;
            }
            File.WriteAllText(temporary, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, Path.Combine(directory,"connection.json"), overwrite: true);
            published = true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (!published && credentialFile is not null) File.Delete(Path.Combine(directory, credentialFile));
        }
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
            Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD")
                ?? Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD",EnvironmentVariableTarget.User) ?? string.Empty,
            useEmbedded);
    }

    private static bool IsPortableMode()
    {
        var value = Environment.GetEnvironmentVariable("KAZUKO_PORTABLE_MODE");
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }
}
