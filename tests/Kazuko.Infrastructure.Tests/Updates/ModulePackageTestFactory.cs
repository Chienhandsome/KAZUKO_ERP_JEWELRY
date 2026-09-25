using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Tests.Updates;

internal sealed class SigningMaterials : IDisposable
{
    public SigningMaterials()
    {
        Rsa = RSA.Create(2048);
        PublicKeySpki = Rsa.ExportSubjectPublicKeyInfo();
    }

    public RSA Rsa { get; }
    public byte[] PublicKeySpki { get; }
    public string Publisher { get; } = "Kazuko";
    public string KeyId { get; } = "test-key-001";

    public byte[] SignManifest(byte[] manifestBytes) =>
        Rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

    public void Dispose() => Rsa.Dispose();
}

internal static class ModulePackageTestFactory
{
    public static async Task<(string PackagePath, SigningMaterials Keys)> CreateValidPackageAsync(
        string workDirectory,
        Action<PackageBuildOptions>? configure = null)
    {
        var options = new PackageBuildOptions();
        configure?.Invoke(options);
        var keys = options.Keys ?? new SigningMaterials();

        Directory.CreateDirectory(workDirectory);
        var payloadDir = Path.Combine(workDirectory, "payload-src");
        Directory.CreateDirectory(payloadDir);

        var files = new List<(string ArchivePath, string LocalPath, long Length, string Sha256, int? ExternalAttributes)>();
        foreach (var file in options.PayloadFiles)
        {
            var safeLocalName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.ArchivePath)))[..16];
            var local = Path.Combine(payloadDir, safeLocalName + ".bin");
            await File.WriteAllBytesAsync(local, file.Content);
            var hash = Convert.ToHexString(SHA256.HashData(file.Content));
            files.Add((file.ArchivePath, local, file.Content.LongLength, hash, file.ExternalAttributes));
        }

        var payloadJson = new List<object>();
        foreach (var f in files)
        {
            payloadJson.Add(new
            {
                path = f.ArchivePath,
                sha256 = options.CorruptHashFor == f.ArchivePath ? new string('0', 64) : f.Sha256,
                length = options.CorruptLengthFor == f.ArchivePath ? f.Length + 1 : f.Length
            });
        }

        if (options.ExtraManifestPayload is not null)
            payloadJson.Add(options.ExtraManifestPayload);

        var manifestObject = new Dictionary<string, object?>
        {
            ["packageFormatVersion"] = options.PackageFormatVersion,
            ["moduleId"] = options.ModuleId,
            ["displayName"] = options.DisplayName,
            ["moduleVersion"] = options.ModuleVersion,
            ["minimumHostVersion"] = options.MinimumHostVersion,
            ["entryAssembly"] = options.EntryAssembly,
            ["publisher"] = options.Publisher ?? keys.Publisher,
            ["keyId"] = options.KeyId ?? keys.KeyId,
            ["signatureAlgorithm"] = options.SignatureAlgorithm,
            ["payload"] = payloadJson
        };

        foreach (var pair in options.ExtraManifestFields)
            manifestObject[pair.Key] = pair.Value;

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifestObject);
        var signatureBytes = options.InvalidSignature
            ? Encoding.UTF8.GetBytes("not-a-valid-signature")
            : keys.SignManifest(manifestBytes);

        var packagePath = Path.Combine(workDirectory, options.PackageFileName);
        if (File.Exists(packagePath))
            File.Delete(packagePath);

        using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            if (!options.OmitManifest)
            {
                var manifestEntry = zip.CreateEntry(ModulePackageFormat.ManifestFileName, CompressionLevel.Optimal);
                await using (var stream = manifestEntry.Open())
                    await stream.WriteAsync(manifestBytes);
            }

            if (!options.OmitSignature)
            {
                var signatureEntry = zip.CreateEntry(ModulePackageFormat.SignatureFileName, CompressionLevel.NoCompression);
                await using (var stream = signatureEntry.Open())
                    await stream.WriteAsync(signatureBytes);
            }

            foreach (var file in files)
            {
                var entry = zip.CreateEntryFromFile(file.LocalPath, file.ArchivePath, CompressionLevel.Optimal);
                if (file.ExternalAttributes.HasValue)
                    entry.ExternalAttributes = file.ExternalAttributes.Value;
            }

            foreach (var extra in options.ExtraZipEntries)
            {
                var entry = zip.CreateEntry(extra.ArchivePath, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(extra.Content);
                if (extra.ExternalAttributes.HasValue)
                    entry.ExternalAttributes = extra.ExternalAttributes.Value;
            }

            options.MutateArchive?.Invoke(zip);
        }

        return (packagePath, keys);
    }

    internal sealed class PackageBuildOptions
    {
        public SigningMaterials? Keys { get; set; }
        public string PackageFileName { get; set; } = "module.kazuko-module";
        public int PackageFormatVersion { get; set; } = ModulePackageFormat.CurrentVersion;
        public string ModuleId { get; set; } = "Kazuko.Warehouse";
        public string DisplayName { get; set; } = "Kho";
        public string ModuleVersion { get; set; } = "2.1.0";
        public int MinimumHostVersion { get; set; } = 1;
        public string EntryAssembly { get; set; } = "bin/Kazuko.Modules.Warehouse.dll";
        public string? Publisher { get; set; }
        public string? KeyId { get; set; }
        public string SignatureAlgorithm { get; set; } = ModulePackageFormat.AllowedSignatureAlgorithm;
        public bool OmitManifest { get; set; }
        public bool OmitSignature { get; set; }
        public bool InvalidSignature { get; set; }
        public string? CorruptHashFor { get; set; }
        public string? CorruptLengthFor { get; set; }
        public Dictionary<string, object?> ExtraManifestFields { get; } = new();
        public object? ExtraManifestPayload { get; set; }
        public Action<ZipArchive>? MutateArchive { get; set; }
        public List<PayloadFile> PayloadFiles { get; } =
        [
            new("bin/Kazuko.Modules.Warehouse.dll", Encoding.UTF8.GetBytes("fake-assembly-bytes"))
        ];
        public List<PayloadFile> ExtraZipEntries { get; } = [];
    }

    internal sealed record PayloadFile(string ArchivePath, byte[] Content, int? ExternalAttributes = null);
}
