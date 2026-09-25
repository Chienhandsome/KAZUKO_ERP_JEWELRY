using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

public sealed class ModuleActivationLockFactory(IModuleLayoutPaths paths) : IModuleActivationLockFactory
{
    private readonly IModuleLayoutPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public bool TryAcquire(out IModuleActivationLock? activationLock)
    {
        activationLock = null;
        try
        {
            Directory.CreateDirectory(_paths.ModulesRoot);
            var stream = new FileStream(
                _paths.ActivationLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.None);
            try
            {
                stream.SetLength(0);
                var payload = System.Text.Encoding.UTF8.GetBytes(
                    $"{Environment.ProcessId}|{Environment.MachineName}|{DateTimeOffset.UtcNow:O}");
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            activationLock = new ModuleActivationFileLock(stream);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class ModuleActivationFileLock(FileStream stream) : IModuleActivationLock
    {
        private FileStream? _stream = stream;

        public bool IsAcquired => _stream is not null;

        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _stream, null);
            s?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
