using Kazuko.Core.Settings;

namespace Kazuko.Shared.UI.DataGridLayout;

/// <summary>Shared facade for grids and report columns; the host supplies persistence.</summary>
public static class DataGridColumnLayoutStore
{
    private static ITableColumnLayoutStore? _store;
    public static ITableColumnLayoutStore? Current => _store;

    public static void Configure(ITableColumnLayoutStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public static DataGridColumnLayoutSettings? Load(string settingsKey) => _store?.Load(settingsKey);

    public static void Save(string settingsKey, DataGridColumnLayoutSettings settings)
    {
        if (_store is null)
            throw new InvalidOperationException("The host must configure the table column layout store before opening views.");
        _store.Save(settingsKey, settings);
    }
}
