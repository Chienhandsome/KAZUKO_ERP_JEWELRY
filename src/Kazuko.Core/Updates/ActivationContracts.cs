namespace Kazuko.Core.Updates;

/// <summary>
/// Lifecycle states for an installed module package. Import promotes to
/// <see cref="PendingActivation"/>; cold-start activation (later tasks) advances further.
/// </summary>
public enum ModuleActivationState
{
    Staged = 0,
    PendingActivation = 1,
    Activating = 2,
    Active = 3,
    Failed = 4,
    RollbackPending = 5,
    RolledBack = 6,
    Quarantined = 7
}

public static class ModuleActivationStateFile
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "activation-state.json";
    public const string BackupFileName = "activation-state.json.bak";
    public const string TempFileName = "activation-state.json.tmp";
    public const string LockFileName = "activation.lock";
}

public sealed record ModuleActivationRecord(
    string ModuleId,
    ModuleActivationState State,
    string? ActiveVersion,
    string? PreviousVersion,
    string? PendingVersion,
    string Source,
    string? Publisher,
    string? KeyId,
    string? PackageSha256,
    string? LastError,
    string? BackupId,
    string? AttemptId,
    bool BuiltInFallbackAllowed);

public sealed record ModuleActivationDocument(
    int SchemaVersion,
    int HostVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyDictionary<string, ModuleActivationRecord> Modules,
    IReadOnlyList<string> ActivationQueue);

public sealed record ModuleActivationAcceptResult(
    bool IsSuccess,
    string Message,
    ModuleActivationRecord? Record = null,
    string? InstalledDirectory = null);

public interface IModuleLayoutPaths
{
    string ModulesRoot { get; }
    string StagingRoot { get; }
    string InstalledRoot { get; }
    string ActiveRoot { get; }
    string PreviousRoot { get; }
    string QuarantineRoot { get; }
    string ActivationStatePath { get; }
    string ActivationStateBackupPath { get; }
    string ActivationLockPath { get; }
    string DatabaseBackupRoot { get; }

    void EnsureCreated();
}

public interface IModuleActivationLock : IAsyncDisposable, IDisposable
{
    bool IsAcquired { get; }
}

public interface IModuleActivationLockFactory
{
    /// <summary>
    /// Attempts an exclusive cross-process lock on the activation catalog.
    /// Returns false when another process already holds the lock.
    /// </summary>
    bool TryAcquire(out IModuleActivationLock? activationLock);
}

public interface IModuleActivationCatalog
{
    Task<ModuleActivationDocument> LoadDocumentAsync(CancellationToken cancellationToken = default);

    Task<ModuleActivationRecord?> GetAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes a validated staging directory into Installed/{moduleId}/{version}
    /// and marks the module <see cref="ModuleActivationState.PendingActivation"/>.
    /// Does not load assemblies or run migrations.
    /// </summary>
    Task<ModuleActivationAcceptResult> AcceptStagedForActivationAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default);

    Task SaveAsync(ModuleActivationRecord record, CancellationToken cancellationToken = default);
}
