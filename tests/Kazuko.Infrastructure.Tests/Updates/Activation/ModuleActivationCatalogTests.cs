using System.Text;
using Kazuko.Core.Updates;
using Kazuko.Infrastructure.Updates;
using Kazuko.Infrastructure.Updates.Activation;
using Kazuko.Infrastructure.Tests.Updates;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Updates.Activation;

public sealed class ModuleActivationCatalogTests
{
    [Fact]
    public async Task AcceptStaged_promotes_to_Installed_and_PendingActivation()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);

        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Record);
        Assert.Equal(ModuleActivationState.PendingActivation, result.Record!.State);
        Assert.Equal("2.1.0", result.Record.PendingVersion);
        Assert.Null(result.Record.ActiveVersion);
        Assert.True(Directory.Exists(result.InstalledDirectory));
        Assert.True(File.Exists(Path.Combine(result.InstalledDirectory!, ModulePackageFormat.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(result.InstalledDirectory!, "bin", "Kazuko.Modules.Warehouse.dll")));

        var document = await catalog.LoadDocumentAsync();
        Assert.Contains("Kazuko.Warehouse", document.ActivationQueue);
        Assert.True(File.Exists(scope.Layout.ActivationStatePath));
        Assert.True(File.Exists(scope.Layout.ActivationStateBackupPath));
    }

    [Fact]
    public async Task AcceptStaged_is_idempotent_for_same_package_hash()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);

        var first = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        var second = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(first.Record!.PackageSha256, second.Record!.PackageSha256);
        Assert.Equal(ModuleActivationState.PendingActivation, second.Record.State);
    }

    [Fact]
    public async Task AcceptStaged_rejects_same_version_with_different_content()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        var first = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(first.IsSuccess, first.Message);

        using var conflicting = await CreateStagedPackageAsync(
            scope.Layout,
            configure: o =>
            {
                o.PayloadFiles.Clear();
                o.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                    "bin/Kazuko.Modules.Warehouse.dll",
                    Encoding.UTF8.GetBytes("other-assembly-bytes")));
            },
            disposeLayout: false);

        var rejected = await catalog.AcceptStagedForActivationAsync(conflicting.StagingDirectory);
        Assert.False(rejected.IsSuccess);
        Assert.Contains("khác", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptStaged_rejects_staging_outside_StagingRoot()
    {
        using var scope = await CreateStagedPackageAsync();
        var outside = Path.Combine(Path.GetTempPath(), "kazuko-activation-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            CopyDirectory(scope.StagingDirectory, outside);

            var catalog = CreateCatalog(scope.Layout);
            var result = await catalog.AcceptStagedForActivationAsync(outside);
            Assert.False(result.IsSuccess);
            Assert.Contains("ngoài", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(outside);
        }
    }

    [Fact]
    public async Task AcceptStaged_rejects_tampered_payload_hash()
    {
        using var scope = await CreateStagedPackageAsync();
        var dll = Directory.GetFiles(scope.StagingDirectory, "*.dll", SearchOption.AllDirectories).Single();
        var original = await File.ReadAllBytesAsync(dll);
        var tampered = Encoding.UTF8.GetBytes("tampered-payload-bytes!!!!!!");
        if (tampered.Length != original.Length)
            tampered = original.Select((_, i) => (byte)(original[i] ^ 0x5A)).ToArray();
        await File.WriteAllBytesAsync(dll, tampered);

        var catalog = CreateCatalog(scope.Layout);
        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
        Assert.Contains("Checksum", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptStaged_rejects_missing_manifest()
    {
        using var scope = await CreateStagedPackageAsync();
        File.Delete(Path.Combine(scope.StagingDirectory, ModulePackageFormat.ManifestFileName));

        var catalog = CreateCatalog(scope.Layout);
        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task StateStore_recovers_from_backup_when_primary_corrupt()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        var accepted = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(accepted.IsSuccess, accepted.Message);

        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, "{ not-json");

        var reloaded = CreateCatalog(scope.Layout);
        var record = await reloaded.GetAsync("Kazuko.Warehouse");
        Assert.NotNull(record);
        Assert.Equal(ModuleActivationState.PendingActivation, record!.State);
        Assert.Equal("2.1.0", record.PendingVersion);
    }

    [Fact]
    public async Task StateStore_ignores_leftover_temp_and_keeps_primary()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

        var tempPath = Path.Combine(scope.Layout.ModulesRoot, ModuleActivationStateFile.TempFileName);
        await File.WriteAllTextAsync(tempPath, "{ \"schemaVersion\": 1, \"modules\": {} }");

        var reloaded = await CreateCatalog(scope.Layout).GetAsync("Kazuko.Warehouse");
        Assert.NotNull(reloaded);
        Assert.Equal(ModuleActivationState.PendingActivation, reloaded!.State);
        Assert.True(File.Exists(tempPath));
    }

    [Fact]
    public void ActivationLock_second_acquire_fails_while_held()
    {
        var root = CreateTempRoot();
        try
        {
            var layout = new ModuleLayoutPaths(Path.Combine(root, "Modules"), Path.Combine(root, "Updates"));
            layout.EnsureCreated();
            var factory = new ModuleActivationLockFactory(layout);

            Assert.True(factory.TryAcquire(out var first));
            using (first)
            {
                Assert.False(factory.TryAcquire(out var second));
                Assert.Null(second);
            }

            Assert.True(factory.TryAcquire(out var third));
            third!.Dispose();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AcceptStaged_fails_when_lock_held_by_other_process_handle()
    {
        using var scope = await CreateStagedPackageAsync();
        var factory = new ModuleActivationLockFactory(scope.Layout);
        Assert.True(factory.TryAcquire(out var held));
        using (held)
        {
            var catalog = new ModuleActivationCatalog(scope.Layout, factory, new FixedHostVersionProvider(1));
            var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
            Assert.False(result.IsSuccess);
            Assert.Contains("đang cập nhật", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AcceptStaged_preserves_existing_ActiveVersion_when_queuing_pending()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);

        await catalog.SaveAsync(new ModuleActivationRecord(
            "Kazuko.Warehouse",
            ModuleActivationState.Active,
            ActiveVersion: "1.0.0",
            PreviousVersion: null,
            PendingVersion: null,
            Source: "Installed",
            Publisher: "Kazuko",
            KeyId: "test-key-001",
            PackageSha256: "AA",
            LastError: null,
            BackupId: null,
            AttemptId: null,
            BuiltInFallbackAllowed: true));

        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("1.0.0", result.Record!.ActiveVersion);
        Assert.Equal("2.1.0", result.Record.PendingVersion);
        Assert.Equal(ModuleActivationState.PendingActivation, result.Record.State);
    }

    [Fact]
    public async Task Failed_promote_does_not_leave_tmp_install_directory()
    {
        using var scope = await CreateStagedPackageAsync();
        File.Delete(Path.Combine(scope.StagingDirectory, ModulePackageFormat.SignatureFileName));

        var catalog = CreateCatalog(scope.Layout);
        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);

        var installedRoot = Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse");
        if (Directory.Exists(installedRoot))
        {
            var leftovers = Directory.GetDirectories(installedRoot, ".tmp-promote-*");
            Assert.Empty(leftovers);
        }
    }

    private static ModuleActivationCatalog CreateCatalog(IModuleLayoutPaths layout) =>
        new(layout, new ModuleActivationLockFactory(layout), new FixedHostVersionProvider(1));

    private static async Task<TestScope> CreateStagedPackageAsync(
        ModuleLayoutPaths? existingLayout = null,
        Action<ModulePackageTestFactory.PackageBuildOptions>? configure = null,
        bool disposeLayout = true)
    {
        string root;
        ModuleLayoutPaths layout;
        if (existingLayout is null)
        {
            root = CreateTempRoot();
            layout = new ModuleLayoutPaths(
                Path.Combine(root, "Modules"),
                Path.Combine(root, "Updates"),
                Path.Combine(root, "DatabaseBackups"));
        }
        else
        {
            layout = existingLayout;
            root = layout.ModulesRoot;
        }

        layout.EnsureCreated();

        var work = Path.Combine(Path.GetTempPath(), "kazuko-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var (packagePath, keys) = await ModulePackageTestFactory.CreateValidPackageAsync(work, configure);
            using (keys)
            {
                var installer = new ModulePackageInstaller(
                    layout.StagingRoot,
                    new FixedHostVersionProvider(1),
                    CreateKeyStore(keys));
                var staged = await installer.ValidateAndStageAsync(packagePath);
                Assert.True(staged.IsSuccess, staged.Message);
                Assert.False(string.IsNullOrWhiteSpace(staged.StagingDirectory));
                return new TestScope(root, layout, staged.StagingDirectory!, disposeLayout && existingLayout is null);
            }
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static InMemoryTrustedPublicKeyStore CreateKeyStore(SigningMaterials keys)
    {
        var store = new InMemoryTrustedPublicKeyStore();
        store.Add(keys.Publisher, keys.KeyId, keys.PublicKeySpki);
        return store;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "kazuko-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class TestScope(
        string root,
        ModuleLayoutPaths layout,
        string stagingDirectory,
        bool disposeRoot) : IDisposable
    {
        public ModuleLayoutPaths Layout { get; } = layout;
        public string StagingDirectory { get; } = stagingDirectory;

        public void Dispose()
        {
            if (disposeRoot)
                TryDelete(root);
        }
    }
}
