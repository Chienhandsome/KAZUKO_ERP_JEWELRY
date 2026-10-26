using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;
using Kazuko.Core.Settings;

namespace Kazuko.Infrastructure.Data;

/// <summary>Logical backups through bundled gbak; never copies an active database file.</summary>
internal sealed class FirebirdBackupManager
{
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private readonly Func<ConnectionProfile> _profile;
    private readonly Func<string> _runtime;
    private readonly string _root;
    private readonly IGbakRunner _runner;

    internal FirebirdBackupManager(Func<ConnectionProfile>? profile = null, Func<string>? runtime = null,
        string? root = null, IGbakRunner? runner = null)
    {
        _profile = profile ?? (() => FirebirdDatabase.Profile);
        _runtime = runtime ?? (() => FirebirdRuntime.HomeDirectory);
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KazukoERP", "backups");
        _runner = runner ?? new GbakRunner();
    }

    private string Folder(ConnectionProfile profile)
    {
        var identity = $"{profile.UseEmbedded}|{profile.Host.ToUpperInvariant()}|{profile.Port}|{profile.DatabasePath.ToUpperInvariant()}|{profile.UserName.ToUpperInvariant()}";
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]);
    }

    public DatabaseBackupStatus GetStatus() => ReadStatus(_profile());

    private DatabaseBackupStatus ReadStatus(ConnectionProfile profile)
    {
        var path = Path.Combine(Folder(profile), "status.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<DatabaseBackupStatus>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Không đọc được lịch sử sao lưu.");
    }

    private void SaveStatus(ConnectionProfile profile, DatabaseBackupStatus status)
    {
        var folder = Folder(profile);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "status.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<string> BackupAsync(string? targetFolder, bool automatic, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        if (!await OperationGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Một tác vụ sao lưu/kiểm tra đang chạy. Vui lòng chờ hoàn tất.");
        var profile = _profile();
        string? partial = null;
        try
        {
            var previous = ReadStatus(profile);
            SaveStatus(profile, previous with { LastAttemptAt = DateTimeOffset.Now, LastError = null });
            EnsureProfile(profile);
            var folder = automatic ? Path.Combine(Folder(profile), "files") : Path.GetFullPath(
                string.IsNullOrWhiteSpace(targetFolder) ? throw new ArgumentException("Chưa chọn thư mục sao lưu.") : targetFolder);
            Directory.CreateDirectory(folder);
            var name = $"KAZUKO_{(automatic ? "auto" : "backup")}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.fbk";
            var final = Path.Combine(folder, name);
            partial = final + ".partial";
            progress?.Report("Đang tạo bản sao lưu…");
            await _runner.RunAsync(_runtime(), profile, ["-backup_database", DatabaseSource(profile), partial], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
                throw new IOException("gbak không tạo được bản sao lưu hoàn chỉnh.");
            File.Move(partial, final); // unique destination, never overwrite another backup
            partial = null;
            SaveStatus(profile, ReadStatus(profile) with { LastSuccessAt = DateTimeOffset.Now, LastSuccessPath = final, LastError = null });
            if (automatic) PruneAutomaticBackups(folder);
            progress?.Report("Sao lưu hoàn tất.");
            return final;
        }
        catch (Exception error)
        {
            RecordFailure(profile, error);
            throw;
        }
        finally
        {
            try { if (partial is not null && File.Exists(partial)) File.Delete(partial); }
            finally { OperationGate.Release(); }
        }
    }

    public async Task<BackupVerificationResult> VerifyAsync(string backupPath, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        if (!await OperationGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Một tác vụ sao lưu/kiểm tra đang chạy. Vui lòng chờ hoàn tất.");
        var profile = _profile();
        // The restore destination is generated here, never accepted from UI or connection settings.
        var tempFolder = Path.Combine(Path.GetTempPath(), "KazukoBackupVerify", Guid.NewGuid().ToString("N"));
        try
        {
            EnsureProfile(profile);
            var source = Path.GetFullPath(backupPath);
            if (!string.Equals(Path.GetExtension(source), ".fbk", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                throw new InvalidOperationException("Hãy chọn bản sao lưu .fbk có sẵn.");
            Directory.CreateDirectory(tempFolder);
            var restored = Path.Combine(tempFolder, "verify.fdb");
            progress?.Report("Đang khôi phục bản sao lưu vào database tạm…");
            await _runner.RunAsync(_runtime(), profile, ["-create_database", source, restored], cancellationToken);
            progress?.Report("Đang kiểm tra cấu trúc và dữ liệu KAZUKO…");
            var local = profile with { DatabasePath = restored, UseEmbedded = true };
            var builder = new FbConnectionStringBuilder(FirebirdDatabase.BuildConnectionString(local)) { Pooling = false };
            BackupVerificationResult result;
            await using (var connection = new FbConnection(builder.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
                var schema = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(VERSION_NO),0) FROM SCHEMA_MIGRATIONS", cancellationToken: cancellationToken));
                var companies = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM COMPANIES", cancellationToken: cancellationToken));
                if (schema <= 0 || companies <= 0)
                    throw new InvalidOperationException("Database khôi phục thiếu phiên bản schema hoặc dữ liệu công ty KAZUKO.");
                result = new(schema, companies);
            }
            Directory.Delete(tempFolder, true);
            SaveStatus(profile, ReadStatus(profile) with { LastVerificationAt = DateTimeOffset.Now, LastVerifiedPath = source, LastError = null });
            progress?.Report("Bản sao lưu đã khôi phục và kiểm tra thành công.");
            return result;
        }
        catch (Exception error) { RecordFailure(profile, error); throw; }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
            }
            finally { OperationGate.Release(); }
        }
    }

    private void RecordFailure(ConnectionProfile profile, Exception error)
    {
        var message = error is OperationCanceledException ? "Đã hủy tác vụ." : Redact(error.Message, profile.Password);
        SaveStatus(profile, ReadStatus(profile) with { LastAttemptAt = DateTimeOffset.Now, LastError = message });
    }

    internal static string DatabaseSource(ConnectionProfile profile) => profile.UseEmbedded
        ? Path.GetFullPath(profile.DatabasePath)
        : $"{(profile.Host.Contains(':') && !profile.Host.StartsWith('[') ? $"[{profile.Host}]" : profile.Host)}/{profile.Port}:{profile.DatabasePath}";

    private static void EnsureProfile(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Password) || string.IsNullOrWhiteSpace(profile.UserName))
            throw new InvalidOperationException("Cấu hình đăng nhập Firebird chưa đầy đủ.");
        if (!profile.UseEmbedded && (string.IsNullOrWhiteSpace(profile.Host) || profile.Port is < 1 or > 65535))
            throw new InvalidOperationException("Địa chỉ/cổng máy chủ Firebird không hợp lệ.");
        if (string.IsNullOrWhiteSpace(profile.DatabasePath)) throw new InvalidOperationException("Chưa cấu hình database.");
    }

    internal static string Redact(string message, string password) =>
        string.IsNullOrEmpty(password) ? message : message.Replace(password, "***", StringComparison.Ordinal);

    private static void PruneAutomaticBackups(string folder)
    {
        // Only complete new-format automatic backups in this database's own folder.
        foreach (var file in new DirectoryInfo(folder).GetFiles("KAZUKO_auto_*.fbk")
            .OrderByDescending(x => x.Name, StringComparer.Ordinal).Skip(7))
        {
            try { file.Delete(); }
            catch (IOException) { /* A locked old backup can be retried after the next successful backup. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}

internal interface IGbakRunner
{
    Task RunAsync(string runtime, ConnectionProfile profile, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

internal sealed class GbakRunner : IGbakRunner
{
    public async Task RunAsync(string runtime, ConnectionProfile profile, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(runtime, "gbak.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Không tìm thấy gbak.exe trong bộ runtime Firebird.");
        if (profile.UseEmbedded && arguments[0] == "-backup_database")
        {
            // Embedded Super mode holds the file in this process. Use the same engine's
            // logical backup service instead of opening it in a second gbak.exe process.
            // Let native backup detach cleanly before honoring cancellation/cleaning its output.
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() =>
                {
                    var backup = new FbBackup(FirebirdDatabase.BuildConnectionString(profile)) { Verbose = true };
                    backup.BackupFiles.Add(new FbBackupFile(arguments[2]));
                    backup.Execute();
                }, CancellationToken.None);
            }
            catch (Exception error)
            {
                throw new IOException("Firebird không sao lưu được database: " + FirebirdBackupManager.Redact(error.Message, profile.Password), error);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = runtime
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Secrets are inherited only by this child; never appear in command-line arguments or logs.
        start.Environment["ISC_USER"] = profile.UserName;
        start.Environment["ISC_PASSWORD"] = profile.Password;
        start.Environment["FIREBIRD"] = runtime;
        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("Không khởi chạy được gbak.");
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new IOException($"gbak thất bại (mã {process.ExitCode}): {FirebirdBackupManager.Redact(output.ToString(), profile.Password)}");
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }

        async Task Drain(StreamReader stream)
        {
            while (await stream.ReadLineAsync() is { } line)
            {
                lock (output)
                {
                    output.AppendLine(line);
                    if (output.Length > 4000) output.Remove(0, output.Length - 4000);
                }
            }
        }
    }
}
