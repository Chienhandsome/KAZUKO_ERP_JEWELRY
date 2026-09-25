using System.Text.Json;
using Kazuko.Core.Appearance;

namespace Kazuko.Infrastructure.Settings;

public sealed class JsonPreferencesService : IPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public JsonPreferencesService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KazukoERP",
            "settings.json");
    }

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppPreferences();
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_settingsPath), JsonOptions)
                   ?? new AppPreferences();
        }
        catch (JsonException)
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }
}
