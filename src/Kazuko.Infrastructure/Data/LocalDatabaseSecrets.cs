using System.Security.Cryptography;
using System.Text;

namespace Kazuko.Infrastructure.Data;

/// <summary>Local first-run credentials, encrypted for the current Windows account.</summary>
public static class LocalDatabaseSecrets
{
    public static string? Read(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local installation requires Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
    }

    public static void Create(string directory, string name, string secret, bool overwrite=false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local installation requires Windows.");
        Directory.CreateDirectory(directory);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        var temporary = Path.Combine(directory, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, Path.Combine(directory, name),overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
