using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates;

public sealed class ModulePackageInstaller : IModulePackageInstaller
{
    private static readonly HashSet<string> MetadataNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ModulePackageFormat.ManifestFileName,
        ModulePackageFormat.SignatureFileName
    };

    private readonly string _stagingRoot;
    private readonly IHostVersionProvider _hostVersionProvider;
    private readonly ITrustedPublicKeyStore _publicKeyStore;
    private readonly IModulePackageSignatureVerifier _signatureVerifier;
    private readonly ModulePackageResourceLimits _limits;

    public ModulePackageInstaller(string stagingRoot)
        : this(
            stagingRoot,
            new FixedHostVersionProvider(1),
            new EmptyTrustedPublicKeyStore(),
            new RsaPssSha256PackageSignatureVerifier(),
            ModulePackageResourceLimits.Default)
    {
    }

    public ModulePackageInstaller(
        string stagingRoot,
        IHostVersionProvider hostVersionProvider,
        ITrustedPublicKeyStore publicKeyStore,
        IModulePackageSignatureVerifier? signatureVerifier = null,
        ModulePackageResourceLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _stagingRoot = Path.GetFullPath(stagingRoot);
        _hostVersionProvider = hostVersionProvider ?? throw new ArgumentNullException(nameof(hostVersionProvider));
        _publicKeyStore = publicKeyStore ?? throw new ArgumentNullException(nameof(publicKeyStore));
        _signatureVerifier = signatureVerifier ?? new RsaPssSha256PackageSignatureVerifier();
        _limits = limits ?? ModulePackageResourceLimits.Default;
    }

    public async Task<ModuleInstallResult> ValidateAndStageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            return Fail("Không tìm thấy gói cập nhật.");

        if (!string.Equals(Path.GetExtension(packagePath), ".kazuko-module", StringComparison.OrdinalIgnoreCase))
            return Fail("Gói cập nhật phải có đuôi .kazuko-module.");

        string? tempStaging = null;
        string? finalStaging = null;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = archive.Entries.ToList();
            if (entries.Count == 0)
                return Fail("Gói cập nhật trống.");

            ValidateArchiveEntriesAndLimits(entries);

            var manifestEntry = FindRequiredEntry(entries, ModulePackageFormat.ManifestFileName);
            var signatureEntry = FindRequiredEntry(entries, ModulePackageFormat.SignatureFileName);

            var manifestBytes = await ReadEntryBytesAsync(manifestEntry, cancellationToken);
            var signatureBytes = await ReadEntryBytesAsync(signatureEntry, cancellationToken);

            ModulePackageManifest manifest;
            try
            {
                manifest = ModuleManifestParser.Parse(manifestBytes);
            }
            catch (ModulePackageValidationException ex)
            {
                return Fail(ex.Message);
            }

            if (!_signatureVerifier.IsAlgorithmAllowed(manifest.SignatureAlgorithm))
                return Fail("Thuật toán chữ ký không được phép.");

            if (manifest.MinimumHostVersion > _hostVersionProvider.CurrentHostVersion)
                return Fail("Gói yêu cầu phiên bản ứng dụng mới hơn.");

            var publicKey = _publicKeyStore.FindPublicKey(manifest.Publisher, manifest.KeyId);
            if (publicKey is null || publicKey.Length == 0)
                return Fail("Không tìm thấy nhà phát hành đáng tin cậy.");

            if (!_signatureVerifier.Verify(manifestBytes, signatureBytes, publicKey, manifest.SignatureAlgorithm))
                return Fail("Chữ ký số của gói không hợp lệ.");

            ValidateArchivePayloadMatching(entries, manifest);

            await VerifyPayloadIntegrityAsync(entries, manifest, cancellationToken);

            Directory.CreateDirectory(_stagingRoot);
            var safeId = SanitizeForDirectory(manifest.ModuleId);
            var safeVersion = SanitizeForDirectory(manifest.ModuleVersion);
            tempStaging = Path.Combine(
                _stagingRoot,
                $".tmp-{safeId}-{safeVersion}-{Guid.NewGuid():N}");
            finalStaging = Path.Combine(
                _stagingRoot,
                $"{safeId}-{safeVersion}-{Guid.NewGuid():N}.staged");

            Directory.CreateDirectory(tempStaging);
            await ExtractPayloadAsync(entries, manifest, tempStaging, cancellationToken);

            // Persist signed metadata for later activation tasks (not loaded here).
            await File.WriteAllBytesAsync(
                Path.Combine(tempStaging, ModulePackageFormat.ManifestFileName),
                manifestBytes,
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(tempStaging, ModulePackageFormat.SignatureFileName),
                signatureBytes,
                cancellationToken);

            if (Directory.Exists(finalStaging))
                Directory.Delete(finalStaging, recursive: true);
            Directory.Move(tempStaging, finalStaging);
            tempStaging = null;

            var totalBytes = manifest.Payload.Sum(p => p.Length);
            var summary = new ModuleValidationSummary(
                manifest.Payload.Count,
                totalBytes,
                manifest.SignatureAlgorithm,
                manifest.Publisher,
                manifest.KeyId);

            return new ModuleInstallResult(
                true,
                "Gói hợp lệ và đã được đưa vào vùng chờ.",
                finalStaging,
                manifest.ModuleId,
                manifest.ModuleVersion,
                summary);
        }
        catch (ModulePackageValidationException ex)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail(ex.Message);
        }
        catch (InvalidDataException)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail("Tệp không phải gói cập nhật hợp lệ.");
        }
        catch (JsonException)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail("module.json không đúng định dạng.");
        }
        catch (IOException)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail("Không thể chuẩn bị vùng chờ cho gói cập nhật.");
        }
        catch (UnauthorizedAccessException)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail("Không thể chuẩn bị vùng chờ cho gói cập nhật.");
        }
        catch (CryptographicException)
        {
            Cleanup(tempStaging);
            Cleanup(finalStaging);
            return Fail("Không thể xác minh chữ ký số của gói.");
        }
    }

    private void ValidateArchiveEntriesAndLimits(IReadOnlyList<ZipArchiveEntry> entries)
    {
        if (entries.Count > _limits.MaxEntryCount)
            throw new ModulePackageValidationException("Gói cập nhật vượt quá số lượng tệp cho phép.");

        var seenCanonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalUncompressed = 0;

        foreach (var entry in entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            ModulePackagePathRules.ValidateRelativePath(name, allowEmpty: false);

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                if (entry.Length != 0)
                    throw new ModulePackageValidationException("Thư mục trong gói không được chứa dữ liệu.");
                continue;
            }

            if (ModulePackagePathRules.LooksLikeSymlink(new ZipArchiveEntryAttributes(entry.ExternalAttributes)))
                throw new ModulePackageValidationException("Gói chứa mục symlink không được phép.");

            if (!seenCanonical.Add(name))
                throw new ModulePackageValidationException("Gói chứa đường dẫn trùng.");

            if (entry.Length < 0 || entry.Length > _limits.MaxEntryBytes)
                throw new ModulePackageValidationException("Một tệp trong gói vượt quá kích thước cho phép.");

            totalUncompressed += entry.Length;
            if (totalUncompressed > _limits.MaxTotalUncompressedBytes)
                throw new ModulePackageValidationException("Tổng dung lượng giải nén vượt quá giới hạn.");

            if (entry.CompressedLength > 0)
            {
                var ratio = (double)entry.Length / entry.CompressedLength;
                if (ratio > _limits.MaxCompressionRatio)
                    throw new ModulePackageValidationException("Tỷ lệ nén của gói vượt quá giới hạn an toàn.");
            }
            else if (entry.Length > 0)
            {
                throw new ModulePackageValidationException("Tỷ lệ nén của gói vượt quá giới hạn an toàn.");
            }
        }

        var manifestEntry = FindRequiredEntry(entries, ModulePackageFormat.ManifestFileName);
        if (manifestEntry.Length > Math.Min(_limits.MaxEntryBytes, 1_048_576))
            throw new ModulePackageValidationException("Kích thước tệp module.json vượt quá giới hạn cho phép.");

        var signatureEntry = FindRequiredEntry(entries, ModulePackageFormat.SignatureFileName);
        if (signatureEntry.Length > Math.Min(_limits.MaxEntryBytes, 65_536))
            throw new ModulePackageValidationException("Kích thước tệp signature.sig vượt quá giới hạn cho phép.");
    }

    private static void ValidateArchivePayloadMatching(IReadOnlyList<ZipArchiveEntry> entries, ModulePackageManifest manifest)
    {
        var payloadMap = manifest.Payload.ToDictionary(
            p => p.Path,
            p => p,
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            if (MetadataNames.Contains(Path.GetFileName(name)) &&
                string.Equals(Path.GetFileName(name), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!payloadMap.ContainsKey(name))
                throw new ModulePackageValidationException("Gói chứa tệp không được khai báo trong manifest.");
        }

        foreach (var payload in manifest.Payload)
        {
            var match = entries.FirstOrDefault(e =>
                string.Equals(NormalizeEntryName(e.FullName), payload.Path, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ModulePackageValidationException("Gói thiếu tệp payload đã khai báo.");
        }
    }

    private async Task VerifyPayloadIntegrityAsync(
        IReadOnlyList<ZipArchiveEntry> entries,
        ModulePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var payload in manifest.Payload)
        {
            var entry = entries.First(e =>
                string.Equals(NormalizeEntryName(e.FullName), payload.Path, StringComparison.OrdinalIgnoreCase));

            if (entry.Length != payload.Length)
                throw new ModulePackageValidationException("Kích thước payload không khớp manifest.");

            await using var stream = entry.Open();
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;
                total += read;
                if (total > payload.Length || total > _limits.MaxEntryBytes)
                    throw new ModulePackageValidationException("Kích thước payload không khớp manifest.");
                hasher.AppendData(buffer, 0, read);
            }

            if (total != payload.Length)
                throw new ModulePackageValidationException("Kích thước payload không khớp manifest.");

            var hash = hasher.GetHashAndReset();
            if (!CryptographicEquals.HexSha256Equals(payload.Sha256, hash))
                throw new ModulePackageValidationException("Checksum của payload không khớp.");
        }
    }

    private async Task ExtractPayloadAsync(
        IReadOnlyList<ZipArchiveEntry> entries,
        ModulePackageManifest manifest,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var payload in manifest.Payload)
        {
            var entry = entries.First(e =>
                string.Equals(NormalizeEntryName(e.FullName), payload.Path, StringComparison.OrdinalIgnoreCase));
            var destination = ModulePackagePathRules.ResolveUnderRoot(stagingDirectory, payload.Path);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using var source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static ZipArchiveEntry FindRequiredEntry(IReadOnlyList<ZipArchiveEntry> entries, string fileName)
    {
        var entry = entries.FirstOrDefault(e =>
            string.Equals(NormalizeEntryName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new ModulePackageValidationException($"Gói cập nhật thiếu {fileName}.");
        return entry;
    }

    private static string NormalizeEntryName(string fullName) =>
        fullName.Replace('\\', '/').TrimEnd('/');

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static string SanitizeForDirectory(string value)
    {
        var cleaned = string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));
        return string.IsNullOrWhiteSpace(cleaned) ? "module" : cleaned;
    }

    private static void Cleanup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup after failed staging.
        }
    }

    private static ModuleInstallResult Fail(string message) =>
        new(false, message);
}
