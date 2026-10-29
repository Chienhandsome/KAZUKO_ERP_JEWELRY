using System.IO;
using System.Text.Json;

using Kazuko.Core.Settings;

namespace Kazuko.Infrastructure.Settings;

/// <summary>
/// Persists DataGrid column visibility/order under LocalAppData (UI preference, not Firebird).
/// File: %LocalAppData%\KazukoERP\{settingsKey}.json
/// </summary>
public sealed class JsonTableColumnLayoutStore : ITableColumnLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public JsonTableColumnLayoutStore(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KazukoERP");

    public string GetSettingsPath(string settingsKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);
        var fileName = settingsKey.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? settingsKey
            : settingsKey + ".json";

        return Path.Combine(_directory, fileName);
    }

    public DataGridColumnLayoutSettings? Load(string settingsKey)
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
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(string settingsKey, DataGridColumnLayoutSettings settings)
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
