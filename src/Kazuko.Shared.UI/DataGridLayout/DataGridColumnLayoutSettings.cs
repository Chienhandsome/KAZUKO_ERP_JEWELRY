namespace Kazuko.Shared.UI.DataGridLayout;

public sealed record DataGridColumnSetting(string Key, bool IsVisible, int DisplayIndex);

public sealed record DataGridColumnLayoutSettings(IReadOnlyList<DataGridColumnSetting> Columns);
