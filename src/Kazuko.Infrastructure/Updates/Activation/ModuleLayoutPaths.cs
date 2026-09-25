using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

/// <summary>
/// Resolves the on-disk module layout under a modules root.
/// Staging may optionally live outside ModulesRoot (007A LocalAppData Updates alias).
/// </summary>
public sealed class ModuleLayoutPaths : IModuleLayoutPaths
{
    public ModuleLayoutPaths(string modulesRoot, string? stagingRoot = null, string? databaseBackupRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        ModulesRoot = Path.GetFullPath(modulesRoot);
        StagingRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(stagingRoot)
                ? Path.Combine(ModulesRoot, "Staging")
                : stagingRoot);
        InstalledRoot = Path.Combine(ModulesRoot, "Installed");
        ActiveRoot = Path.Combine(ModulesRoot, "Active");
        PreviousRoot = Path.Combine(ModulesRoot, "Previous");
        QuarantineRoot = Path.Combine(ModulesRoot, "Quarantine");
        ActivationStatePath = Path.Combine(ModulesRoot, ModuleActivationStateFile.FileName);
        ActivationStateBackupPath = Path.Combine(ModulesRoot, ModuleActivationStateFile.BackupFileName);
        ActivationLockPath = Path.Combine(ModulesRoot, ModuleActivationStateFile.LockFileName);
        DatabaseBackupRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(databaseBackupRoot)
                ? Path.Combine(ModulesRoot, "DatabaseBackups")
                : databaseBackupRoot);
    }

    /// <summary>
    /// Dev-compatible layout: modules under LocalAppData\KazukoERP\Modules,
    /// staging under LocalAppData\KazukoERP\Updates (007A default).
    /// </summary>
    public static ModuleLayoutPaths CreateDefaultLocalAppData()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KazukoERP");
        return new ModuleLayoutPaths(
            Path.Combine(root, "Modules"),
            stagingRoot: Path.Combine(root, "Updates"),
            databaseBackupRoot: Path.Combine(root, "DatabaseBackups"));
    }

    public string ModulesRoot { get; }
    public string StagingRoot { get; }
    public string InstalledRoot { get; }
    public string ActiveRoot { get; }
    public string PreviousRoot { get; }
    public string QuarantineRoot { get; }
    public string ActivationStatePath { get; }
    public string ActivationStateBackupPath { get; }
    public string ActivationLockPath { get; }
    public string DatabaseBackupRoot { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ModulesRoot);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(InstalledRoot);
        Directory.CreateDirectory(ActiveRoot);
        Directory.CreateDirectory(PreviousRoot);
        Directory.CreateDirectory(QuarantineRoot);
        Directory.CreateDirectory(DatabaseBackupRoot);
    }
}
