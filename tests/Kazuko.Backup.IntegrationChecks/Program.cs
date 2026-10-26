using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Infrastructure.Data;

if (args.Length != 1) throw new ArgumentException("Pass an output directory for newly created disposable test databases.");
var root = Path.Combine(Path.GetFullPath(args[0]), "backup-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var source = Path.Combine(root, "source.fdb");
var profile = ConnectionProfileStore.Load(source) with { DatabasePath = source, UseEmbedded = true };
FbConnection.CreateDatabase(FirebirdDatabase.BuildConnectionString(profile));
await using (var connection = new FbConnection(FirebirdDatabase.BuildConnectionString(profile)))
{
    await connection.OpenAsync();
    await connection.ExecuteAsync("CREATE TABLE COMPANIES (ID INTEGER PRIMARY KEY, NAME VARCHAR(100) CHARACTER SET UTF8)");
    await connection.ExecuteAsync("INSERT INTO COMPANIES VALUES (1, @name)", new { name = "Kiểm tra sao lưu KAZUKO" });
    await connection.ExecuteAsync("CREATE TABLE SCHEMA_MIGRATIONS (VERSION_NO INTEGER PRIMARY KEY)");
    await connection.ExecuteAsync("INSERT INTO SCHEMA_MIGRATIONS VALUES (89)");
}
FbConnection.ClearAllPools();
foreach (var embedded in new[] { true, false })
{
    var current = profile with { UseEmbedded = embedded };
    var manager = new FirebirdBackupManager(() => current, root: Path.Combine(root, embedded ? "embedded" : "server"));
    // Keep a connection open to prove backups use the engine rather than file copy.
    await using var online = new FbConnection(FirebirdDatabase.BuildConnectionString(current));
    await online.OpenAsync();
    var before = await online.ExecuteScalarAsync<string>("SELECT NAME FROM COMPANIES WHERE ID=1");
    var file = await manager.BackupAsync(Path.Combine(root, "manual backups"), false, default);
    var verified = await manager.VerifyAsync(file, default);
    Check(verified.SchemaVersion == 89 && verified.CompanyCount == 1, $"{(embedded ? "Embedded" : "Server")}: real gbak backup restores and validates");
    Check(await online.ExecuteScalarAsync<string>("SELECT NAME FROM COMPANIES WHERE ID=1") == before, "live source remains readable and unchanged");
    Check(manager.GetStatus().LastVerifiedPath == file, "verification status persisted");
    using (var cancelled = new CancellationTokenSource())
    {
        var progress = new CancelProgress(cancelled);
        try { await manager.BackupAsync(null, true, cancelled.Token, progress); throw new Exception("Cancelled backup was accepted"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: cancellation preserves previous successful backup"); }
        Check(manager.GetStatus().LastSuccessPath == file && File.Exists(file), "cancelled operation preserves last successful backup");
        Check(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories).Length == 0, "no partial backup left after cancellation");
    }
    var corrupt = Path.Combine(root, "corrupt.fbk"); await File.WriteAllTextAsync(corrupt, "not a Firebird backup");
    try { await manager.VerifyAsync(corrupt, default); throw new Exception("Corrupt backup was accepted"); }
    catch (IOException) { Console.WriteLine("PASS: corrupt backup rejected"); }
    await manager.BackupAsync(null, true, default);
    Check(manager.GetStatus().LastError is null, "successful retry clears previous failure");
    Console.WriteLine("Backup artifact: " + file);
    await online.CloseAsync();
    FbConnection.ClearAllPools();
}
Console.WriteLine("ALL BACKUP INTEGRATION CHECKS PASSED. Only newly created disposable databases were used: " + root);
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<string>
{
    public void Report(string value) => cancellation.Cancel();
}
