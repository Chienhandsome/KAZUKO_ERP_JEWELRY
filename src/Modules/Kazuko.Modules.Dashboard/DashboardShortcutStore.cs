using System.IO;
using System.Text.Json;

namespace Kazuko.Modules.Dashboard;

public interface IDashboardShortcutStore
{
    IReadOnlyList<string>? Load(long userId);
    void Save(long userId, IReadOnlyList<string> keys);
}

public sealed class JsonDashboardShortcutStore(string? directory = null) : IDashboardShortcutStore
{
    private readonly string _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KazukoERP", "home-shortcuts");

    private string FilePath(long userId) => Path.Combine(_directory, $"user-{userId}.json");

    public IReadOnlyList<string>? Load(long userId)
    {
        try
        {
            var path = FilePath(userId);
            return File.Exists(path) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(long userId, IReadOnlyList<string> keys)
    {
        Directory.CreateDirectory(_directory);
        var path = FilePath(userId);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(keys));
        File.Move(temporary, path, overwrite: true);
    }
}
