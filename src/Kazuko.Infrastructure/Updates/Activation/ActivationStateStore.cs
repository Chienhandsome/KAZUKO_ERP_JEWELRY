using System.Text.Json;
using System.Text.Json.Serialization;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

/// <summary>
/// Atomic activation-state.json reader/writer with .bak recovery.
/// </summary>
internal sealed class ActivationStateStore(IModuleLayoutPaths paths)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private readonly IModuleLayoutPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public ModuleActivationDocument LoadOrEmpty(int hostVersion)
    {
        Directory.CreateDirectory(_paths.ModulesRoot);

        if (TryRead(_paths.ActivationStatePath, out var primary))
            return primary!;

        if (TryRead(_paths.ActivationStateBackupPath, out var backup))
        {
            // Restore primary from backup when primary is missing/corrupt.
            try
            {
                WriteAtomic(backup!);
            }
            catch
            {
                // Best-effort restore; still return the recovered document.
            }

            return backup!;
        }

        return CreateEmpty(hostVersion);
    }

    public void WriteAtomic(ModuleActivationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(_paths.ModulesRoot);

        var dto = ToDto(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, SerializerOptions);
        var tempPath = Path.Combine(_paths.ModulesRoot, ModuleActivationStateFile.TempFileName);

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.ActivationStatePath))
            {
                File.Replace(
                    tempPath,
                    _paths.ActivationStatePath,
                    _paths.ActivationStateBackupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _paths.ActivationStatePath);
                File.Copy(_paths.ActivationStatePath, _paths.ActivationStateBackupPath, overwrite: true);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static ModuleActivationDocument CreateEmpty(int hostVersion) =>
        new(
            ModuleActivationStateFile.CurrentSchemaVersion,
            hostVersion,
            DateTimeOffset.UtcNow,
            new Dictionary<string, ModuleActivationRecord>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>());

    private bool TryRead(string path, out ModuleActivationDocument? document)
    {
        document = null;
        if (!File.Exists(path))
            return false;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return false;

            var dto = JsonSerializer.Deserialize<ActivationStateDto>(bytes, SerializerOptions);
            if (dto is null)
                return false;

            document = FromDto(dto);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ModuleActivationDocument FromDto(ActivationStateDto dto)
    {
        if (dto.SchemaVersion < 1)
            throw new JsonException("Unsupported activation state schema.");

        var modules = new Dictionary<string, ModuleActivationRecord>(StringComparer.OrdinalIgnoreCase);
        if (dto.Modules is not null)
        {
            foreach (var pair in dto.Modules)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                    continue;

                var value = pair.Value;
                modules[pair.Key.Trim()] = new ModuleActivationRecord(
                    pair.Key.Trim(),
                    ParseState(value.State),
                    NullIfWhiteSpace(value.ActiveVersion),
                    NullIfWhiteSpace(value.PreviousVersion),
                    NullIfWhiteSpace(value.PendingVersion),
                    string.IsNullOrWhiteSpace(value.Source) ? "Installed" : value.Source.Trim(),
                    NullIfWhiteSpace(value.Publisher),
                    NullIfWhiteSpace(value.KeyId),
                    NullIfWhiteSpace(value.PackageSha256),
                    NullIfWhiteSpace(value.LastError),
                    NullIfWhiteSpace(value.BackupId),
                    NullIfWhiteSpace(value.AttemptId),
                    value.BuiltInFallbackAllowed);
            }
        }

        var queue = (dto.ActivationQueue ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ModuleActivationDocument(
            dto.SchemaVersion,
            dto.HostVersion < 1 ? 1 : dto.HostVersion,
            dto.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : dto.UpdatedAtUtc,
            modules,
            queue);
    }

    private static ActivationStateDto ToDto(ModuleActivationDocument document)
    {
        var modules = new Dictionary<string, ActivationRecordDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in document.Modules)
        {
            modules[pair.Key] = new ActivationRecordDto
            {
                State = pair.Value.State.ToString(),
                ActiveVersion = pair.Value.ActiveVersion,
                PreviousVersion = pair.Value.PreviousVersion,
                PendingVersion = pair.Value.PendingVersion,
                Source = pair.Value.Source,
                Publisher = pair.Value.Publisher,
                KeyId = pair.Value.KeyId,
                PackageSha256 = pair.Value.PackageSha256,
                LastError = pair.Value.LastError,
                BackupId = pair.Value.BackupId,
                AttemptId = pair.Value.AttemptId,
                BuiltInFallbackAllowed = pair.Value.BuiltInFallbackAllowed
            };
        }

        return new ActivationStateDto
        {
            SchemaVersion = document.SchemaVersion,
            HostVersion = document.HostVersion,
            UpdatedAtUtc = document.UpdatedAtUtc,
            Modules = modules,
            ActivationQueue = document.ActivationQueue.ToList()
        };
    }

    private static ModuleActivationState ParseState(string? value)
    {
        if (Enum.TryParse<ModuleActivationState>(value, ignoreCase: true, out var state))
            return state;
        throw new JsonException($"Unknown module activation state '{value}'.");
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private sealed class ActivationStateDto
    {
        public int SchemaVersion { get; set; }
        public int HostVersion { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public Dictionary<string, ActivationRecordDto>? Modules { get; set; }
        public List<string>? ActivationQueue { get; set; }
    }

    private sealed class ActivationRecordDto
    {
        public string? State { get; set; }
        public string? ActiveVersion { get; set; }
        public string? PreviousVersion { get; set; }
        public string? PendingVersion { get; set; }
        public string? Source { get; set; }
        public string? Publisher { get; set; }
        public string? KeyId { get; set; }
        public string? PackageSha256 { get; set; }
        public string? LastError { get; set; }
        public string? BackupId { get; set; }
        public string? AttemptId { get; set; }
        public bool BuiltInFallbackAllowed { get; set; } = true;
    }
}
