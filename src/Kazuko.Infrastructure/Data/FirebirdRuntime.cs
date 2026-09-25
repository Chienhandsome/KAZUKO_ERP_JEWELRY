namespace Kazuko.Infrastructure.Data;

public static class FirebirdRuntime
{
    public static string HomeDirectory
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_HOME");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return Path.GetFullPath(env);

            var bundled = Path.Combine(AppContext.BaseDirectory, "firebird");
            if (File.Exists(Path.Combine(bundled, "fbclient.dll")))
                return Path.GetFullPath(bundled);

            foreach (var candidate in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "firebird",
                             "Firebird-5.0.4.1812-0-windows-x64"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Firebird",
                             "Firebird_5_0")
                     })
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(full, "fbclient.dll")))
                    return full;
            }

            return Path.GetFullPath(bundled);
        }
    }

    public static string ClientLibraryPath => Path.Combine(HomeDirectory, "fbclient.dll");

    public static bool HasBundledClient => File.Exists(ClientLibraryPath);
}
