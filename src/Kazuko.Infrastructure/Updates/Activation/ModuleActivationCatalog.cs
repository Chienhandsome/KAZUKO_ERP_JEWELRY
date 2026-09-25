using System.Security.Cryptography;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

public sealed class ModuleActivationException : Exception
{
    public ModuleActivationException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}

public sealed class ModuleActivationCatalog : IModuleActivationCatalog
{
    private readonly IModuleLayoutPaths _paths;
    private readonly IModuleActivationLockFactory _lockFactory;
    private readonly IHostVersionProvider _hostVersionProvider;
    private readonly ActivationStateStore _stateStore;
    private readonly object _gate = new();

    /// <summary>
    /// Test seam: async chunked file copy. Must honor cancellation.
    /// </summary>
    internal Func<string, string, CancellationToken, Task>? CopyFileAsyncForTests { get; set; }

    /// <summary>
    /// Test seam invoked after Installed rename and before state commit.
    /// </summary>
    internal Action? AfterInstalledRenameBeforeStateCommitForTests { get; set; }

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
            return Task.FromResult(_stateStore.Load(_hostVersionProvider.CurrentHostVersion, _paths));
        }
    }

    public async Task<ModuleActivationRecord?> GetAsync(
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document.Modules.TryGetValue(moduleId.Trim(), out var record)
            ? record
            : document.Modules.FirstOrDefault(p =>
                string.Equals(p.Key, moduleId.Trim(), StringComparison.OrdinalIgnoreCase)).Value;
    }

    public async Task<ModuleActivationAcceptResult> AcceptStagedForActivationAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stagingDirectory))
            return Fail(ModuleActivationErrorCodes.InvalidStaging, "Thư mục staging không hợp lệ.");

        cancellationToken.ThrowIfCancellationRequested();

        var lockStatus = _lockFactory.TryAcquire(TimeSpan.FromSeconds(2), out var activationLock);
        if (lockStatus == ModuleActivationLockStatus.Locked || activationLock is null)
            return Fail(ModuleActivationErrorCodes.Locked, "Ứng dụng đang cập nhật module. Không thể nhận gói chờ kích hoạt.");

        if (lockStatus == ModuleActivationLockStatus.AbandonedRequiresRecovery)
            return Fail(ModuleActivationErrorCodes.AbandonedLock, "Khóa kích hoạt bị bỏ dở; cần recovery trước khi tiếp tục.");

        try
        {
            return await AcceptStagedCoreAsync(stagingDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ModuleActivationException ex)
        {
            return Fail(ex.ErrorCode ?? ModuleActivationErrorCodes.InvalidStaging, ex.Message);
        }
        catch (ModulePackageValidationException ex)
        {
            return Fail(ModuleActivationErrorCodes.InvalidStaging, ex.Message);
        }
        catch (IOException)
        {
            return Fail(ModuleActivationErrorCodes.InvalidStaging, "Không thể đưa gói từ staging vào thư mục Installed.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(ModuleActivationErrorCodes.InvalidStaging, "Không thể đưa gói từ staging vào thư mục Installed.");
        }
        finally
        {
            activationLock.Dispose();
        }
    }

    private async Task<ModuleActivationAcceptResult> AcceptStagedCoreAsync(
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureCreated();

        ModuleActivationDocument document;
        lock (_gate)
        {
            document = _stateStore.Load(_hostVersionProvider.CurrentHostVersion, _paths);
        }

        var stagingFull = RequireDirectoryUnderRoot(_paths.StagingRoot, stagingDirectory, "staging");
        RejectReparseChain(stagingFull, _paths.StagingRoot);

        var manifestPath = Path.Combine(stagingFull, ModulePackageFormat.ManifestFileName);
        var signaturePath = Path.Combine(stagingFull, ModulePackageFormat.SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            throw new ModuleActivationException("Staging thiếu module.json hoặc signature.sig.", ModuleActivationErrorCodes.InvalidStaging);

        var manifestBytes = await ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var signatureBytes = await ReadAllBytesAsync(signaturePath, cancellationToken).ConfigureAwait(false);
        var manifest = ModuleManifestParser.Parse(manifestBytes);
        var fingerprint = ComputePackageFingerprint(manifestBytes, signatureBytes);

        if (ReservedHostModules.IsDenied(manifest.ModuleId))
        {
            throw new ModuleActivationException(
                $"Module '{manifest.ModuleId}' thuộc host và không được override qua package trong B1.",
                ModuleActivationErrorCodes.ReservedModule);
        }

        await VerifyExactStagedContentsAsync(stagingFull, manifest, cancellationToken).ConfigureAwait(false);

        var safeModuleId = SanitizeForDirectory(manifest.ModuleId);
        var safeVersion = SanitizeForDirectory(manifest.ModuleVersion);
        ModulePackagePathRules.ValidateRelativePath($"{safeModuleId}/{safeVersion}", allowEmpty: false);

        var relativeDirectory = $"{safeModuleId}/{safeVersion}";
        var installedDirectory = Path.Combine(_paths.InstalledRoot, safeModuleId, safeVersion);
        EnsureInstalledParentSafe(safeModuleId, safeVersion);

        document.Modules.TryGetValue(manifest.ModuleId, out var existing);
        existing ??= document.Modules.FirstOrDefault(p =>
            string.Equals(p.Key, manifest.ModuleId, StringComparison.OrdinalIgnoreCase)).Value;

        ValidatePendingConflict(existing, manifest.ModuleVersion, fingerprint);

        var createdInstall = false;
        if (Directory.Exists(installedDirectory))
        {
            RejectReparseChain(installedDirectory, _paths.InstalledRoot);
            await VerifyExactInstalledContentsAsync(installedDirectory, fingerprint, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await PromoteStagingToInstalledAsync(stagingFull, installedDirectory, manifest, cancellationToken)
                .ConfigureAwait(false);
            createdInstall = true;

            try
            {
                AfterInstalledRenameBeforeStateCommitForTests?.Invoke();
            }
            catch
            {
                TryDeleteDirectory(installedDirectory);
                throw;
            }
        }

        ModuleActivationRecord record;
        try
        {
            lock (_gate)
            {
                // Reload under lock for revision consistency.
                document = _stateStore.Load(_hostVersionProvider.CurrentHostVersion, _paths);
                document.Modules.TryGetValue(manifest.ModuleId, out existing);
                existing ??= document.Modules.FirstOrDefault(p =>
                    string.Equals(p.Key, manifest.ModuleId, StringComparison.OrdinalIgnoreCase)).Value;

                ValidatePendingConflict(existing, manifest.ModuleVersion, fingerprint);
                record = CommitPendingActivation(document, existing, manifest, fingerprint, relativeDirectory);
            }
        }
        catch
        {
            if (createdInstall)
                TryDeleteDirectory(installedDirectory);
            throw;
        }

        string? cleanupWarning = null;
        try
        {
            if (Directory.Exists(stagingFull))
                Directory.Delete(stagingFull, recursive: true);
        }
        catch (Exception ex)
        {
            cleanupWarning = $"Installed + Pending đã commit nhưng không xóa được staging: {ex.Message}";
        }

        return new ModuleActivationAcceptResult(
            true,
            "Gói đã được đưa vào Installed và chờ kích hoạt ở lần khởi động tiếp theo.",
            ErrorCode: null,
            record,
            installedDirectory,
            cleanupWarning);
    }

    private ModuleActivationRecord CommitPendingActivation(
        ModuleActivationDocument document,
        ModuleActivationRecord? existing,
        ModulePackageManifest manifest,
        string fingerprint,
        string relativeDirectory)
    {
        var installedVersions = new List<ModuleInstalledVersionRecord>(
            existing?.InstalledVersions ?? Array.Empty<ModuleInstalledVersionRecord>());

        var existingVersion = installedVersions.FirstOrDefault(v =>
            string.Equals(v.Version, manifest.ModuleVersion, StringComparison.OrdinalIgnoreCase));

        if (existingVersion is null)
        {
            installedVersions.Add(new ModuleInstalledVersionRecord(
                manifest.ModuleVersion,
                fingerprint,
                DateTimeOffset.UtcNow,
                relativeDirectory,
                manifest.Publisher,
                manifest.KeyId));
        }
        else if (!string.Equals(existingVersion.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModuleActivationException(
                "Phiên bản module đã được cài với fingerprint khác.",
                ModuleActivationErrorCodes.TamperedInstall);
        }

        installedVersions = installedVersions
            .OrderBy(static v => v.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var record = new ModuleActivationRecord(
            manifest.ModuleId,
            ModuleActivationState.PendingActivation,
            existing?.ActiveVersion,
            existing?.PreviousVersion,
            manifest.ModuleVersion,
            fingerprint,
            "Installed",
            LastError: null,
            BackupId: existing?.BackupId,
            AttemptId: null,
            BuiltInFallbackAllowed: existing?.BuiltInFallbackAllowed ?? true,
            installedVersions);

        var modules = new SortedDictionary<string, ModuleActivationRecord>(StringComparer.Ordinal);
        foreach (var pair in document.Modules)
            modules[pair.Key] = pair.Value;

        // Replace by canonical module id (preserve key casing if present).
        var key = modules.Keys.FirstOrDefault(k =>
            string.Equals(k, manifest.ModuleId, StringComparison.OrdinalIgnoreCase)) ?? manifest.ModuleId;
        modules[key] = record with { ModuleId = key };

        var queue = RebuildQueue(modules, document.ActivationQueue);
        if (!queue.Contains(key, StringComparer.OrdinalIgnoreCase))
            queue = queue.Append(key).ToArray();

        var updated = new ModuleActivationDocument(
            ModuleActivationStateFile.CurrentSchemaVersion,
            document.Revision + 1,
            _hostVersionProvider.CurrentHostVersion,
            DateTimeOffset.UtcNow,
            modules,
            queue,
            document.Diagnostics);

        _stateStore.WriteAtomic(updated);
        return record;
    }

    private static void ValidatePendingConflict(
        ModuleActivationRecord? existing,
        string incomingVersion,
        string incomingFingerprint)
    {
        if (existing is null)
            return;

        if (existing.State != ModuleActivationState.PendingActivation ||
            string.IsNullOrWhiteSpace(existing.PendingVersion))
        {
            return;
        }

        if (string.Equals(existing.PendingVersion, incomingVersion, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(existing.PendingFingerprint, incomingFingerprint, StringComparison.OrdinalIgnoreCase))
                return;

            throw new ModuleActivationException(
                "Cùng pending version nhưng fingerprint khác.",
                ModuleActivationErrorCodes.PendingConflict);
        }

        throw new ModuleActivationException(
            $"Đang chờ kích hoạt phiên bản '{existing.PendingVersion}'; không nhận phiên bản '{incomingVersion}'.",
            ModuleActivationErrorCodes.PendingConflict);
    }

    private async Task PromoteStagingToInstalledAsync(
        string stagingFull,
        string installedDirectory,
        ModulePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(installedDirectory)
            ?? throw new ModuleActivationException("Không xác định được thư mục Installed.", ModuleActivationErrorCodes.InvalidStaging);
        Directory.CreateDirectory(parent);

        var tempDirectory = Path.Combine(
            parent,
            $".tmp-promote-{SanitizeForDirectory(manifest.ModuleVersion)}-{Guid.NewGuid():N}");

        var succeeded = false;
        try
        {
            Directory.CreateDirectory(tempDirectory);

            await CopyFileAsync(
                Path.Combine(stagingFull, ModulePackageFormat.ManifestFileName),
                Path.Combine(tempDirectory, ModulePackageFormat.ManifestFileName),
                cancellationToken).ConfigureAwait(false);
            await CopyFileAsync(
                Path.Combine(stagingFull, ModulePackageFormat.SignatureFileName),
                Path.Combine(tempDirectory, ModulePackageFormat.SignatureFileName),
                cancellationToken).ConfigureAwait(false);

            foreach (var payload in manifest.Payload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ModulePackagePathRules.ResolveUnderRoot(stagingFull, payload.Path);
                var destination = ModulePackagePathRules.ResolveUnderRoot(tempDirectory, payload.Path);
                RejectReparsePoint(source);

                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
                VerifyCopiedPayload(destination, payload);
            }

            await VerifyExactStagedContentsAsync(tempDirectory, manifest, cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(installedDirectory))
                throw new ModuleActivationException("Thư mục Installed xuất hiện trong lúc promote.", ModuleActivationErrorCodes.InvalidStaging);

            Directory.Move(tempDirectory, installedDirectory);
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
                TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (CopyFileAsyncForTests is not null)
        {
            await CopyFileAsyncForTests(source, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        RejectReparsePoint(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task VerifyExactStagedContentsAsync(
        string root,
        ModulePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(root);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ModulePackageFormat.ManifestFileName,
            ModulePackageFormat.SignatureFileName
        };
        foreach (var payload in manifest.Payload)
            allowed.Add(payload.Path.Replace('\\', '/'));

        foreach (var relative in EnumerateFilesNoFollow(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowed.Contains(relative))
                throw new ModuleActivationException(
                    $"Staging chứa tệp thừa không có trong manifest: {relative}",
                    ModuleActivationErrorCodes.InvalidStaging);
        }

        foreach (var name in new[] { ModulePackageFormat.ManifestFileName, ModulePackageFormat.SignatureFileName })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
                throw new ModuleActivationException($"Thiếu {name}.", ModuleActivationErrorCodes.InvalidStaging);
            RejectReparsePoint(path);
        }

        foreach (var payload in manifest.Payload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ModulePackagePathRules.ResolveUnderRoot(root, payload.Path);
            if (!File.Exists(path))
                throw new ModuleActivationException($"Thiếu payload '{payload.Path}'.", ModuleActivationErrorCodes.InvalidStaging);
            RejectReparsePoint(path);
            VerifyCopiedPayload(path, payload);
        }

        return Task.CompletedTask;
    }

    private static async Task VerifyExactInstalledContentsAsync(
        string installedDirectory,
        string expectedFingerprint,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(installedDirectory, ModulePackageFormat.ManifestFileName);
        var signaturePath = Path.Combine(installedDirectory, ModulePackageFormat.SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            throw new ModuleActivationException("Installed thiếu metadata đã ký.", ModuleActivationErrorCodes.TamperedInstall);

        var manifestBytes = await ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var signatureBytes = await ReadAllBytesAsync(signaturePath, cancellationToken).ConfigureAwait(false);
        var fingerprint = ComputePackageFingerprint(manifestBytes, signatureBytes);
        if (!string.Equals(fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ModuleActivationException("Fingerprint installed không khớp gói staging.", ModuleActivationErrorCodes.TamperedInstall);

        ModulePackageManifest manifest;
        try
        {
            manifest = ModuleManifestParser.Parse(manifestBytes);
        }
        catch (ModulePackageValidationException ex)
        {
            throw new ModuleActivationException(ex.Message, ModuleActivationErrorCodes.TamperedInstall);
        }

        await VerifyExactStagedContentsAsync(installedDirectory, manifest, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateFilesNoFollow(string root)
    {
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stack = new Stack<string>();
        stack.Push(rootFull);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            RejectReparsePoint(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new ModuleActivationException(
                        "Không chấp nhận symlink/reparse trong cây staging/installed.",
                        ModuleActivationErrorCodes.InvalidStaging);

                if ((attrs & FileAttributes.Directory) != 0)
                {
                    stack.Push(entry);
                    continue;
                }

                var relative = Path.GetRelativePath(rootFull, entry).Replace('\\', '/');
                yield return relative;
            }
        }
    }

    private void EnsureInstalledParentSafe(string safeModuleId, string safeVersion)
    {
        var moduleRoot = Path.Combine(_paths.InstalledRoot, safeModuleId);
        var versionRoot = Path.Combine(moduleRoot, safeVersion);
        var installedRoot = Path.GetFullPath(_paths.InstalledRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!Path.GetFullPath(moduleRoot).StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(versionRoot).StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModuleActivationException("Đường dẫn Installed không an toàn.", ModuleActivationErrorCodes.InvalidStaging);
        }
    }

    private static void VerifyCopiedPayload(string path, ModulePayloadEntry payload)
    {
        var info = new FileInfo(path);
        if (info.Length != payload.Length)
            throw new ModuleActivationException($"Kích thước payload không khớp: {payload.Path}.", ModuleActivationErrorCodes.TamperedInstall);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ModuleActivationException($"Checksum payload không khớp: {payload.Path}.", ModuleActivationErrorCodes.TamperedInstall);
    }

    internal static string ComputePackageFingerprint(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        var buffer = new byte[manifestBytes.Length + signatureBytes.Length];
        manifestBytes.CopyTo(buffer);
        signatureBytes.CopyTo(buffer.AsSpan(manifestBytes.Length));
        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string RequireDirectoryUnderRoot(string root, string candidate, string label)
    {
        if (!Directory.Exists(candidate))
            throw new ModuleActivationException($"Không tìm thấy thư mục {label}.", ModuleActivationErrorCodes.InvalidStaging);

        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidateFull = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(
                candidateFull,
                rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ModuleActivationException($"Thư mục {label} không hợp lệ.", ModuleActivationErrorCodes.InvalidStaging);
        }

        if (!candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ModuleActivationException($"Thư mục {label} nằm ngoài vùng cho phép.", ModuleActivationErrorCodes.InvalidStaging);

        return candidateFull;
    }

    private static void RejectReparseChain(string path, string stopAtRoot)
    {
        var stop = Path.GetFullPath(stopAtRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path);

        while (true)
        {
            RejectReparsePoint(current);
            if (string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
                break;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
            // Do not walk above stop root.
            var stopPrefix = stop + Path.DirectorySeparatorChar;
            if (!current.StartsWith(stopPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            throw new ModuleActivationException(
                "Không chấp nhận symlink/reparse trong activation.",
                ModuleActivationErrorCodes.InvalidStaging);
        }
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

        foreach (var id in pending.OrderBy(static x => x, StringComparer.Ordinal))
            ordered.Add(id);

        return ordered;
    }

    private static string SanitizeForDirectory(string value)
    {
        var cleaned = string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new ModuleActivationException(
                "moduleId/moduleVersion không thể dùng làm tên thư mục.",
                ModuleActivationErrorCodes.InvalidStaging);
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
            // Best-effort cleanup.
        }
    }

    private static ModuleActivationAcceptResult Fail(string errorCode, string message) =>
        new(false, message, errorCode);
}
