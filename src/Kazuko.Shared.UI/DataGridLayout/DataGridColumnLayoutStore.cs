using System.IO;
using System.Text.Json;

namespace Kazuko.Shared.UI.DataGridLayout;

/// <summary>
/// Persists DataGrid column visibility/order under LocalAppData (UI preference, not Firebird).
/// File: %LocalAppData%\KazukoERP\{settingsKey}.json
/// </summary>
public static class DataGridColumnLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetSettingsPath(string settingsKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);
        var fileName = settingsKey.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? settingsKey
            : settingsKey + ".json";

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KazukoERP",
            fileName);
    }

    public static DataGridColumnLayoutSettings? Load(string settingsKey)
    {
        try
        {
            var path = GetSettingsPath(settingsKey);
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<DataGridColumnLayoutSettings>(
                       File.ReadAllText(path),
                       JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Save(string settingsKey, DataGridColumnLayoutSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);
        ArgumentNullException.ThrowIfNull(settings);

        var path = GetSettingsPath(settingsKey);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
