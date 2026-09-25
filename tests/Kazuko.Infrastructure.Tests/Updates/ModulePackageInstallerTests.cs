using System.IO.Compression;
using System.Text;
using Kazuko.Core.Updates;
using Kazuko.Infrastructure.Updates;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Updates;

public sealed class ModulePackageInstallerTests
{
    [Fact]
    public async Task Valid_package_stages_successfully()
    {
        using var harness = await PackageHarness.CreateAsync();
        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.StagingDirectory));
        Assert.True(Directory.Exists(result.StagingDirectory));
        Assert.Equal("Kazuko.Warehouse", result.ModuleId);
        Assert.Equal("2.1.0", result.ModuleVersion);
        Assert.True(File.Exists(Path.Combine(result.StagingDirectory!, "bin", "Kazuko.Modules.Warehouse.dll")));
        Assert.True(File.Exists(Path.Combine(result.StagingDirectory!, "module.json")));
        Assert.True(File.Exists(Path.Combine(result.StagingDirectory!, "signature.sig")));
    }

    [Fact]
    public async Task Rejects_parent_directory_traversal()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "../evil.dll",
                Encoding.UTF8.GetBytes("evil")));
            options.EntryAssembly = "../evil.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Null(result.StagingDirectory);
    }

    [Fact]
    public async Task Rejects_absolute_path_payload()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "C:/Windows/evil.dll",
                Encoding.UTF8.GetBytes("evil")));
            options.EntryAssembly = "C:/Windows/evil.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_duplicate_paths_differing_only_by_case()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/Module.dll",
                Encoding.UTF8.GetBytes("one")));
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/module.dll",
                Encoding.UTF8.GetBytes("two")));
            options.EntryAssembly = "bin/Module.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("trùng", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_windows_reserved_filename()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/CON.dll",
                Encoding.UTF8.GetBytes("device")));
            options.EntryAssembly = "bin/CON.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_alternate_data_stream_path()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/module.dll:zone.identifier",
                Encoding.UTF8.GetBytes("ads")));
            options.EntryAssembly = "bin/module.dll:zone.identifier";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_extra_zip_payload_not_in_manifest()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.ExtraZipEntries.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/extra.dll",
                Encoding.UTF8.GetBytes("extra")));
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("không được khai báo", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_missing_declared_payload()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.ExtraManifestPayload = new
            {
                path = "bin/missing.dll",
                sha256 = new string('A', 64),
                length = 4L
            };
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("thiếu", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_incorrect_hash()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.CorruptHashFor = options.EntryAssembly;
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("Checksum", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_incorrect_length()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.CorruptLengthFor = options.EntryAssembly;
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("Kích thước", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_invalid_signature()
    {
        using var harness = await PackageHarness.CreateAsync(options => options.InvalidSignature = true);
        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("Chữ ký", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_unknown_publisher_or_key()
    {
        using var keys = new SigningMaterials();
        var work = CreateTempDir();
        var (packagePath, _) = await ModulePackageTestFactory.CreateValidPackageAsync(work, o => o.Keys = keys);

        var installer = new ModulePackageInstaller(
            Path.Combine(work, "staging"),
            new FixedHostVersionProvider(1),
            new EmptyTrustedPublicKeyStore());

        var result = await installer.ValidateAndStageAsync(packagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("đáng tin cậy", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.StagingDirectory);
    }

    [Fact]
    public async Task Rejects_disallowed_signature_algorithm()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.SignatureAlgorithm = "RSA-SHA1";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("Thuật toán", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_incompatible_host_version()
    {
        using var harness = await PackageHarness.CreateAsync(
            options => options.MinimumHostVersion = 99,
            hostVersion: 1);

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("phiên bản ứng dụng", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_too_many_entries()
    {
        using var harness = await PackageHarness.CreateAsync(
            options => { },
            hostVersion: 1,
            limits: new ModulePackageResourceLimits { MaxEntryCount = 1 });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("số lượng", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_total_uncompressed_over_limit()
    {
        using var harness = await PackageHarness.CreateAsync(
            options =>
            {
                options.PayloadFiles.Clear();
                options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                    "bin/Kazuko.Modules.Warehouse.dll",
                    new byte[2048]));
                options.EntryAssembly = "bin/Kazuko.Modules.Warehouse.dll";
            },
            limits: new ModulePackageResourceLimits { MaxTotalUncompressedBytes = 100 });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("dung lượng", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_excessive_compression_ratio()
    {
        using var harness = await PackageHarness.CreateAsync(
            options =>
            {
                options.PayloadFiles.Clear();
                // Highly compressible payload to inflate ratio.
                options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                    "bin/Kazuko.Modules.Warehouse.dll",
                    new byte[200_000]));
                options.EntryAssembly = "bin/Kazuko.Modules.Warehouse.dll";
            },
            limits: new ModulePackageResourceLimits { MaxCompressionRatio = 2.0 });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("Tỷ lệ nén", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_validation_does_not_leave_successful_staging()
    {
        using var harness = await PackageHarness.CreateAsync(options => options.InvalidSignature = true);
        var stagingRoot = harness.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        var before = Directory.GetDirectories(stagingRoot, "*.staged", SearchOption.TopDirectoryOnly);

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);

        var after = Directory.GetDirectories(stagingRoot, "*.staged", SearchOption.TopDirectoryOnly);
        Assert.Equal(before.Length, after.Length);
        Assert.Empty(Directory.GetDirectories(stagingRoot, ".tmp-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Rejects_unknown_manifest_field()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.ExtraManifestFields["unexpected"] = "value";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("không được phép", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_legacy_format_without_payload_list()
    {
        var work = CreateTempDir();
        var packagePath = Path.Combine(work, "legacy.kazuko-module");
        using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var manifest = zip.CreateEntry("module.json");
            await using (var stream = manifest.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(
                    """{"id":"Kazuko.Warehouse","name":"Kho","version":"1.0.0","entryAssembly":"bin/a.dll","sha256":"ABC","minimumHostVersion":1}""");
                await stream.WriteAsync(bytes);
            }

            var sig = zip.CreateEntry("signature.sig");
            await using (var stream = sig.Open())
                await stream.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        var installer = new ModulePackageInstaller(
            Path.Combine(work, "staging"),
            new FixedHostVersionProvider(1),
            new EmptyTrustedPublicKeyStore());
        var result = await installer.ValidateAndStageAsync(packagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_symlink_entry()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/Kazuko.Modules.Warehouse.dll",
                Encoding.UTF8.GetBytes("symlink-target"),
                unchecked((int)0xA1FF0000)));
            options.EntryAssembly = "bin/Kazuko.Modules.Warehouse.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("symlink", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_windows_reparse_point_entry()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/Kazuko.Modules.Warehouse.dll",
                Encoding.UTF8.GetBytes("reparse-point"),
                0x0400));
            options.EntryAssembly = "bin/Kazuko.Modules.Warehouse.dll";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("symlink", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_path_with_trailing_dot()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/module.dll.",
                Encoding.UTF8.GetBytes("content")));
            options.EntryAssembly = "bin/module.dll.";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_path_with_trailing_space()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.PayloadFiles.Clear();
            options.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/module.dll ",
                Encoding.UTF8.GetBytes("content")));
            options.EntryAssembly = "bin/module.dll ";
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_directory_entry_with_traversal()
    {
        using var harness = await PackageHarness.CreateAsync(options =>
        {
            options.ExtraZipEntries.Add(new ModulePackageTestFactory.PayloadFile(
                "../sub/",
                []));
        });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_manifest_exceeding_entry_limit()
    {
        using var harness = await PackageHarness.CreateAsync(
            options => { },
            limits: new ModulePackageResourceLimits { MaxEntryBytes = 50 });

        var result = await harness.Installer.ValidateAndStageAsync(harness.PackagePath);
        Assert.False(result.IsSuccess);
        Assert.Contains("kích thước", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "kazuko-pkg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class PackageHarness : IDisposable
    {
        private PackageHarness(
            string workDirectory,
            string packagePath,
            string stagingRoot,
            ModulePackageInstaller installer,
            SigningMaterials keys)
        {
            WorkDirectory = workDirectory;
            PackagePath = packagePath;
            StagingRoot = stagingRoot;
            Installer = installer;
            Keys = keys;
        }

        public string WorkDirectory { get; }
        public string PackagePath { get; }
        public string StagingRoot { get; }
        public ModulePackageInstaller Installer { get; }
        public SigningMaterials Keys { get; }

        public static async Task<PackageHarness> CreateAsync(
            Action<ModulePackageTestFactory.PackageBuildOptions>? configure = null,
            int hostVersion = 1,
            ModulePackageResourceLimits? limits = null)
        {
            var work = CreateTempDir();
            var (packagePath, keys) = await ModulePackageTestFactory.CreateValidPackageAsync(work, configure);
            var store = new InMemoryTrustedPublicKeyStore();
            store.Add(keys.Publisher, keys.KeyId, keys.PublicKeySpki);
            var stagingRoot = Path.Combine(work, "staging");
            var installer = new ModulePackageInstaller(
                stagingRoot,
                new FixedHostVersionProvider(hostVersion),
                store,
                new RsaPssSha256PackageSignatureVerifier(),
                limits);
            return new PackageHarness(work, packagePath, stagingRoot, installer, keys);
        }

        public void Dispose()
        {
            Keys.Dispose();
            try
            {
                if (Directory.Exists(WorkDirectory))
                    Directory.Delete(WorkDirectory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
