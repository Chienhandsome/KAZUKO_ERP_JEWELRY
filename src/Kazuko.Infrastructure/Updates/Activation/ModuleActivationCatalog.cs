using System.Security.Cryptography;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

public sealed class ModuleActivationCatalog : IModuleActivationCatalog
{
    private readonly IModuleLayoutPaths _paths;
    private readonly IModuleActivationLockFactory _lockFactory;
    private readonly IHostVersionProvider _hostVersionProvider;
    private readonly ActivationStateStore _stateStore;
    private readonly object _gate = new();

    public ModuleActivationCatalog(
        IModuleLayoutPaths paths,
        IModuleActivationLockFactory? lockFactory = null,
        IHostVersionProvider? hostVersionProvider = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _lockFactory = lockFactory ?? new ModuleActivationLockFactory(paths);
        _hostVersionProvider = hostVersionProvider ?? new FixedHostVersionProvider(1);
        _stateStore = new ActivationStateStore(paths);
        _paths.EnsureCreated();
    }

    public Task<ModuleActivationDocument> LoadDocumentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_stateStore.LoadOrEmpty(_hostVersionProvider.CurrentHostVersion));
        }
    }

    public async Task<ModuleActivationRecord?> GetAsync(
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document.Modules.TryGetValue(moduleId.Trim(), out var record) ? record : null;
    }

    public Task SaveAsync(ModuleActivationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ModuleId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_lockFactory.TryAcquire(out var activationLock) || activationLock is null)
            throw new ModuleActivationException("Ứng dụng đang cập nhật module. Không thể ghi trạng thái kích hoạt.");

        try
        {
            lock (_gate)
            {
                var document = _stateStore.LoadOrEmpty(_hostVersionProvider.CurrentHostVersion);
                var modules = new Dictionary<string, ModuleActivationRecord>(
                    document.Modules,
                    StringComparer.OrdinalIgnoreCase)
                {
                    [record.ModuleId] = record
                };

                var queue = RebuildQueue(modules, document.ActivationQueue);
                var updated = new ModuleActivationDocument(
                    ModuleActivationStateFile.CurrentSchemaVersion,
                    _hostVersionProvider.CurrentHostVersion,
                    DateTimeOffset.UtcNow,
                    modules,
                    queue);
                _stateStore.WriteAtomic(updated);
            }
        }
        finally
        {
            activationLock.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task<ModuleActivationAcceptResult> AcceptStagedForActivationAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stagingDirectory))
            return Task.FromResult(Fail("Thư mục staging không hợp lệ."));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_lockFactory.TryAcquire(out var activationLock) || activationLock is null)
            return Task.FromResult(Fail("Ứng dụng đang cập nhật module. Không thể nhận gói chờ kích hoạt."));

        try
        {
            lock (_gate)
            {
                return Task.FromResult(AcceptStagedCore(stagingDirectory, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ModuleActivationException ex)
        {
            return Task.FromResult(Fail(ex.Message));
        }
        catch (ModulePackageValidationException ex)
        {
            return Task.FromResult(Fail(ex.Message));
        }
        catch (IOException)
        {
            return Task.FromResult(Fail("Không thể đưa gói từ staging vào thư mục Installed."));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Fail("Không thể đưa gói từ staging vào thư mục Installed."));
        }
        finally
        {
            activationLock.Dispose();
        }
    }

    private ModuleActivationAcceptResult AcceptStagedCore(
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureCreated();

        var stagingFull = RequireDirectoryUnderRoot(_paths.StagingRoot, stagingDirectory, "staging");
        RejectReparsePoint(stagingFull, isDirectory: true);

        var manifestPath = Path.Combine(stagingFull, ModulePackageFormat.ManifestFileName);
        var signaturePath = Path.Combine(stagingFull, ModulePackageFormat.SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            throw new ModuleActivationException("Staging thiếu module.json hoặc signature.sig.");

        RejectReparsePoint(manifestPath, isDirectory: false);
        RejectReparsePoint(signaturePath, isDirectory: false);

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = ModuleManifestParser.Parse(manifestBytes);
        var packageSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));

        VerifyPayloadIntegrity(stagingFull, manifest, cancellationToken);

        var safeModuleId = SanitizeForDirectory(manifest.ModuleId);
        var safeVersion = SanitizeForDirectory(manifest.ModuleVersion);
        // Guard against silent path collapse for moduleId/version directory names.
        ModulePackagePathRules.ValidateRelativePath($"{safeModuleId}/{safeVersion}", allowEmpty: false);

        var installedDirectory = Path.Combine(_paths.InstalledRoot, safeModuleId, safeVersion);
        EnsureInstalledParentSafe(safeModuleId, safeVersion);

        if (Directory.Exists(installedDirectory))
        {
            RejectReparsePoint(installedDirectory, isDirectory: true);
            var existingManifestPath = Path.Combine(installedDirectory, ModulePackageFormat.ManifestFileName);
            if (!File.Exists(existingManifestPath))
                throw new ModuleActivationException("Thư mục Installed đã tồn tại nhưng thiếu module.json.");

            var existingSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(existingManifestPath)));
            if (!string.Equals(existingSha, packageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModuleActivationException(
                    "Phiên bản module đã được cài với nội dung khác. Từ chối ghi đè để tránh giả mạo.");
            }

            // Idempotent: same version + same package hash → refresh PendingActivation only.
            var existingRecord = UpsertPendingRecord(
                manifest,
                packageSha256,
                installedDirectory);
            return new ModuleActivationAcceptResult(
                true,
                "Gói đã có trong Installed và được giữ ở trạng thái chờ kích hoạt.",
                existingRecord,
                installedDirectory);
        }

        PromoteStagingToInstalled(stagingFull, installedDirectory, manifest, cancellationToken);

        var record = UpsertPendingRecord(manifest, packageSha256, installedDirectory);
        return new ModuleActivationAcceptResult(
            true,
            "Gói đã được đưa vào Installed và chờ kích hoạt ở lần khởi động tiếp theo.",
            record,
            installedDirectory);
    }

    private ModuleActivationRecord UpsertPendingRecord(
        ModulePackageManifest manifest,
        string packageSha256,
        string installedDirectory)
    {
        _ = installedDirectory;
        var document = _stateStore.LoadOrEmpty(_hostVersionProvider.CurrentHostVersion);
        document.Modules.TryGetValue(manifest.ModuleId, out var previous);

        // One active version invariant: keep existing ActiveVersion; only set pending.
        var record = new ModuleActivationRecord(
            manifest.ModuleId,
            ModuleActivationState.PendingActivation,
            previous?.ActiveVersion,
            previous?.PreviousVersion,
            manifest.ModuleVersion,
            "Installed",
            manifest.Publisher,
            manifest.KeyId,
            packageSha256,
            LastError: null,
            BackupId: previous?.BackupId,
            AttemptId: null,
            BuiltInFallbackAllowed: previous?.BuiltInFallbackAllowed ?? true);

        var modules = new Dictionary<string, ModuleActivationRecord>(
            document.Modules,
            StringComparer.OrdinalIgnoreCase)
        {
            [manifest.ModuleId] = record
        };

        var queue = RebuildQueue(modules, document.ActivationQueue);
        if (!queue.Contains(manifest.ModuleId, StringComparer.OrdinalIgnoreCase))
            queue = queue.Append(manifest.ModuleId).ToArray();

        var updated = new ModuleActivationDocument(
            ModuleActivationStateFile.CurrentSchemaVersion,
            _hostVersionProvider.CurrentHostVersion,
            DateTimeOffset.UtcNow,
            modules,
            queue);
        _stateStore.WriteAtomic(updated);
        return record;
    }

    private void PromoteStagingToInstalled(
        string stagingFull,
        string installedDirectory,
        ModulePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(installedDirectory)
            ?? throw new ModuleActivationException("Không xác định được thư mục Installed.");
        Directory.CreateDirectory(parent);

        var tempDirectory = Path.Combine(
            parent,
            $".tmp-promote-{SanitizeForDirectory(manifest.ModuleVersion)}-{Guid.NewGuid():N}");

        var succeeded = false;
        try
        {
            Directory.CreateDirectory(tempDirectory);

            // Copy signed metadata first.
            CopyFileVerified(
                Path.Combine(stagingFull, ModulePackageFormat.ManifestFileName),
                Path.Combine(tempDirectory, ModulePackageFormat.ManifestFileName),
                cancellationToken);
            CopyFileVerified(
                Path.Combine(stagingFull, ModulePackageFormat.SignatureFileName),
                Path.Combine(tempDirectory, ModulePackageFormat.SignatureFileName),
                cancellationToken);

            foreach (var payload in manifest.Payload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ModulePackagePathRules.ResolveUnderRoot(stagingFull, payload.Path);
                var destination = ModulePackagePathRules.ResolveUnderRoot(tempDirectory, payload.Path);
                RejectReparsePoint(source, isDirectory: false);

                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                CopyFileVerified(source, destination, cancellationToken);
                VerifyCopiedPayload(destination, payload);
            }

            // Re-check payload set matches between staging and temp (no extras required beyond allow-list).
            VerifyPayloadIntegrity(tempDirectory, manifest, cancellationToken);

            if (Directory.Exists(installedDirectory))
                throw new ModuleActivationException("Thư mục Installed xuất hiện trong lúc promote.");

            Directory.Move(tempDirectory, installedDirectory);
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
                TryDeleteDirectory(tempDirectory);
        }
    }

    private void EnsureInstalledParentSafe(string safeModuleId, string safeVersion)
    {
        var moduleRoot = Path.Combine(_paths.InstalledRoot, safeModuleId);
        var versionRoot = Path.Combine(moduleRoot, safeVersion);

        var resolvedModule = Path.GetFullPath(moduleRoot);
        var resolvedVersion = Path.GetFullPath(versionRoot);
        var installedRoot = Path.GetFullPath(_paths.InstalledRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!resolvedModule.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase) ||
            !resolvedVersion.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModuleActivationException("Đường dẫn Installed không an toàn.");
        }
    }

    private static void VerifyPayloadIntegrity(
        string root,
        ModulePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var payload in manifest.Payload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ModulePackagePathRules.ResolveUnderRoot(root, payload.Path);
            if (!File.Exists(path))
                throw new ModuleActivationException($"Staging thiếu payload '{payload.Path}'.");

            RejectReparsePoint(path, isDirectory: false);
            VerifyCopiedPayload(path, payload);
        }
    }

    private static void VerifyCopiedPayload(string path, ModulePayloadEntry payload)
    {
        var info = new FileInfo(path);
        if (info.Length != payload.Length)
            throw new ModuleActivationException($"Kích thước payload không khớp: {payload.Path}.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ModuleActivationException($"Checksum payload không khớp: {payload.Path}.");
    }

    private static void CopyFileVerified(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectReparsePoint(source, isDirectory: false);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.SequentialScan);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static string RequireDirectoryUnderRoot(string root, string candidate, string label)
    {
        if (!Directory.Exists(candidate))
            throw new ModuleActivationException($"Không tìm thấy thư mục {label}.");

        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidateFull = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Staging folder must be a strict subdirectory of StagingRoot.
        if (string.Equals(
                candidateFull,
                rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ModuleActivationException($"Thư mục {label} không hợp lệ.");
        }

        if (!candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ModuleActivationException($"Thư mục {label} nằm ngoài vùng cho phép.");

        return candidateFull;
    }

    private static void RejectReparsePoint(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            if (!Directory.Exists(path))
                return;
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                throw new ModuleActivationException("Không chấp nhận thư mục symlink/reparse trong activation.");
            return;
        }

        if (!File.Exists(path))
            return;
        var fileAttrs = File.GetAttributes(path);
        if ((fileAttrs & FileAttributes.ReparsePoint) != 0)
            throw new ModuleActivationException("Không chấp nhận tệp symlink/reparse trong activation.");
    }

    private static IReadOnlyList<string> RebuildQueue(
        IReadOnlyDictionary<string, ModuleActivationRecord> modules,
        IReadOnlyList<string> existingQueue)
    {
        var pending = modules.Values
            .Where(static m => m.State is ModuleActivationState.PendingActivation
                or ModuleActivationState.Activating
                or ModuleActivationState.RollbackPending)
            .Select(static m => m.ModuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        foreach (var id in existingQueue)
        {
            if (pending.Remove(id))
                ordered.Add(id);
        }

        foreach (var id in pending.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
            ordered.Add(id);

        return ordered;
    }

    private static string SanitizeForDirectory(string value)
    {
        var cleaned = string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new ModuleActivationException("moduleId/moduleVersion không thể dùng làm tên thư mục.");
        return cleaned;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup after failed promote.
        }
    }

    private static ModuleActivationAcceptResult Fail(string message) =>
        new(false, message);
}

public sealed class ModuleActivationException(string message) : Exception(message);
