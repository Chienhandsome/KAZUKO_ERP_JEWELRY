namespace Kazuko.Core.Updates;

/// <summary>
/// Supported package format. Legacy manifests without a payload allow-list are rejected.
/// </summary>
public static class ModulePackageFormat
{
    public const int CurrentVersion = 2;
    public const string ManifestFileName = "module.json";
    public const string SignatureFileName = "signature.sig";
    public const string AllowedSignatureAlgorithm = "RSASSA-PSS-SHA256";
}

public sealed record ModulePayloadEntry(string Path, string Sha256, long Length);

public sealed record ModulePackageManifest(
    int PackageFormatVersion,
    string ModuleId,
    string DisplayName,
    string ModuleVersion,
    int MinimumHostVersion,
    string EntryAssembly,
    string Publisher,
    string KeyId,
    string SignatureAlgorithm,
    IReadOnlyList<ModulePayloadEntry> Payload);

public sealed record ModuleValidationSummary(
    int PayloadCount,
    long TotalUncompressedBytes,
    string SignatureAlgorithm,
    string Publisher,
    string KeyId);

public sealed record ModuleInstallResult(
    bool IsSuccess,
    string Message,
    string? StagingDirectory = null,
    string? ModuleId = null,
    string? ModuleVersion = null,
    ModuleValidationSummary? Summary = null);

public interface IModulePackageInstaller
{
    Task<ModuleInstallResult> ValidateAndStageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}

public interface IHostVersionProvider
{
    int CurrentHostVersion { get; }
}

public sealed class FixedHostVersionProvider(int currentHostVersion) : IHostVersionProvider
{
    public int CurrentHostVersion { get; } = currentHostVersion;
}

public interface ITrustedPublicKeyStore
{
    /// <summary>
    /// Returns SubjectPublicKeyInfo (DER) bytes for RSA verification, or null when unknown.
    /// </summary>
    byte[]? FindPublicKey(string publisher, string keyId);
}

/// <summary>
/// Empty store used at composition until production keys are provisioned.
/// Signature verification fails safely with an unknown-publisher message.
/// </summary>
public sealed class EmptyTrustedPublicKeyStore : ITrustedPublicKeyStore
{
    public byte[]? FindPublicKey(string publisher, string keyId) => null;
}

public sealed class InMemoryTrustedPublicKeyStore : ITrustedPublicKeyStore
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string publisher, string keyId, byte[] subjectPublicKeyInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        _keys[MakeKey(publisher, keyId)] = subjectPublicKeyInfo;
    }

    public byte[]? FindPublicKey(string publisher, string keyId) =>
        _keys.TryGetValue(MakeKey(publisher, keyId), out var key) ? key : null;

    private static string MakeKey(string publisher, string keyId) =>
        publisher.Trim() + "\u001f" + keyId.Trim();
}

public sealed class ModulePackageResourceLimits
{
    public int MaxEntryCount { get; init; } = 256;
    public long MaxEntryBytes { get; init; } = 50L * 1024 * 1024;
    public long MaxTotalUncompressedBytes { get; init; } = 200L * 1024 * 1024;
    public double MaxCompressionRatio { get; init; } = 100.0;

    public static ModulePackageResourceLimits Default { get; } = new();
}

public interface IModulePackageSignatureVerifier
{
    bool IsAlgorithmAllowed(string algorithm);

    bool Verify(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        ReadOnlySpan<byte> subjectPublicKeyInfo,
        string algorithm);
}
