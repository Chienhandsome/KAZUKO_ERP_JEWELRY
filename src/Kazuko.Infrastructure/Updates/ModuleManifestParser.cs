using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates;

internal static class ModuleManifestParser
{
    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "packageFormatVersion",
        "moduleId",
        "displayName",
        "moduleVersion",
        "minimumHostVersion",
        "entryAssembly",
        "publisher",
        "keyId",
        "signatureAlgorithm",
        "payload"
    };

    private static readonly HashSet<string> AllowedPayloadProperties = new(StringComparer.Ordinal)
    {
        "path",
        "sha256",
        "length"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ModulePackageManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ModulePackageValidationException("module.json phải là một đối tượng JSON.");

        EnsureNoDuplicateProperties(document.RootElement, "module.json");

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!AllowedRootProperties.Contains(property.Name))
                throw new ModulePackageValidationException("module.json chứa trường không được phép.");
        }

        RequireProperty(document.RootElement, "packageFormatVersion");
        if (document.RootElement.GetProperty("packageFormatVersion").ValueKind != JsonValueKind.Number ||
            !document.RootElement.GetProperty("packageFormatVersion").TryGetInt32(out var formatVersion) ||
            formatVersion != ModulePackageFormat.CurrentVersion)
        {
            throw new ModulePackageValidationException(
                "Định dạng gói không được hỗ trợ. Yêu cầu packageFormatVersion = 2 kèm danh sách payload.");
        }

        if (!document.RootElement.TryGetProperty("payload", out var payloadElement) ||
            payloadElement.ValueKind != JsonValueKind.Array ||
            payloadElement.GetArrayLength() == 0)
        {
            throw new ModulePackageValidationException("Manifest phải khai báo danh sách payload.");
        }

        foreach (var item in payloadElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ModulePackageValidationException("Mỗi phần tử payload phải là đối tượng.");

            EnsureNoDuplicateProperties(item, "payload");

            foreach (var property in item.EnumerateObject())
            {
                if (!AllowedPayloadProperties.Contains(property.Name))
                    throw new ModulePackageValidationException("payload chứa trường không được phép.");
            }
        }

        ModulePackageManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ModulePackageManifestDto>(utf8Json, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new ModulePackageValidationException("module.json không đúng định dạng.");
        }

        if (dto is null)
            throw new ModulePackageValidationException("module.json không hợp lệ.");

        return ValidateAndMap(dto);
    }

    private static ModulePackageManifest ValidateAndMap(ModulePackageManifestDto dto)
    {
        RequireText(dto.ModuleId, "moduleId");
        RequireText(dto.DisplayName, "displayName");
        RequireText(dto.ModuleVersion, "moduleVersion");
        RequireText(dto.EntryAssembly, "entryAssembly");
        RequireText(dto.Publisher, "publisher");
        RequireText(dto.KeyId, "keyId");
        RequireText(dto.SignatureAlgorithm, "signatureAlgorithm");

        if (dto.MinimumHostVersion < 1)
            throw new ModulePackageValidationException("minimumHostVersion không hợp lệ.");

        if (!IsValidModuleId(dto.ModuleId))
            throw new ModulePackageValidationException("moduleId không hợp lệ.");

        if (!Version.TryParse(NormalizeVersion(dto.ModuleVersion), out _))
            throw new ModulePackageValidationException("moduleVersion không hợp lệ.");

        if (dto.Payload is null || dto.Payload.Count == 0)
            throw new ModulePackageValidationException("Manifest phải khai báo danh sách payload.");

        var payload = new List<ModulePayloadEntry>(dto.Payload.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in dto.Payload)
        {
            RequireText(entry.Path, "payload.path");
            RequireText(entry.Sha256, "payload.sha256");
            if (entry.Length < 0)
                throw new ModulePackageValidationException("payload.length không hợp lệ.");

            var path = ModulePackagePathRules.NormalizeManifestPath(entry.Path);
            if (!seen.Add(path))
                throw new ModulePackageValidationException("payload chứa đường dẫn trùng.");

            if (entry.Sha256.Trim().Length != 64 ||
                !entry.Sha256.Trim().All(static ch => Uri.IsHexDigit(ch)))
            {
                throw new ModulePackageValidationException("payload.sha256 không hợp lệ.");
            }

            payload.Add(new ModulePayloadEntry(path, entry.Sha256.Trim().ToUpperInvariant(), entry.Length));
        }

        var entryAssembly = ModulePackagePathRules.NormalizeManifestPath(dto.EntryAssembly);
        if (!entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ModulePackageValidationException("entryAssembly phải là tệp .dll.");

        if (!payload.Any(p => string.Equals(p.Path, entryAssembly, StringComparison.OrdinalIgnoreCase)))
            throw new ModulePackageValidationException("entryAssembly phải có trong danh sách payload.");

        if (!string.Equals(
                dto.SignatureAlgorithm.Trim(),
                ModulePackageFormat.AllowedSignatureAlgorithm,
                StringComparison.Ordinal))
        {
            throw new ModulePackageValidationException("Thuật toán chữ ký không được phép.");
        }

        return new ModulePackageManifest(
            dto.PackageFormatVersion,
            dto.ModuleId.Trim(),
            dto.DisplayName.Trim(),
            dto.ModuleVersion.Trim(),
            dto.MinimumHostVersion,
            entryAssembly,
            dto.Publisher.Trim(),
            dto.KeyId.Trim(),
            ModulePackageFormat.AllowedSignatureAlgorithm,
            payload);
    }

    private static void EnsureNoDuplicateProperties(JsonElement objectElement, string context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ModulePackageValidationException($"{context} chứa trường JSON trùng tên.");
        }
    }

    private static void RequireProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out _))
            throw new ModulePackageValidationException($"Thiếu trường bắt buộc '{name}'.");
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ModulePackageValidationException($"Thiếu hoặc rỗng trường '{field}'.");
    }

    private static bool IsValidModuleId(string moduleId)
    {
        if (moduleId.Length is < 3 or > 128)
            return false;
        if (!moduleId.StartsWith("Kazuko.", StringComparison.Ordinal))
            return false;
        return moduleId.All(static ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_');
    }

    private static string NormalizeVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            2 => version + ".0",
            3 or 4 => version,
            _ => version
        };
    }

    private sealed class ModulePackageManifestDto
    {
        [JsonPropertyName("packageFormatVersion")]
        public int PackageFormatVersion { get; set; }

        [JsonPropertyName("moduleId")]
        public string ModuleId { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("moduleVersion")]
        public string ModuleVersion { get; set; } = string.Empty;

        [JsonPropertyName("minimumHostVersion")]
        public int MinimumHostVersion { get; set; }

        [JsonPropertyName("entryAssembly")]
        public string EntryAssembly { get; set; } = string.Empty;

        [JsonPropertyName("publisher")]
        public string Publisher { get; set; } = string.Empty;

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; } = string.Empty;

        [JsonPropertyName("signatureAlgorithm")]
        public string SignatureAlgorithm { get; set; } = string.Empty;

        [JsonPropertyName("payload")]
        public List<ModulePayloadEntryDto>? Payload { get; set; }
    }

    private sealed class ModulePayloadEntryDto
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("length")]
        public long Length { get; set; }
    }
}
