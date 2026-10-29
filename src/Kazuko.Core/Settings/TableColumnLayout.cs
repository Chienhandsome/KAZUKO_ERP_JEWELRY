namespace Kazuko.Core.Settings;

/// <param name="Width">Absolute pixel width; null keeps the view's default width.</param>
public sealed record DataGridColumnSetting(string Key, bool IsVisible, int DisplayIndex, double? Width = null);

public sealed record DataGridColumnLayoutSettings(IReadOnlyList<DataGridColumnSetting> Columns, int SchemaVersion = 1);

/// <summary>
/// UI-facing layout cache. Database implementations hydrate the cache
/// asynchronously before opening views and queue writes without blocking the UI.
/// Keys identify tables and columns, independently of translated headers.
/// </summary>
public interface ITableColumnLayoutStore
{
    DataGridColumnLayoutSettings? Load(string settingsKey);
    void Save(string settingsKey, DataGridColumnLayoutSettings settings);
}
