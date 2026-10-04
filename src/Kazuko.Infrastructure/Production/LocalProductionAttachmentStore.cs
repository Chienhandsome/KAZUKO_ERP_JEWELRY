using Kazuko.Core.Production;

namespace Kazuko.Infrastructure.Production;

public sealed class LocalProductionAttachmentStore : IProductionAttachmentStore
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public string ManagedRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERP",
        "production-attachments");

    private string StagingRoot => Path.Combine(ManagedRoot, "_staging");

    public async Task<string> ImportToStagingAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Không tìm thấy file đã chọn.", sourceFilePath);

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, JPEG, PNG hoặc WEBP.");

        var info = new FileInfo(sourceFilePath);
        if (info.Length > MaxFileBytes)
            throw new InvalidOperationException("File ảnh vượt quá 10 MB.");

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
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeOrderCode(string orderCode)
    {
        var safe = string.Concat(orderCode.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return string.IsNullOrWhiteSpace(safe) ? "draft" : safe;
    }
}
