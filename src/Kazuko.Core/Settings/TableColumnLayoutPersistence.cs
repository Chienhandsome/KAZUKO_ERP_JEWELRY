namespace Kazuko.Core.Settings;

/// <summary>Bound to one authenticated user and database; implementations own asynchronous I/O.</summary>
public interface ITableColumnLayoutRepository
{
    Task<IReadOnlyDictionary<string, DataGridColumnLayoutSettings>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string settingsKey, DataGridColumnLayoutSettings settings, CancellationToken cancellationToken = default);
}

public interface ITableColumnLayoutSession : ITableColumnLayoutStore, IAsyncDisposable
{
    string StatusMessage { get; }
    string? LastError { get; }
    event EventHandler? StatusChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public static class TableColumnLayoutValidation
{
    public static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        key = key.Trim().ToLowerInvariant();
        if (key.Length > 160 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Khóa cấu hình cột không hợp lệ.", nameof(key));
        return key;
    }

    public static DataGridColumnLayoutSettings Snapshot(DataGridColumnLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != 1 || settings.Columns is null || settings.Columns.Count is 0 or > 512)
            throw new ArgumentException("Phiên bản hoặc danh sách cấu hình cột không hợp lệ.", nameof(settings));
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in settings.Columns)
        {
            if (column is null || string.IsNullOrWhiteSpace(column.Key) || column.Key.Length > 200 ||
                !keys.Add(column.Key) || column.DisplayIndex < 0 ||
                column.Width is { } width && (!double.IsFinite(width) || width <= 0))
                throw new ArgumentException("Cấu hình cột không hợp lệ.", nameof(settings));
        }
        return settings with { Columns = settings.Columns.ToArray() };
    }
}
