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

public static class ModuleActivationErrorCodes
{
    public const string StateCorrupt = "STATE_CORRUPT";
    public const string StateSchemaUnsupported = "STATE_SCHEMA_UNSUPPORTED";
    public const string Locked = "LOCKED";
    public const string AbandonedLock = "LOCK_ABANDONED";
    public const string ReservedModule = "RESERVED_MODULE";
    public const string PendingConflict = "PENDING_CONFLICT";
    public const string TamperedInstall = "TAMPERED_INSTALL";
    public const string InvalidStaging = "INVALID_STAGING";
    public const string Referential = "STATE_REFERENTIAL";
}

/// <summary>
/// Immutable record of one installed package version under Installed/{moduleId}/{version}.
/// </summary>
public sealed record ModuleInstalledVersionRecord(
    string Version,
    string Fingerprint,
    DateTimeOffset InstalledAtUtc,
    string RelativeDirectory,
    string Publisher,
    string KeyId);

public sealed record ModuleActivationRecord(
    string ModuleId,
    ModuleActivationState State,
    string? ActiveVersion,
    string? PreviousVersion,
    string? PendingVersion,
    string? PendingFingerprint,
    string Source,
    string? LastError,
    string? BackupId,
    string? AttemptId,
    bool BuiltInFallbackAllowed,
    IReadOnlyList<ModuleInstalledVersionRecord> InstalledVersions);

public sealed record ModuleActivationDocument(
    int SchemaVersion,
    long Revision,
    int HostVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyDictionary<string, ModuleActivationRecord> Modules,
    IReadOnlyList<string> ActivationQueue,
    IReadOnlyList<string> Diagnostics);

public sealed record ModuleActivationAcceptResult(
    bool IsSuccess,
    string Message,
    string? ErrorCode = null,
    ModuleActivationRecord? Record = null,
    string? InstalledDirectory = null,
    string? CleanupWarning = null);

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

public enum ModuleActivationLockStatus
{
    Acquired = 0,
    Locked = 1,
    AbandonedRequiresRecovery = 2
}

public interface IModuleActivationLock : IAsyncDisposable, IDisposable
{
    bool IsAcquired { get; }
}

public interface IModuleActivationLockFactory
{
    /// <summary>
    /// Attempts an exclusive cross-process lock (named mutex + file lock).
    /// </summary>
    ModuleActivationLockStatus TryAcquire(
        TimeSpan timeout,
        out IModuleActivationLock? activationLock);
}

/// <summary>
/// B1 public surface: load snapshot, get module, accept staged → PendingActivation.
/// No caller may set Active/Previous/Activating/Quarantined/RollbackPending directly.
/// </summary>
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
}

public static class ReservedHostModules
{
    public static readonly IReadOnlyList<string> DeniedIds =
    [
        "Kazuko.Dashboard",
        "Kazuko.Authentication",
        "Kazuko.Customers",
        "Kazuko.HumanResources",
        "Kazuko.PriceBoard"
    ];

    public static bool IsDenied(string moduleId) =>
        DeniedIds.Any(id => string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase));
}
