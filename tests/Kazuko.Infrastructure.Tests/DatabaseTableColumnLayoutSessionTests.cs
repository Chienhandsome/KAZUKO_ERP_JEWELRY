using System.Collections.Concurrent;
using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Settings;
using Xunit;

namespace Kazuko.Infrastructure.Tests;

public sealed class DatabaseTableColumnLayoutSessionTests
{
    [Fact]
    public async Task Database_wins_over_legacy_and_missing_tables_are_imported_once()
    {
        await WithJournal(async path =>
        {
            var repository = new Repository();
            repository.Rows["existing"] = Layout(205);
            var legacy = new Legacy(Layout(120));
            await using (var session = new DatabaseTableColumnLayoutSession(repository, path, legacy))
            {
                await session.InitializeAsync();
                Assert.Equal(205, session.Load("EXISTING")!.Columns[0].Width);
                Assert.Equal(120, session.Load("missing")!.Columns[0].Width);
                await session.FlushAsync();
                Assert.Equal(120, repository.Rows["missing"].Columns[0].Width);
            }
            await using var reopened = new DatabaseTableColumnLayoutSession(repository, path, new Legacy(Layout(999)));
            await reopened.InitializeAsync();
            Assert.Equal(120, reopened.Load("missing")!.Columns[0].Width);
        });
    }

    [Fact]
    public async Task Offline_changes_survive_disposal_and_are_retried_after_reconnection()
    {
        await WithJournal(async path =>
        {
            var repository = new Repository { Offline = true };
            var legacy = new Legacy(Layout(999));
            await using (var offline = new DatabaseTableColumnLayoutSession(repository, path, legacy))
            {
                await offline.InitializeAsync();
                Assert.Null(offline.Load("table")); // No legacy import before checking the database.
                offline.Save("table", Layout(333));
                await offline.FlushAsync();
                Assert.NotNull(offline.LastError);
            }
            repository.Offline = false;
            repository.Rows["table"] = Layout(100);
            await using var reopened = new DatabaseTableColumnLayoutSession(repository, path);
            await reopened.InitializeAsync();
            await reopened.FlushAsync();
            Assert.Null(reopened.LastError);
            Assert.Equal(333, repository.Rows["table"].Columns[0].Width);
            Assert.Equal(333, reopened.Load("table")!.Columns[0].Width);
        });
    }

    [Fact]
    public async Task Acknowledgement_of_an_inflight_save_does_not_drop_a_newer_edit()
    {
        await WithJournal(async path =>
        {
            var repository = new Repository { BlockFirstSave = true };
            await using var session = new DatabaseTableColumnLayoutSession(repository, path);
            await session.InitializeAsync();
            session.Save("table", Layout(110));
            await repository.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Save("table", Layout(220));
            repository.Release.TrySetResult();
            await session.FlushAsync();
            Assert.Equal(220, repository.Rows["table"].Columns[0].Width);
            Assert.Null(session.LastError);
            await using var reopened = new DatabaseTableColumnLayoutSession(repository, path);
            await reopened.InitializeAsync();
            Assert.Equal(220, reopened.Load("table")!.Columns[0].Width);
        });
    }

    [Fact]
    public void Invalid_keys_versions_duplicate_columns_and_nonfinite_widths_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => TableColumnLayoutValidation.NormalizeKey("../another-user"));
        Assert.Throws<ArgumentException>(() => TableColumnLayoutValidation.Snapshot(Layout(double.NaN)));
        Assert.Throws<ArgumentException>(() => TableColumnLayoutValidation.Snapshot(Layout(100) with { SchemaVersion = 2 }));
        Assert.Throws<ArgumentException>(() => TableColumnLayoutValidation.Snapshot(new([
            new("code", true, 0), new("CODE", false, 1)])));
    }

    [Fact]
    public async Task Invalid_legacy_snapshot_uses_view_defaults_without_importing_bad_data()
    {
        await WithJournal(async path =>
        {
            var repository = new Repository();
            await using var session = new DatabaseTableColumnLayoutSession(repository, path, new Legacy(Layout(-1)));
            await session.InitializeAsync();
            Assert.Null(session.Load("table"));
            await session.FlushAsync();
            Assert.Empty(repository.Rows);
        });
    }

    private static DataGridColumnLayoutSettings Layout(double width) => new([
        new("code", true, 1, width), new("name", false, 0, 180)]);
    private static async Task WithJournal(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "kazuko-table-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await test(Path.Combine(directory, "cache.json")); }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    private sealed class Legacy(DataGridColumnLayoutSettings layout) : ITableColumnLayoutStore
    {
        public DataGridColumnLayoutSettings? Load(string key) => layout;
        public void Save(string key, DataGridColumnLayoutSettings settings) => throw new NotSupportedException();
    }
    private sealed class Repository : ITableColumnLayoutRepository
    {
        public ConcurrentDictionary<string, DataGridColumnLayoutSettings> Rows { get; } = new();
        public volatile bool Offline;
        public bool BlockFirstSave;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyDictionary<string, DataGridColumnLayoutSettings>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (Offline) throw new IOException("offline");
            return Task.FromResult<IReadOnlyDictionary<string, DataGridColumnLayoutSettings>>(Rows.ToDictionary());
        }
        public async Task SaveAsync(string key, DataGridColumnLayoutSettings settings, CancellationToken cancellationToken = default)
        {
            if (Offline) throw new IOException("offline");
            if (BlockFirstSave)
            {
                BlockFirstSave = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            Rows[key] = settings;
        }
    }
}
