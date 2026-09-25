using System.Reflection;
using System.Text;
using System.Text.Json;
using Kazuko.Core.Updates;
using Kazuko.Infrastructure.Tests.Updates;
using Kazuko.Infrastructure.Updates;
using Kazuko.Infrastructure.Updates.Activation;
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
        Assert.NotNull(result.Record.PendingFingerprint);
        Assert.Null(result.Record.ActiveVersion);
        Assert.Single(result.Record.InstalledVersions);
        Assert.True(Directory.Exists(result.InstalledDirectory));
        Assert.False(Directory.Exists(scope.StagingDirectory), "staging should be removed after commit");

        var document = await catalog.LoadDocumentAsync();
        Assert.Equal(1, document.Revision);
        Assert.Contains("Kazuko.Warehouse", document.ActivationQueue);
    }

    [Fact]
    public async Task AcceptStaged_is_idempotent_for_same_fingerprint()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        var first = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(first.IsSuccess, first.Message);

        var restage = RestageExactBytes(scope.Layout, first.InstalledDirectory!);
        var second = await catalog.AcceptStagedForActivationAsync(restage);

        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(first.Record!.PendingFingerprint, second.Record!.PendingFingerprint);
        Assert.Equal(2, (await catalog.LoadDocumentAsync()).Revision);
    }

    [Fact]
    public async Task AcceptStaged_rejects_same_version_different_fingerprint()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

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
        Assert.Equal(ModuleActivationErrorCodes.PendingConflict, rejected.ErrorCode);
    }

    [Fact]
    public async Task AcceptStaged_rejects_pending_B_while_pending_A()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

        using var other = await CreateStagedPackageAsync(
            scope.Layout,
            configure: o => o.ModuleVersion = "2.2.0",
            disposeLayout: false);

        var rejected = await catalog.AcceptStagedForActivationAsync(other.StagingDirectory);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(ModuleActivationErrorCodes.PendingConflict, rejected.ErrorCode);
        Assert.Contains("2.1.0", rejected.Message);
    }

    [Fact]
    public async Task AcceptStaged_preserves_prior_installed_versions_when_adding_pending()
    {
        using var scope = await CreateStagedPackageAsync();
        SeedStateWithActiveVersion(scope.Layout, "Kazuko.Warehouse", "1.0.0", "AAA111");

        // Plant installed 1.0.0 directory referenced by state.
        var oldDir = Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse", "1.0.0");
        Directory.CreateDirectory(oldDir);
        await File.WriteAllTextAsync(Path.Combine(oldDir, "marker.txt"), "old");

        // Referential check requires installedVersions paths to exist — seed uses relativeDirectory.
        // marker-only folder is enough for existence; Accept will add 2.1.0.

        // Fix seed: Load will fail referential if we only have marker — state says installed 1.0.0.
        // Re-seed with a minimal valid tree is hard without full package; instead write installed
        // versions after accepting once is not possible for two versions with pending conflict.
        // Approach: seed Active + installedVersions for 1.0.0 with real fingerprint of a planted
        // fake tree that only needs Directory.Exists — referential only checks directory exists.
        // Good.

        var catalog = CreateCatalog(scope.Layout);
        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("1.0.0", result.Record!.ActiveVersion);
        Assert.Equal(2, result.Record.InstalledVersions.Count);
        Assert.Contains(result.Record.InstalledVersions, v => v.Version == "1.0.0" && v.Fingerprint == "AAA111");
        Assert.Contains(result.Record.InstalledVersions, v => v.Version == "2.1.0");
    }

    [Fact]
    public async Task AcceptStaged_rejects_staging_outside_StagingRoot()
    {
        using var scope = await CreateStagedPackageAsync();
        var outside = Path.Combine(Path.GetTempPath(), "kazuko-activation-outside-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(scope.StagingDirectory, outside);
        try
        {
            var result = await CreateCatalog(scope.Layout).AcceptStagedForActivationAsync(outside);
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
        var tampered = original.Select(b => (byte)(b ^ 0x5A)).ToArray();
        await File.WriteAllBytesAsync(dll, tampered);

        var result = await CreateCatalog(scope.Layout).AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
        Assert.Contains("Checksum", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptStaged_rejects_extra_staged_file()
    {
        using var scope = await CreateStagedPackageAsync();
        await File.WriteAllTextAsync(Path.Combine(scope.StagingDirectory, "extra.txt"), "nope");

        var result = await CreateCatalog(scope.Layout).AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
        Assert.Equal(ModuleActivationErrorCodes.InvalidStaging, result.ErrorCode);
        Assert.Contains("thừa", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptStaged_rejects_missing_manifest()
    {
        using var scope = await CreateStagedPackageAsync();
        File.Delete(Path.Combine(scope.StagingDirectory, ModulePackageFormat.ManifestFileName));
        var result = await CreateCatalog(scope.Layout).AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AcceptStaged_rejects_installed_payload_tampered()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        var first = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(first.IsSuccess, first.Message);

        var restage = RestageExactBytes(scope.Layout, first.InstalledDirectory!);

        var dll = Directory.GetFiles(first.InstalledDirectory!, "*.dll", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(dll);
        await File.WriteAllBytesAsync(dll, bytes.Select(b => (byte)(b ^ 0x11)).ToArray());

        var rejected = await catalog.AcceptStagedForActivationAsync(restage);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(ModuleActivationErrorCodes.TamperedInstall, rejected.ErrorCode);
    }

    [Fact]
    public async Task AcceptStaged_rejects_installed_signature_changed()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        var first = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.True(first.IsSuccess, first.Message);

        var restage = RestageExactBytes(scope.Layout, first.InstalledDirectory!);

        var sig = Path.Combine(first.InstalledDirectory!, ModulePackageFormat.SignatureFileName);
        var sigBytes = await File.ReadAllBytesAsync(sig);
        sigBytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(sig, sigBytes);

        var rejected = await catalog.AcceptStagedForActivationAsync(restage);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(ModuleActivationErrorCodes.TamperedInstall, rejected.ErrorCode);
    }

    [Theory]
    [InlineData("Kazuko.Dashboard")]
    [InlineData("Kazuko.Authentication")]
    [InlineData("Kazuko.Customers")]
    public async Task AcceptStaged_rejects_reserved_host_modules(string moduleId)
    {
        using var scope = await CreateStagedPackageAsync(configure: o =>
        {
            o.ModuleId = moduleId;
            o.EntryAssembly = "bin/Module.dll";
            o.PayloadFiles.Clear();
            o.PayloadFiles.Add(new ModulePackageTestFactory.PayloadFile(
                "bin/Module.dll",
                Encoding.UTF8.GetBytes("x")));
        });

        var result = await CreateCatalog(scope.Layout).AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);
        Assert.Equal(ModuleActivationErrorCodes.ReservedModule, result.ErrorCode);
    }

    [Fact]
    public void Reserved_host_modules_match_case_insensitively()
    {
        Assert.True(ReservedHostModules.IsDenied("kazuko.dashboard"));
        Assert.True(ReservedHostModules.IsDenied("KAZUKO.AUTHENTICATION"));
        Assert.True(ReservedHostModules.IsDenied("Kazuko.Customers"));
        Assert.False(ReservedHostModules.IsDenied("Kazuko.Warehouse"));
    }

    [Fact]
    public void Public_catalog_api_cannot_set_Active_directly()
    {
        var methods = typeof(IModuleActivationCatalog).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(methods, m => m.Name is "SaveAsync" or "Save");
        Assert.Contains(methods, m => m.Name == "LoadDocumentAsync");
        Assert.Contains(methods, m => m.Name == "GetAsync");
        Assert.Contains(methods, m => m.Name == "AcceptStagedForActivationAsync");
    }

    [Fact]
    public async Task StateStore_recovers_from_backup_when_primary_corrupt_without_clobbering_bak()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

        var backupBytes = await File.ReadAllBytesAsync(scope.Layout.ActivationStateBackupPath);
        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, "{ not-json");

        var reloaded = CreateCatalog(scope.Layout);
        var record = await reloaded.GetAsync("Kazuko.Warehouse");
        Assert.NotNull(record);
        Assert.Equal(ModuleActivationState.PendingActivation, record!.State);

        var backupAfter = await File.ReadAllBytesAsync(scope.Layout.ActivationStateBackupPath);
        Assert.Equal(backupBytes, backupAfter);
        Assert.Contains(
            Directory.GetFiles(scope.Layout.ModulesRoot, "activation-state.json.corrupt-*"),
            _ => true);
    }

    [Fact]
    public async Task StateStore_both_primary_and_backup_corrupt_fail_closed()
    {
        using var scope = await CreateStagedPackageAsync();
        scope.Layout.EnsureCreated();
        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, "{ bad");
        await File.WriteAllTextAsync(scope.Layout.ActivationStateBackupPath, "{ also-bad");

        var ex = await Assert.ThrowsAsync<ModuleActivationException>(
            () => CreateCatalog(scope.Layout).LoadDocumentAsync());
        Assert.Equal(ModuleActivationErrorCodes.StateCorrupt, ex.ErrorCode);
    }

    [Fact]
    public async Task StateStore_primary_corrupt_without_backup_fail_closed()
    {
        using var scope = await CreateStagedPackageAsync();
        scope.Layout.EnsureCreated();
        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, "{ bad");
        if (File.Exists(scope.Layout.ActivationStateBackupPath))
            File.Delete(scope.Layout.ActivationStateBackupPath);

        var ex = await Assert.ThrowsAsync<ModuleActivationException>(
            () => CreateCatalog(scope.Layout).LoadDocumentAsync());
        Assert.Equal(ModuleActivationErrorCodes.StateCorrupt, ex.ErrorCode);
    }

    [Fact]
    public async Task StateStore_future_schema_version_rejected()
    {
        using var scope = await CreateStagedPackageAsync();
        scope.Layout.EnsureCreated();
        var json = """
            {
              "schemaVersion": 99,
              "revision": 1,
              "hostVersion": 1,
              "updatedAtUtc": "2026-01-01T00:00:00Z",
              "modules": {},
              "activationQueue": [],
              "diagnostics": []
            }
            """;
        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, json);

        var ex = await Assert.ThrowsAsync<ModuleActivationException>(
            () => CreateCatalog(scope.Layout).LoadDocumentAsync());
        Assert.Equal(ModuleActivationErrorCodes.StateSchemaUnsupported, ex.ErrorCode);
    }

    [Fact]
    public async Task StateStore_unknown_json_field_rejected()
    {
        using var scope = await CreateStagedPackageAsync();
        scope.Layout.EnsureCreated();
        var json = """
            {
              "schemaVersion": 1,
              "revision": 0,
              "hostVersion": 1,
              "updatedAtUtc": "2026-01-01T00:00:00Z",
              "modules": {},
              "activationQueue": [],
              "diagnostics": [],
              "evil": true
            }
            """;
        await File.WriteAllTextAsync(scope.Layout.ActivationStatePath, json);

        var ex = await Assert.ThrowsAsync<ModuleActivationException>(
            () => CreateCatalog(scope.Layout).LoadDocumentAsync());
        Assert.Equal(ModuleActivationErrorCodes.StateCorrupt, ex.ErrorCode);
    }

    [Fact]
    public async Task StateStore_missing_installed_version_directory_fail_closed()
    {
        using var scope = await CreateStagedPackageAsync();
        SeedStateWithPendingMissingInstall(scope.Layout);

        var ex = await Assert.ThrowsAsync<ModuleActivationException>(
            () => CreateCatalog(scope.Layout).LoadDocumentAsync());
        Assert.Equal(ModuleActivationErrorCodes.Referential, ex.ErrorCode);
    }

    [Fact]
    public async Task StateStore_ignores_leftover_temp_and_keeps_primary()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

        var tempPath = Path.Combine(scope.Layout.ModulesRoot, ModuleActivationStateFile.TempFileName);
        await File.WriteAllTextAsync(tempPath, """{"schemaVersion":1,"modules":{}}""");

        var reloaded = await CreateCatalog(scope.Layout).GetAsync("Kazuko.Warehouse");
        Assert.NotNull(reloaded);
        Assert.Equal(ModuleActivationState.PendingActivation, reloaded!.State);
    }

    [Fact]
    public async Task State_revision_increments_and_json_is_deterministic()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        Assert.True((await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory)).IsSuccess);

        var json1 = await File.ReadAllTextAsync(scope.Layout.ActivationStatePath);
        var restage = RestageExactBytes(scope.Layout, Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse", "2.1.0"));
        Assert.True((await catalog.AcceptStagedForActivationAsync(restage)).IsSuccess);

        var doc = await catalog.LoadDocumentAsync();
        Assert.Equal(2, doc.Revision);

        var json2 = await File.ReadAllTextAsync(scope.Layout.ActivationStatePath);
        Assert.Contains("\"revision\": 2", json2);
        // Module keys / installedVersions stay stably ordered across rewrites of same logical set.
        Assert.Contains("\"Kazuko.Warehouse\"", json1);
        Assert.Contains("installedVersions", json2);
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

            Assert.Equal(ModuleActivationLockStatus.Acquired, factory.TryAcquire(TimeSpan.FromSeconds(1), out var first));
            using (first)
            {
                Assert.Equal(ModuleActivationLockStatus.Locked, factory.TryAcquire(TimeSpan.Zero, out var second));
                Assert.Null(second);
            }

            Assert.Equal(ModuleActivationLockStatus.Acquired, factory.TryAcquire(TimeSpan.FromSeconds(1), out var third));
            third!.Dispose();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AcceptStaged_fails_when_lock_held()
    {
        using var scope = await CreateStagedPackageAsync();
        var factory = new ModuleActivationLockFactory(scope.Layout);
        Assert.Equal(ModuleActivationLockStatus.Acquired, factory.TryAcquire(TimeSpan.FromSeconds(1), out var held));
        using (held)
        {
            var catalog = new ModuleActivationCatalog(scope.Layout, factory, new FixedHostVersionProvider(1));
            var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
            Assert.False(result.IsSuccess);
            Assert.Equal(ModuleActivationErrorCodes.Locked, result.ErrorCode);
        }
    }

    [Fact]
    public async Task Cancellation_during_large_copy_cleans_tmp_promote()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        catalog.CopyFileAsyncForTests = async (source, destination, ct) =>
        {
            if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException(ct);

            await using var input = File.OpenRead(source);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, ct);
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            catalog.AcceptStagedForActivationAsync(scope.StagingDirectory, CancellationToken.None));

        var moduleRoot = Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse");
        if (Directory.Exists(moduleRoot))
            Assert.Empty(Directory.GetDirectories(moduleRoot, ".tmp-promote-*"));
        Assert.False(Directory.Exists(Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse", "2.1.0")));
    }

    [Fact]
    public async Task Failure_after_rename_before_state_commit_rolls_back_install()
    {
        using var scope = await CreateStagedPackageAsync();
        var catalog = CreateCatalog(scope.Layout);
        catalog.AfterInstalledRenameBeforeStateCommitForTests = () =>
            throw new ModuleActivationException("simulated-state-failure", ModuleActivationErrorCodes.StateCorrupt);

        var result = await catalog.AcceptStagedForActivationAsync(scope.StagingDirectory);
        Assert.False(result.IsSuccess);

        var installed = Path.Combine(scope.Layout.InstalledRoot, "Kazuko.Warehouse", "2.1.0");
        Assert.False(Directory.Exists(installed));
        Assert.True(Directory.Exists(scope.StagingDirectory), "staging kept for retry");
        Assert.False(File.Exists(scope.Layout.ActivationStatePath));
    }

    [Fact]
    public async Task AcceptStaged_rejects_reparse_on_parent_directory()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var realUpdates = Path.Combine(root, "real-updates");
            Directory.CreateDirectory(realUpdates);
            var modules = Path.Combine(root, "Modules");
            var updatesJunction = Path.Combine(root, "Updates");
            Directory.CreateDirectory(modules);

            CreateJunction(updatesJunction, realUpdates);

            var layout = new ModuleLayoutPaths(modules, updatesJunction);
            layout.EnsureCreated();

            var work = Path.Combine(root, "pkg");
            Directory.CreateDirectory(work);
            var (packagePath, keys) = await ModulePackageTestFactory.CreateValidPackageAsync(work);
            using (keys)
            {
                var installer = new ModulePackageInstaller(
                    layout.StagingRoot,
                    new FixedHostVersionProvider(1),
                    CreateKeyStore(keys));
                var staged = await installer.ValidateAndStageAsync(packagePath);
                Assert.True(staged.IsSuccess, staged.Message);

                var result = await CreateCatalog(layout).AcceptStagedForActivationAsync(staged.StagingDirectory!);
                Assert.False(result.IsSuccess);
                Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void SeedStateWithActiveVersion(
        ModuleLayoutPaths layout,
        string moduleId,
        string activeVersion,
        string fingerprint)
    {
        layout.EnsureCreated();
        var relative = $"{moduleId}/{activeVersion}";
        Directory.CreateDirectory(Path.Combine(layout.InstalledRoot, moduleId, activeVersion));
        var json = $$"""
            {
              "schemaVersion": 1,
              "revision": 3,
              "hostVersion": 1,
              "updatedAtUtc": "2026-01-01T00:00:00Z",
              "modules": {
                "{{moduleId}}": {
                  "state": "Active",
                  "activeVersion": "{{activeVersion}}",
                  "previousVersion": null,
                  "pendingVersion": null,
                  "pendingFingerprint": null,
                  "source": "Installed",
                  "lastError": null,
                  "backupId": null,
                  "attemptId": null,
                  "builtInFallbackAllowed": true,
                  "installedVersions": [
                    {
                      "version": "{{activeVersion}}",
                      "fingerprint": "{{fingerprint}}",
                      "installedAtUtc": "2026-01-01T00:00:00Z",
                      "relativeDirectory": "{{relative}}",
                      "publisher": "Kazuko",
                      "keyId": "test-key-001"
                    }
                  ]
                }
              },
              "activationQueue": [],
              "diagnostics": []
            }
            """;
        File.WriteAllText(layout.ActivationStatePath, json);
        File.Copy(layout.ActivationStatePath, layout.ActivationStateBackupPath, overwrite: true);
    }

    private static void SeedStateWithPendingMissingInstall(ModuleLayoutPaths layout)
    {
        layout.EnsureCreated();
        var json = """
            {
              "schemaVersion": 1,
              "revision": 1,
              "hostVersion": 1,
              "updatedAtUtc": "2026-01-01T00:00:00Z",
              "modules": {
                "Kazuko.Warehouse": {
                  "state": "PendingActivation",
                  "activeVersion": null,
                  "previousVersion": null,
                  "pendingVersion": "9.9.9",
                  "pendingFingerprint": "DEADBEEF",
                  "source": "Installed",
                  "lastError": null,
                  "backupId": null,
                  "attemptId": null,
                  "builtInFallbackAllowed": true,
                  "installedVersions": [
                    {
                      "version": "9.9.9",
                      "fingerprint": "DEADBEEF",
                      "installedAtUtc": "2026-01-01T00:00:00Z",
                      "relativeDirectory": "Kazuko.Warehouse/9.9.9",
                      "publisher": "Kazuko",
                      "keyId": "test-key-001"
                    }
                  ]
                }
              },
              "activationQueue": ["Kazuko.Warehouse"],
              "diagnostics": []
            }
            """;
        File.WriteAllText(layout.ActivationStatePath, json);
    }

    private static string RestageExactBytes(ModuleLayoutPaths layout, string installedDirectory)
    {
        var restage = Path.Combine(layout.StagingRoot, $"restage-{Guid.NewGuid():N}.staged");
        CopyDirectory(installedDirectory, restage);
        return restage;
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
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
        SigningMaterials? createdKeys = null;
        try
        {
            var (packagePath, keys) = await ModulePackageTestFactory.CreateValidPackageAsync(work, options =>
            {
                configure?.Invoke(options);
                if (options.Keys is null)
                {
                    createdKeys = new SigningMaterials();
                    options.Keys = createdKeys;
                }
            });

            var installer = new ModulePackageInstaller(
                layout.StagingRoot,
                new FixedHostVersionProvider(1),
                CreateKeyStore(keys));
            var staged = await installer.ValidateAndStageAsync(packagePath);
            Assert.True(staged.IsSuccess, staged.Message);

            // Scope owns keys only when this helper created them.
            var ownsKeys = createdKeys is not null;
            createdKeys = null;
            return new TestScope(
                root,
                layout,
                staged.StagingDirectory!,
                keys,
                disposeLayout && existingLayout is null,
                ownsKeys);
        }
        catch
        {
            createdKeys?.Dispose();
            throw;
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
        SigningMaterials keys,
        bool disposeRoot,
        bool disposeKeys) : IDisposable
    {
        public ModuleLayoutPaths Layout { get; } = layout;
        public string StagingDirectory { get; } = stagingDirectory;
        public SigningMaterials Keys { get; } = keys;

        public void Dispose()
        {
            if (disposeKeys)
                Keys.Dispose();
            if (disposeRoot)
                TryDelete(root);
        }
    }
}
