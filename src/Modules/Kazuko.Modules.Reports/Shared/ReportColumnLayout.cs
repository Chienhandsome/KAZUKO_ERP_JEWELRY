using Kazuko.Core.Settings;
using Kazuko.Shared.UI.DataGridLayout;

namespace Kazuko.Modules.Reports.Shared;

/// <summary>
/// Resolve / lưu layout cột báo cáo (ẩn-hiện + thứ tự) qua
/// <see cref="DataGridColumnLayoutStore"/> (%LocalAppData%\KazukoERP).
/// </summary>
public static class ReportColumnLayout
{
    public static IReadOnlyList<ReportColumnDefinition> Resolve(
        IReadOnlyList<ReportColumnDefinition> available,
        string settingsKey)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (available.Count == 0)
            return available;

        var saved = DataGridColumnLayoutStore.Load(settingsKey);
        if (saved?.Columns is null || saved.Columns.Count == 0)
            return available.Where(c => c.DefaultVisible).ToArray();

        var byKey = available.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ReportColumnDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in saved.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (!byKey.TryGetValue(setting.Key, out var def))
                continue;
            seen.Add(def.Key);
            if (setting.IsVisible)
                ordered.Add(def);
        }

        // Cột mới chưa có trong settings: theo DefaultVisible, gắn cuối.
        foreach (var def in available)
        {
            if (seen.Contains(def.Key))
                continue;
            if (def.DefaultVisible)
                ordered.Add(def);
        }

        return ordered.Count > 0
            ? ordered
            : available.Where(c => c.DefaultVisible).DefaultIfEmpty(available[0]).ToArray();
    }

    public static void Save(
        string settingsKey,
        IReadOnlyList<ReportColumnDefinition> available,
        IReadOnlyList<ReportColumnDefinition> visibleOrdered)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(visibleOrdered);

        var visibleKeys = new HashSet<string>(
            visibleOrdered.Select(c => c.Key),
            StringComparer.OrdinalIgnoreCase);

        var settings = new List<DataGridColumnSetting>();
        var index = 0;
        foreach (var col in visibleOrdered)
            settings.Add(new DataGridColumnSetting(col.Key, true, index++));

        foreach (var col in available)
        {
            if (visibleKeys.Contains(col.Key))
                continue;
            settings.Add(new DataGridColumnSetting(col.Key, false, index++));
        }

        DataGridColumnLayoutStore.Save(settingsKey, new DataGridColumnLayoutSettings(settings));
    }

    /// <summary>Thứ tự dialog: cột đang hiện trước (đúng thứ tự), rồi cột đang ẩn.</summary>
    public static IReadOnlyList<(ReportColumnDefinition Column, bool IsVisible)> BuildDialogItems(
        IReadOnlyList<ReportColumnDefinition> available,
        IReadOnlyList<ReportColumnDefinition> activeVisible)
    {
        var visibleKeys = activeVisible
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = new List<(ReportColumnDefinition, bool)>();

        foreach (var col in activeVisible)
            items.Add((col, true));

        foreach (var col in available)
        {
            if (visibleKeys.Contains(col.Key))
                continue;
            items.Add((col, false));
        }

        return items;
    }
}
