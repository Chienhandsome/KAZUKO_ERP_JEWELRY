using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates.Activation;

/// <summary>
/// Atomic activation-state.json reader/writer with fail-closed corruption handling.
/// </summary>
internal sealed class ActivationStateStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IModuleLayoutPaths _paths;

    public ActivationStateStore(IModuleLayoutPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public ModuleActivationDocument Load(int hostVersion, IModuleLayoutPaths layoutForReferentialCheck)
    {
        Directory.CreateDirectory(_paths.ModulesRoot);

        var primaryExists = File.Exists(_paths.ActivationStatePath);
        var backupExists = File.Exists(_paths.ActivationStateBackupPath);

        if (!primaryExists && !backupExists)
            return CreateEmpty(hostVersion);

        var primaryResult = TryParseFile(_paths.ActivationStatePath, primaryExists);
        if (primaryResult.Kind == ParseKind.Valid)
        {
            ValidateReferential(primaryResult.Document!, layoutForReferentialCheck);
            return primaryResult.Document!;
        }

        if (primaryResult.Kind == ParseKind.SchemaUnsupported)
            throw CreateCoded(ModuleActivationErrorCodes.StateSchemaUnsupported, primaryResult.Error!);

        var backupResult = TryParseFile(_paths.ActivationStateBackupPath, backupExists);
        if (backupResult.Kind == ParseKind.Valid)
        {
            ValidateReferential(backupResult.Document!, layoutForReferentialCheck);
            RecoverPrimaryFromBackup(backupResult.RawBytes!);
            return backupResult.Document!;
        }

        if (backupResult.Kind == ParseKind.SchemaUnsupported)
            throw CreateCoded(ModuleActivationErrorCodes.StateSchemaUnsupported, backupResult.Error!);

        // Primary corrupt/missing-with-file and backup missing or corrupt → fail closed.
        throw CreateCoded(
            ModuleActivationErrorCodes.StateCorrupt,
            "activation-state.json bị hỏng và không khôi phục được từ bản sao lưu.");
    }

    public void WriteAtomic(ModuleActivationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(_paths.ModulesRoot);

        var dto = ToDto(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, WriteOptions);
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
                // Only promote a currently-valid primary into .bak — never overwrite a good
                // backup with a corrupt primary via File.Replace(backupReplaceFile).
                var primaryCheck = TryParseFile(_paths.ActivationStatePath, exists: true);
                if (primaryCheck.Kind == ParseKind.Valid)
                {
                    File.Copy(_paths.ActivationStatePath, _paths.ActivationStateBackupPath, overwrite: true);
                }

                var replaceTemp = tempPath + ".swap";
                File.Copy(tempPath, replaceTemp, overwrite: true);
                File.Delete(tempPath);
                if (File.Exists(_paths.ActivationStatePath))
                    File.Delete(_paths.ActivationStatePath);
                File.Move(replaceTemp, _paths.ActivationStatePath);
            }
            else
            {
                File.Move(tempPath, _paths.ActivationStatePath);
                File.Copy(_paths.ActivationStatePath, _paths.ActivationStateBackupPath, overwrite: true);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
            TryDeleteFile(tempPath + ".swap");
        }
    }

    public static ModuleActivationDocument CreateEmpty(int hostVersion) =>
        new(
            ModuleActivationStateFile.CurrentSchemaVersion,
            Revision: 0,
            hostVersion,
            DateTimeOffset.UtcNow,
            new SortedDictionary<string, ModuleActivationRecord>(StringComparer.Ordinal),
            Array.Empty<string>(),
            Array.Empty<string>());

    private void RecoverPrimaryFromBackup(byte[] backupBytes)
    {
        if (File.Exists(_paths.ActivationStatePath))
        {
            var corruptName =
                $"activation-state.json.corrupt-{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
            var corruptPath = Path.Combine(_paths.ModulesRoot, corruptName);
            File.Move(_paths.ActivationStatePath, corruptPath, overwrite: true);
        }

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
                stream.Write(backupBytes, 0, backupBytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _paths.ActivationStatePath);
            // Keep existing .bak untouched (it is the known-good source).
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private ParseResult TryParseFile(string path, bool exists)
    {
        if (!exists)
            return ParseResult.Missing();

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return ParseResult.Corrupt("State file trống.");

            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ParseResult.Corrupt("State root phải là object.");

            EnsureNoDuplicateProperties(document.RootElement, "activation-state");

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedRootProperties.Contains(property.Name))
                    return ParseResult.Corrupt($"Trường JSON không được phép: {property.Name}.");
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number ||
                !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return ParseResult.Corrupt("Thiếu schemaVersion hợp lệ.");
            }

            if (schemaVersion != ModuleActivationStateFile.CurrentSchemaVersion)
            {
                return ParseResult.SchemaUnsupported(
                    $"schemaVersion={schemaVersion} không được hỗ trợ (yêu cầu {ModuleActivationStateFile.CurrentSchemaVersion}).");
            }

            ActivationStateDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ActivationStateDto>(bytes, WriteOptions);
            }
            catch (JsonException ex)
            {
                return ParseResult.Corrupt(ex.Message);
            }

            if (dto is null)
                return ParseResult.Corrupt("Không deserialize được state.");

            var mapped = FromDto(dto, document.RootElement);
            return ParseResult.Valid(mapped, bytes);
        }
        catch (JsonException ex)
        {
            return ParseResult.Corrupt(ex.Message);
        }
        catch (IOException ex)
        {
            return ParseResult.Corrupt(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ParseResult.Corrupt(ex.Message);
        }
        catch (ModuleActivationException ex) when (ex.ErrorCode == ModuleActivationErrorCodes.StateCorrupt
                                                   || ex.ErrorCode == ModuleActivationErrorCodes.Referential)
        {
            return ParseResult.Corrupt(ex.Message);
        }
    }

    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "revision",
        "hostVersion",
        "updatedAtUtc",
        "modules",
        "activationQueue",
        "diagnostics"
    };

    private static readonly HashSet<string> AllowedModuleProperties = new(StringComparer.Ordinal)
    {
        "state",
        "activeVersion",
        "previousVersion",
        "pendingVersion",
        "pendingFingerprint",
        "source",
        "lastError",
        "backupId",
        "attemptId",
        "builtInFallbackAllowed",
        "installedVersions"
    };

    private static readonly HashSet<string> AllowedInstalledVersionProperties = new(StringComparer.Ordinal)
    {
        "version",
        "fingerprint",
        "installedAtUtc",
        "relativeDirectory",
        "publisher",
        "keyId"
    };

    private static ModuleActivationDocument FromDto(ActivationStateDto dto, JsonElement root)
    {
        if (dto.Revision < 0)
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "revision không hợp lệ.");

        if (dto.HostVersion < 1)
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "hostVersion không hợp lệ.");

        var modules = new SortedDictionary<string, ModuleActivationRecord>(StringComparer.Ordinal);
        if (root.TryGetProperty("modules", out var modulesElement))
        {
            if (modulesElement.ValueKind != JsonValueKind.Object)
                throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "modules phải là object.");

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in modulesElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "moduleId rỗng.");

                var moduleId = property.Name.Trim();
                if (!seenKeys.Add(moduleId))
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"moduleId trùng: {moduleId}.");

                if (property.Value.ValueKind != JsonValueKind.Object)
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"module '{moduleId}' không hợp lệ.");

                EnsureNoDuplicateProperties(property.Value, $"modules.{moduleId}");
                foreach (var child in property.Value.EnumerateObject())
                {
                    if (!AllowedModuleProperties.Contains(child.Name))
                        throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"Trường module không được phép: {child.Name}.");
                }

                if (!dto.Modules!.TryGetValue(property.Name, out var value) || value is null)
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"Không đọc được module '{moduleId}'.");

                modules[moduleId] = MapRecord(moduleId, value, property.Value);
            }
        }

        var queue = new List<string>();
        var seenQueue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dto.ActivationQueue is not null)
        {
            foreach (var id in dto.ActivationQueue)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "activationQueue chứa id rỗng.");

                var trimmed = id.Trim();
                if (!modules.ContainsKey(trimmed) &&
                    !modules.Keys.Any(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    throw CreateCoded(
                        ModuleActivationErrorCodes.StateCorrupt,
                        $"activationQueue tham chiếu module không tồn tại: {trimmed}.");
                }

                if (!seenQueue.Add(trimmed))
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"activationQueue trùng: {trimmed}.");

                queue.Add(GetCanonicalModuleId(modules, trimmed));
            }
        }

        var diagnostics = (dto.Diagnostics ?? [])
            .Where(static d => !string.IsNullOrWhiteSpace(d))
            .Select(static d => d.Trim())
            .ToArray();

        return new ModuleActivationDocument(
            dto.SchemaVersion,
            dto.Revision,
            dto.HostVersion,
            dto.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : dto.UpdatedAtUtc,
            modules,
            queue,
            diagnostics);
    }

    private static string GetCanonicalModuleId(
        SortedDictionary<string, ModuleActivationRecord> modules,
        string id)
    {
        foreach (var key in modules.Keys)
        {
            if (string.Equals(key, id, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return id;
    }

    private static ModuleActivationRecord MapRecord(string moduleId, ActivationRecordDto value, JsonElement element)
    {
        var state = ParseState(value.State);
        var installed = MapInstalledVersions(moduleId, value.InstalledVersions, element);

        if (!string.IsNullOrWhiteSpace(value.PendingVersion))
        {
            var pending = value.PendingVersion.Trim();
            if (!installed.Any(v => string.Equals(v.Version, pending, StringComparison.OrdinalIgnoreCase)))
            {
                throw CreateCoded(
                    ModuleActivationErrorCodes.Referential,
                    $"PendingVersion '{pending}' không có trong installed versions của {moduleId}.");
            }
        }

        return new ModuleActivationRecord(
            moduleId,
            state,
            NullIfWhiteSpace(value.ActiveVersion),
            NullIfWhiteSpace(value.PreviousVersion),
            NullIfWhiteSpace(value.PendingVersion),
            NullIfWhiteSpace(value.PendingFingerprint),
            string.IsNullOrWhiteSpace(value.Source) ? "Installed" : value.Source.Trim(),
            NullIfWhiteSpace(value.LastError),
            NullIfWhiteSpace(value.BackupId),
            NullIfWhiteSpace(value.AttemptId),
            value.BuiltInFallbackAllowed,
            installed);
    }

    private static IReadOnlyList<ModuleInstalledVersionRecord> MapInstalledVersions(
        string moduleId,
        List<InstalledVersionDto>? versions,
        JsonElement moduleElement)
    {
        if (versions is null || versions.Count == 0)
            return Array.Empty<ModuleInstalledVersionRecord>();

        if (!moduleElement.TryGetProperty("installedVersions", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"installedVersions của {moduleId} không hợp lệ.");
        }

        var list = new List<ModuleInstalledVersionRecord>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "installedVersions phần tử không hợp lệ.");

            EnsureNoDuplicateProperties(item, $"installedVersions[{index}]");
            foreach (var property in item.EnumerateObject())
            {
                if (!AllowedInstalledVersionProperties.Contains(property.Name))
                    throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"Trường installedVersion không được phép: {property.Name}.");
            }

            var dto = versions[index];
            if (string.IsNullOrWhiteSpace(dto.Version) ||
                string.IsNullOrWhiteSpace(dto.Fingerprint) ||
                string.IsNullOrWhiteSpace(dto.RelativeDirectory) ||
                string.IsNullOrWhiteSpace(dto.Publisher) ||
                string.IsNullOrWhiteSpace(dto.KeyId))
            {
                throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "installedVersion thiếu trường bắt buộc.");
            }

            var version = dto.Version.Trim();
            if (!seenVersions.Add(version))
                throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"installedVersion trùng: {version}.");

            list.Add(new ModuleInstalledVersionRecord(
                version,
                dto.Fingerprint.Trim().ToUpperInvariant(),
                dto.InstalledAtUtc == default ? DateTimeOffset.UtcNow : dto.InstalledAtUtc,
                dto.RelativeDirectory.Replace('\\', '/').Trim(),
                dto.Publisher.Trim(),
                dto.KeyId.Trim()));
            index++;
        }

        if (index != versions.Count)
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "installedVersions không khớp.");

        return list
            .OrderBy(static v => v.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateReferential(ModuleActivationDocument document, IModuleLayoutPaths paths)
    {
        var installedRoot = Path.GetFullPath(paths.InstalledRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var module in document.Modules.Values)
        {
            foreach (var version in module.InstalledVersions)
            {
                ModulePackagePathRules.ValidateRelativePath(version.RelativeDirectory, allowEmpty: false);
                var full = Path.GetFullPath(Path.Combine(paths.InstalledRoot, version.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateCoded(
                        ModuleActivationErrorCodes.Referential,
                        $"Đường dẫn installed thoát khỏi InstalledRoot: {version.RelativeDirectory}.");
                }

                if (!Directory.Exists(full))
                {
                    throw CreateCoded(
                        ModuleActivationErrorCodes.Referential,
                        $"Thiếu thư mục installed cho {module.ModuleId} {version.Version}.");
                }
            }

            if (!string.IsNullOrWhiteSpace(module.PendingVersion))
            {
                var pending = module.InstalledVersions.FirstOrDefault(v =>
                    string.Equals(v.Version, module.PendingVersion, StringComparison.OrdinalIgnoreCase));
                if (pending is null)
                {
                    throw CreateCoded(
                        ModuleActivationErrorCodes.Referential,
                        $"PendingVersion không tồn tại trên đĩa cho {module.ModuleId}.");
                }

                if (!string.IsNullOrWhiteSpace(module.PendingFingerprint) &&
                    !string.Equals(pending.Fingerprint, module.PendingFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateCoded(
                        ModuleActivationErrorCodes.Referential,
                        $"PendingFingerprint không khớp installed version của {module.ModuleId}.");
                }
            }
        }
    }

    private static ActivationStateDto ToDto(ModuleActivationDocument document)
    {
        var modules = new Dictionary<string, ActivationRecordDto>(StringComparer.Ordinal);
        foreach (var pair in document.Modules.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            modules[pair.Key] = new ActivationRecordDto
            {
                State = pair.Value.State.ToString(),
                ActiveVersion = pair.Value.ActiveVersion,
                PreviousVersion = pair.Value.PreviousVersion,
                PendingVersion = pair.Value.PendingVersion,
                PendingFingerprint = pair.Value.PendingFingerprint,
                Source = pair.Value.Source,
                LastError = pair.Value.LastError,
                BackupId = pair.Value.BackupId,
                AttemptId = pair.Value.AttemptId,
                BuiltInFallbackAllowed = pair.Value.BuiltInFallbackAllowed,
                InstalledVersions = pair.Value.InstalledVersions
                    .OrderBy(static v => v.Version, StringComparer.OrdinalIgnoreCase)
                    .Select(static v => new InstalledVersionDto
                    {
                        Version = v.Version,
                        Fingerprint = v.Fingerprint,
                        InstalledAtUtc = v.InstalledAtUtc,
                        RelativeDirectory = v.RelativeDirectory,
                        Publisher = v.Publisher,
                        KeyId = v.KeyId
                    })
                    .ToList()
            };
        }

        return new ActivationStateDto
        {
            SchemaVersion = document.SchemaVersion,
            Revision = document.Revision,
            HostVersion = document.HostVersion,
            UpdatedAtUtc = document.UpdatedAtUtc,
            Modules = modules,
            ActivationQueue = document.ActivationQueue.ToList(),
            Diagnostics = document.Diagnostics.ToList()
        };
    }

    private static ModuleActivationState ParseState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, "Thiếu state.");

        // Exact enum name, case-sensitive.
        if (!Enum.TryParse<ModuleActivationState>(value, ignoreCase: false, out var state) ||
            !string.Equals(state.ToString(), value, StringComparison.Ordinal))
        {
            throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"State không hợp lệ: {value}.");
        }

        return state;
    }

    private static void EnsureNoDuplicateProperties(JsonElement objectElement, string context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw CreateCoded(ModuleActivationErrorCodes.StateCorrupt, $"{context} chứa trường JSON trùng tên.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ModuleActivationException CreateCoded(string code, string message) =>
        new(message, code);

    private static void TryDeleteFile(string path)
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

    private enum ParseKind
    {
        Missing,
        Valid,
        Corrupt,
        SchemaUnsupported
    }

    private sealed class ParseResult
    {
        public ParseKind Kind { get; private init; }
        public ModuleActivationDocument? Document { get; private init; }
        public byte[]? RawBytes { get; private init; }
        public string? Error { get; private init; }

        public static ParseResult Missing() => new() { Kind = ParseKind.Missing };
        public static ParseResult Valid(ModuleActivationDocument document, byte[] bytes) =>
            new() { Kind = ParseKind.Valid, Document = document, RawBytes = bytes };
        public static ParseResult Corrupt(string error) =>
            new() { Kind = ParseKind.Corrupt, Error = error };
        public static ParseResult SchemaUnsupported(string error) =>
            new() { Kind = ParseKind.SchemaUnsupported, Error = error };
    }

    private sealed class ActivationStateDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("revision")]
        public long Revision { get; set; }

        [JsonPropertyName("hostVersion")]
        public int HostVersion { get; set; }

        [JsonPropertyName("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }

        [JsonPropertyName("modules")]
        public Dictionary<string, ActivationRecordDto>? Modules { get; set; }

        [JsonPropertyName("activationQueue")]
        public List<string>? ActivationQueue { get; set; }

        [JsonPropertyName("diagnostics")]
        public List<string>? Diagnostics { get; set; }
    }

    private sealed class ActivationRecordDto
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("activeVersion")]
        public string? ActiveVersion { get; set; }

        [JsonPropertyName("previousVersion")]
        public string? PreviousVersion { get; set; }

        [JsonPropertyName("pendingVersion")]
        public string? PendingVersion { get; set; }

        [JsonPropertyName("pendingFingerprint")]
        public string? PendingFingerprint { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        [JsonPropertyName("backupId")]
        public string? BackupId { get; set; }

        [JsonPropertyName("attemptId")]
        public string? AttemptId { get; set; }

        [JsonPropertyName("builtInFallbackAllowed")]
        public bool BuiltInFallbackAllowed { get; set; } = true;

        [JsonPropertyName("installedVersions")]
        public List<InstalledVersionDto>? InstalledVersions { get; set; }
    }

    private sealed class InstalledVersionDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("fingerprint")]
        public string? Fingerprint { get; set; }

        [JsonPropertyName("installedAtUtc")]
        public DateTimeOffset InstalledAtUtc { get; set; }

        [JsonPropertyName("relativeDirectory")]
        public string? RelativeDirectory { get; set; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; set; }

        [JsonPropertyName("keyId")]
        public string? KeyId { get; set; }
    }
}
