using System.Diagnostics;
using System.Text.Json;
using Kazuko.Core.Settings;

namespace Kazuko.Infrastructure.Settings;

/// <summary>One session's UI cache and durable outbox. Database work never runs on the UI thread.</summary>
public sealed class DatabaseTableColumnLayoutSession : ITableColumnLayoutSession
{
    private readonly ITableColumnLayoutRepository _repository;
    private readonly ITableColumnLayoutStore? _legacy;
    private readonly string _journalPath;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private Task? _retryLoop;
    private bool _loaded;
    private bool _disposed;
    private string? _lastError;
    private sealed record CacheEntry(DataGridColumnLayoutSettings Settings, bool Pending);

    public DatabaseTableColumnLayoutSession(ITableColumnLayoutRepository repository, string journalPath,
        ITableColumnLayoutStore? legacy = null)
    {
        _repository = repository;
        _journalPath = journalPath;
        _legacy = legacy;
        try
        {
            if (!File.Exists(journalPath)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(journalPath));
            foreach (var (key, entry) in saved ?? [])
                _entries.Add(TableColumnLayoutValidation.NormalizeKey(key),
                    entry with { Settings = TableColumnLayoutValidation.Snapshot(entry.Settings) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _entries.Clear();
            _lastError = "Không đọc được bản lưu cấu hình cột trên máy.";
            Trace.TraceWarning("Cannot read table-layout cache: {0}", ex.Message);
        }
    }

    public string? LastError { get { lock (_sync) return _lastError; } }
    public string StatusMessage
    {
        get
        {
            lock (_sync)
                return _lastError is not null ? "Cột hiển thị: chờ đồng bộ" :
                    _entries.Values.Any(entry => entry.Pending) ? "Cột hiển thị: đang lưu…" :
                    "Cột hiển thị: đã lưu vào DB";
        }
    }
    public event EventHandler? StatusChanged;
    private void Notify() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var remote = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                var pending = _entries.Where(pair => pair.Value.Pending).ToArray();
                _entries.Clear();
                foreach (var (key, value) in remote)
                    _entries[TableColumnLayoutValidation.NormalizeKey(key)] = new(TableColumnLayoutValidation.Snapshot(value), false);
                foreach (var (key, entry) in pending) _entries[key] = entry;
                _loaded = true;
                _lastError = null;
                WriteJournal();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { RecordError(ex); }
        _retryLoop ??= Task.Run(RetryLoopAsync);
        Notify();
        QueueSync();
    }

    public DataGridColumnLayoutSettings? Load(string settingsKey)
    {
        var key = TableColumnLayoutValidation.NormalizeKey(settingsKey);
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DatabaseTableColumnLayoutSession));
            if (_entries.TryGetValue(key, out var saved)) return TableColumnLayoutValidation.Snapshot(saved.Settings);
            // Never import an unscoped legacy file while the database is unavailable:
            // we have not yet established whether this account already has a layout.
            if (!_loaded) return null;
        }
        var legacy = _legacy?.Load(settingsKey);
        if (legacy is null) return null;
        try { legacy = TableColumnLayoutValidation.Snapshot(legacy); }
        catch (ArgumentException ex)
        {
            Trace.TraceWarning("Ignoring invalid legacy table layout: {0}", ex.Message);
            return null;
        }
        Save(key, legacy);
        return TableColumnLayoutValidation.Snapshot(legacy);
    }

    public void Save(string settingsKey, DataGridColumnLayoutSettings settings)
    {
        var key = TableColumnLayoutValidation.NormalizeKey(settingsKey);
        var snapshot = TableColumnLayoutValidation.Snapshot(settings);
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DatabaseTableColumnLayoutSession));
            _entries[key] = new(snapshot, true);
            WriteJournal(); // The preference is durable before reporting success to the UI.
        }
        Notify();
        QueueSync();
    }

    private void WriteJournal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_journalPath))!);
        var temporary = _journalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
            File.Move(temporary, _journalPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void QueueSync() => _ = Task.Run(async () =>
    {
        try { await FlushAsync(_stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { RecordError(ex); }
    });

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // If startup was offline, first check the DB before importing any old JSON.
            if (!_loaded)
            {
                var remote = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    foreach (var (key, settings) in remote)
                        if (!_entries.TryGetValue(key, out var local) || !local.Pending)
                            _entries[key] = new(TableColumnLayoutValidation.Snapshot(settings), false);
                    _loaded = true;
                    WriteJournal();
                }
            }
            while (true)
            {
                KeyValuePair<string, CacheEntry>[] pending;
                lock (_sync) pending = _entries.Where(pair => pair.Value.Pending).ToArray();
                if (pending.Length == 0) break;
                foreach (var (key, entry) in pending)
                {
                    await _repository.SaveAsync(key, entry.Settings, cancellationToken).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (ReferenceEquals(_entries[key], entry)) _entries[key] = entry with { Pending = false };
                        WriteJournal();
                    }
                }
            }
            lock (_sync) _lastError = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { RecordError(ex); }
        finally { _writer.Release(); Notify(); }
    }

    private void RecordError(Exception error)
    {
        lock (_sync) _lastError = "Chưa đồng bộ được cấu hình cột với DB. App sẽ tự thử lại; thay đổi đã lưu trên máy được giữ nguyên.";
        Trace.TraceWarning("Table layout synchronization failed: {0}", error.Message);
        Notify();
    }

    private async Task RetryLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await FlushAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_retryLoop is not null) await _retryLoop.ConfigureAwait(false);
        // Drain writers already in progress; callers flush before disposing.
        await _writer.WaitAsync().ConfigureAwait(false);
        _writer.Release();
    }
}
