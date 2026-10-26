using Kazuko.Infrastructure.Data;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Data;

[Collection(nameof(FirebirdTestCollection))]
public sealed class FirebirdBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "KazukoBackupTests", Guid.NewGuid().ToString("N"));
    private readonly ConnectionProfile _profile = new("db-server", 3051, @"D:\server-only\ERP.fdb", "backup_user", "test-secret", false);

    [Fact]
    public async Task Remote_backup_uses_server_source_and_local_output_without_secret_in_arguments()
    {
        var runner = new FakeRunner();
        var manager = Manager(runner);
        var file = await manager.BackupAsync(Path.Combine(_root, "manual space"), false, default);
        Assert.Equal(@"db-server/3051:D:\server-only\ERP.fdb", runner.Arguments![1]);
        Assert.EndsWith(".partial", runner.Arguments[2]);
        Assert.DoesNotContain(runner.Arguments, x => x.Contains(_profile.Password));
        Assert.EndsWith(".fbk", file);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(runner.Arguments[2]));
        Assert.Equal(file, manager.GetStatus().LastSuccessPath);
        Assert.NotNull(manager.GetStatus().LastSuccessAt);
    }

    [Fact]
    public async Task Failure_cleans_partial_preserves_last_success_and_records_redacted_error()
    {
        var runner = new FakeRunner();
        var manager = Manager(runner);
        var first = await manager.BackupAsync(null, true, default);
        runner.Error = new IOException("disk error test-secret");
        await Assert.ThrowsAsync<IOException>(() => manager.BackupAsync(null, true, default));
        var status = Manager(runner).GetStatus();
        Assert.Equal(first, status.LastSuccessPath);
        Assert.Equal("disk error ***", status.LastError);
        Assert.True(File.Exists(first));
        Assert.Empty(Directory.GetFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancellation_cleans_partial_and_does_not_publish_backup()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new FakeRunner { OnRun = () => cancellation.Cancel() };
        var manager = Manager(runner);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.BackupAsync(null, true, cancellation.Token));
        Assert.Null(manager.GetStatus().LastSuccessPath);
        Assert.Equal("Đã hủy tác vụ.", manager.GetStatus().LastError);
        Assert.Empty(Directory.GetFiles(_root, "*.fbk", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Retention_keeps_seven_complete_auto_backups_and_preserves_manual_and_other_database()
    {
        var manager = Manager(new FakeRunner());
        var manual = await manager.BackupAsync(Path.Combine(_root, "manual"), false, default);
        var other = new FirebirdBackupManager(() => _profile with { DatabasePath = "OTHER" }, root: _root, runner: new FakeRunner());
        var otherFile = await other.BackupAsync(null, true, default);
        var outputs = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            outputs.Add(await manager.BackupAsync(null, true, default));
            await Task.Delay(2);
        }
        Assert.Equal(7, Directory.GetFiles(Path.GetDirectoryName(outputs[0])!, "*.fbk").Length);
        Assert.True(File.Exists(outputs[^1]));
        Assert.True(File.Exists(manual));
        Assert.True(File.Exists(otherFile));
    }

    [Fact]
    public async Task Failed_backup_does_not_prune_old_files()
    {
        var runner = new FakeRunner();
        var manager = Manager(runner);
        var first = await manager.BackupAsync(null, true, default);
        var folder = Path.GetDirectoryName(first)!;
        for (var i = 0; i < 8; i++) File.WriteAllText(Path.Combine(folder, $"KAZUKO_auto_20000101_{i}.fbk"), "old");
        runner.Error = new IOException("out of space");
        await Assert.ThrowsAsync<IOException>(() => manager.BackupAsync(null, true, default));
        Assert.Equal(9, Directory.GetFiles(folder, "*.fbk").Length);
    }

    [Fact]
    public async Task Verification_uses_create_in_unique_temporary_directory_and_cleans_failed_restore()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "chosen.fbk"); File.WriteAllText(source, "bad backup");
        var runner = new FakeRunner { Error = new IOException("corrupt backup") };
        await Assert.ThrowsAsync<IOException>(() => Manager(runner).VerifyAsync(source, default));
        Assert.Equal("-create_database", runner.Arguments![0]);
        Assert.Equal(source, runner.Arguments[1]);
        Assert.Contains("KazukoBackupVerify", runner.Arguments[2]);
        Assert.NotEqual(_profile.DatabasePath, runner.Arguments[2]);
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Arguments[2])));
        Assert.Equal("bad backup", File.ReadAllText(source));
    }

    [Fact]
    public async Task Concurrent_operation_is_rejected_without_interfering_with_running_backup()
    {
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner { Pause = pause };
        var manager = Manager(runner);
        var first = manager.BackupAsync(null, true, default);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.BackupAsync(null, true, default));
            Assert.Null(manager.GetStatus().LastSuccessAt);
        }
        finally { pause.TrySetResult(); }
        Assert.True(File.Exists(await first));
        Assert.Null(manager.GetStatus().LastError);
    }

    [Fact]
    public async Task Invalid_target_reports_failure_and_preserves_previous_backup()
    {
        var manager = Manager(new FakeRunner());
        var first = await manager.BackupAsync(null, true, default);
        var notAFolder = Path.Combine(_root, "not-a-folder"); File.WriteAllText(notAFolder, "existing");
        await Assert.ThrowsAsync<IOException>(() => manager.BackupAsync(notAFolder, false, default));
        Assert.True(File.Exists(first));
        Assert.Equal(first, manager.GetStatus().LastSuccessPath);
        Assert.NotNull(manager.GetStatus().LastError);
        Assert.Equal("existing", File.ReadAllText(notAFolder));
    }

    [Fact]
    public async Task Missing_gbak_is_reported_without_creating_complete_backup()
    {
        var manager = new FirebirdBackupManager(() => _profile, () => _root, _root);
        await Assert.ThrowsAsync<FileNotFoundException>(() => manager.BackupAsync(null, true, default));
        Assert.Contains("gbak.exe", manager.GetStatus().LastError);
        Assert.Empty(Directory.GetFiles(_root, "*.fbk", SearchOption.AllDirectories));
    }

    private FirebirdBackupManager Manager(FakeRunner runner) => new(() => _profile, root: _root, runner: runner);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class FakeRunner : IGbakRunner
    {
        public IReadOnlyList<string>? Arguments;
        public Exception? Error;
        public Action? OnRun;
        public TaskCompletionSource? Pause;
        public Task RunAsync(string runtime, ConnectionProfile profile, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Arguments = arguments;
            File.WriteAllText(arguments[2], "partial or complete test backup");
            OnRun?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null) throw Error;
            return Pause?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
