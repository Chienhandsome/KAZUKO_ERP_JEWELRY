using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.PriceBoard;

public sealed record PriceBoardHostRequest(string Action, AuthenticatedSession Session, long CompanyId,
    PriceBoardTvSettings? Settings = null, string? MonitorDevice = null);
public sealed record PriceBoardHostReply(PriceBoardHostStatus? Status, string Error = "");

public static class PriceBoardHostProtocol
{
    public const int MaximumMessageSize = 1024 * 1024;
    public static string Scope(ConnectionProfile profile, long branchId)
    {
        using var process = Process.GetCurrentProcess();
        var desktopSession = OperatingSystem.IsWindows() ? process.SessionId : 0;
        var identity = $"{Environment.UserDomainName}|{Environment.UserName}|{desktopSession}|{profile.UseEmbedded}|{profile.Host.ToUpperInvariant()}|{profile.Port}|{Path.GetFullPath(profile.DatabasePath).ToUpperInvariant()}|{branchId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..40];
    }
    public static string PipeName(string scope)
    {
        if (scope.Length != 40 || scope.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid TV host scope.");
        return "Kazuko.PriceBoard." + scope;
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumMessageSize) throw new InvalidDataException("TV message is too large.");
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageSize) throw new InvalidDataException("Invalid TV message size.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Invalid TV message.");
    }
}

/// <summary>Local-user IPC only. The LAN web server continues to expose GET only.</summary>
public sealed class PriceBoardHostPipeServer(string scope, Func<PriceBoardHostRequest, Task<PriceBoardHostReply>> handle) : IAsyncDisposable
{
    public event Action? StopRequested;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    public void Start() => _loop = Task.Run(RunAsync);
    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(PriceBoardHostProtocol.PipeName(scope), PipeDirection.InOut,
                1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                var request = await PriceBoardHostProtocol.ReadAsync<PriceBoardHostRequest>(pipe, timeout.Token);
                var reply = await handle(request);
                await PriceBoardHostProtocol.WriteAsync(pipe, reply, timeout.Token);
                if (request.Action == "stop" && string.IsNullOrEmpty(reply.Error)) StopRequested?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
            { Trace.TraceWarning("TV IPC request failed: {0}", ex.Message); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null) await _loop;
        _stop.Dispose();
    }
}

public sealed class PriceBoardTvHostClientFactory(string executablePath, string entryAssembly) : IPriceBoardTvHostClientFactory
{
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);
    public async Task<IPriceBoardTvHostClient> ConnectAsync(AuthenticatedSession session, long companyId, CancellationToken cancellationToken = default)
    {
        if (session.UserId <= 0 || session.BranchId <= 0 || companyId <= 0) throw new InvalidOperationException("Thiếu phiên bảng giá TV.");
        var profile = FirebirdDatabase.Profile;
        var scope = PriceBoardHostProtocol.Scope(profile, session.BranchId);
        var client = new Client(scope, session, companyId);
        await LaunchGate.WaitAsync(cancellationToken);
        try
        {
            try { await client.StatusAsync(cancellationToken); return client; }
            catch (TimeoutException) { }
            var start = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(entryAssembly);
            start.ArgumentList.Add("--price-board-host"); start.ArgumentList.Add(scope);
            // Credentials remain in the child process environment, never command lines or files.
            start.Environment["KAZUKO_PORTABLE_MODE"] = "1";
            start.Environment["KAZUKO_FIREBIRD_HOST"] = profile.Host;
            start.Environment["KAZUKO_FIREBIRD_PORT"] = profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["KAZUKO_FIREBIRD_DATABASE"] = profile.DatabasePath;
            start.Environment["KAZUKO_FIREBIRD_USER"] = profile.UserName;
            start.Environment["KAZUKO_FIREBIRD_PASSWORD"] = profile.Password;
            start.Environment["KAZUKO_FIREBIRD_EMBEDDED"] = profile.UseEmbedded ? "1" : "0";
            start.Environment["KAZUKO_FIREBIRD_HOME"] = FirebirdRuntime.HomeDirectory;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được tiến trình bảng giá TV.");
            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await client.StatusAsync(cancellationToken); return client; }
                catch (TimeoutException) { if (process.HasExited) break; }
            }
            throw new InvalidOperationException("Chưa kết nối được tiến trình bảng giá TV. Hãy thử lại.");
        }
        finally { LaunchGate.Release(); }
    }
    private sealed class Client(string scope, AuthenticatedSession session, long companyId) : IPriceBoardTvHostClient
    {
        private async Task<PriceBoardHostStatus> SendAsync(string action, PriceBoardTvSettings? settings = null,
            string? monitor = null, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await using var pipe = new NamedPipeClientStream(".", PriceBoardHostProtocol.PipeName(scope), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(750, cancellationToken);
            await PriceBoardHostProtocol.WriteAsync(pipe, new PriceBoardHostRequest(action, session, companyId, settings, monitor), timeout.Token);
            var reply = await PriceBoardHostProtocol.ReadAsync<PriceBoardHostReply>(pipe, timeout.Token);
            if (!string.IsNullOrWhiteSpace(reply.Error)) throw new InvalidOperationException(reply.Error);
            return reply.Status ?? throw new InvalidOperationException("Tiến trình bảng giá TV không phản hồi trạng thái.");
        }
        public Task<PriceBoardHostStatus> StatusAsync(CancellationToken cancellationToken = default) => SendAsync("status", cancellationToken: cancellationToken);
        public Task<PriceBoardHostStatus> ConfigureAsync(PriceBoardTvSettings settings, CancellationToken cancellationToken = default) => SendAsync("configure", settings, cancellationToken: cancellationToken);
        public Task<PriceBoardHostStatus> OpenDisplayAsync(string monitorDevice, CancellationToken cancellationToken = default) => SendAsync("display", monitor: monitorDevice, cancellationToken: cancellationToken);
        public async Task StopAsync(CancellationToken cancellationToken = default) => await SendAsync("stop", cancellationToken: cancellationToken);
    }
}
