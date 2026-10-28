using Kazuko.Core.Production;

namespace Kazuko.Infrastructure.Production;

public sealed class LocalProductionAttachmentStore(string? root=null) : IProductionAttachmentStore
{
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxDesignBytes = 50 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> DesignExtraExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".ai", ".cdr", ".dxf", ".dwg", ".psd", ".svg",
        ".zip", ".rar", ".7z", ".doc", ".docx", ".xls", ".xlsx"
    };

    public string ManagedRoot => root??Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERP",
        "production-attachments");

    private string StagingRoot => Path.Combine(ManagedRoot, "_staging");

    public async Task<string> ImportToStagingAsync(
        string sourceFilePath,
        string attachmentKind = ProductionAttachmentKinds.CustomerRequirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Không tìm thấy file đã chọn.", sourceFilePath);

        var kind = (attachmentKind ?? string.Empty).Trim().ToUpperInvariant();
        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("File không có phần mở rộng hợp lệ.");

        var isDesign = kind == ProductionAttachmentKinds.Design;
        var allowed = isDesign
            ? ImageExtensions.Concat(DesignExtraExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : ImageExtensions;

        if (!allowed.Contains(extension))
        {
            throw new InvalidOperationException(isDesign
                ? "File thiết kế chỉ hỗ trợ ảnh (JPG/PNG/WEBP), PDF, AI, CDR, DXF, DWG, PSD, SVG hoặc ZIP/RAR/7Z/Office."
                : "Ảnh mẫu chỉ hỗ trợ JPG, JPEG, PNG hoặc WEBP.");
        }

        var info = new FileInfo(sourceFilePath);
        var maxBytes = isDesign ? MaxDesignBytes : MaxImageBytes;
        if (info.Length > maxBytes)
        {
            var limitMb = maxBytes / (1024 * 1024);
            throw new InvalidOperationException(
                isDesign
                    ? $"File thiết kế vượt quá {limitMb} MB."
                    : $"Ảnh mẫu vượt quá {limitMb} MB.");
        }

        Directory.CreateDirectory(StagingRoot);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(StagingRoot, fileName);
        await using (var source = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        return destination;
    }

    public Task<string> FinalizeAsync(
        string stagingPath,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderCode);

        if (!TryIsUnderManagedRoot(stagingPath, out var stagingFull))
            throw new InvalidOperationException("Đường dẫn staging không hợp lệ.");

        if (!File.Exists(stagingFull))
            throw new FileNotFoundException("Không tìm thấy file staging.", stagingFull);

        var safeCode = SanitizeOrderCode(orderCode);
        var orderDir = Path.Combine(ManagedRoot, safeCode);
        Directory.CreateDirectory(orderDir);

        var extension = Path.GetExtension(stagingFull);
        var finalName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(orderDir, finalName);
        File.Move(stagingFull, destination, overwrite: false);
        return Task.FromResult(destination);
    }

    public void TryDeleteManagedFile(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return;

        try
        {
            if (!TryIsUnderManagedRoot(storedPath, out var full))
                return;
            if (File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    public void TryDeleteStagingFile(string? stagingPath)
    {
        if (string.IsNullOrWhiteSpace(stagingPath))
            return;

        try
        {
            if (!TryIsUnderManagedRoot(stagingPath, out var full))
                return;
            if (!full.StartsWith(Path.GetFullPath(StagingRoot), StringComparison.OrdinalIgnoreCase))
                return;
            if (File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private bool TryIsUnderManagedRoot(string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(ManagedRoot);
        return fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeOrderCode(string orderCode)
    {
        var safe = string.Concat(orderCode.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return string.IsNullOrWhiteSpace(safe) ? "draft" : safe;
    }
}
