namespace Kazuko.Shared.UI.DataGridLayout;

/// <param name="Width">Absolute pixel width when known; null keeps XAML / auto width.</param>
public sealed record DataGridColumnSetting(string Key, bool IsVisible, int DisplayIndex, double? Width = null);

public sealed record DataGridColumnLayoutSettings(IReadOnlyList<DataGridColumnSetting> Columns);
