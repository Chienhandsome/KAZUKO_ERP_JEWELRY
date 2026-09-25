using System.Security.Cryptography;
using System.Text;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

public sealed class ModuleActivationLockFactory(IModuleLayoutPaths paths) : IModuleActivationLockFactory
{
    private readonly IModuleLayoutPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public ModuleActivationLockStatus TryAcquire(TimeSpan timeout, out IModuleActivationLock? activationLock)
    {
        activationLock = null;
        Mutex? mutex = null;
        FileStream? stream = null;
        var mutexOwned = false;

        try
        {
            Directory.CreateDirectory(_paths.ModulesRoot);
            var mutexName = BuildMutexName(_paths.ModulesRoot);
            mutex = new Mutex(initiallyOwned: false, mutexName);

            bool acquired;
            try
            {
                acquired = timeout <= TimeSpan.Zero
                    ? mutex.WaitOne(0)
                    : mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // Abandoned mutex indicates a previous crash — do not mutate until recovery.
                mutex.Dispose();
                return ModuleActivationLockStatus.AbandonedRequiresRecovery;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return ModuleActivationLockStatus.Locked;
            }

            mutexOwned = true;

            stream = new FileStream(
                _paths.ActivationLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.None);

            stream.SetLength(0);
            var payload = Encoding.UTF8.GetBytes(
                $"{Environment.ProcessId}|{Environment.MachineName}|{DateTimeOffset.UtcNow:O}");
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);

            activationLock = new ModuleActivationCompositeLock(mutex, stream, ownsMutex: true);
            mutex = null;
            stream = null;
            mutexOwned = false;
            return ModuleActivationLockStatus.Acquired;
        }
        catch (IOException)
        {
            ReleasePartial(mutex, stream, mutexOwned);
            return ModuleActivationLockStatus.Locked;
        }
        catch (UnauthorizedAccessException)
        {
            ReleasePartial(mutex, stream, mutexOwned);
            return ModuleActivationLockStatus.Locked;
        }
    }

    private static void ReleasePartial(Mutex? mutex, FileStream? stream, bool mutexOwned)
    {
        try
        {
            stream?.Dispose();
        }
        catch
        {
            // ignore
        }

        if (mutex is null)
            return;

        try
        {
            if (mutexOwned)
                mutex.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        mutex.Dispose();
    }

    internal static string BuildMutexName(string modulesRoot)
    {
        var full = Path.GetFullPath(modulesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..32];
        return @"Local\KazukoERP.ModuleActivation." + hash;
    }

    private sealed class ModuleActivationCompositeLock : IModuleActivationLock
    {
        private Mutex? _mutex;
        private FileStream? _stream;
        private readonly bool _ownsMutex;

        public ModuleActivationCompositeLock(Mutex mutex, FileStream stream, bool ownsMutex)
        {
            _mutex = mutex;
            _stream = stream;
            _ownsMutex = ownsMutex;
        }

        public bool IsAcquired => _mutex is not null && _stream is not null;

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            stream?.Dispose();

            var mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex is null)
                return;

            try
            {
                if (_ownsMutex)
                    mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex may already be released.
            }

            mutex.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
