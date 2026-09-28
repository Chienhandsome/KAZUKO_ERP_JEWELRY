using System.IO;

namespace Kazuko.Modules.Customers;

/// <summary>
/// Copies customer photos into LocalAppData so display survives deletion of the original file.
/// </summary>
internal static class CustomerPhotoStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    public static string PhotosDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERP",
        "customer-photos");

    public static string Import(string sourceFilePath, string? customerCodeHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Không tìm thấy file ảnh đã chọn.", sourceFilePath);

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, PNG, BMP, GIF hoặc WEBP.");

        Directory.CreateDirectory(PhotosDirectory);

        var safeHint = string.IsNullOrWhiteSpace(customerCodeHint)
            ? "kh"
            : string.Concat(customerCodeHint.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeHint))
            safeHint = "kh";

        var fileName = $"{safeHint}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(PhotosDirectory, fileName);
        File.Copy(sourceFilePath, destination, overwrite: false);
        return destination;
    }

    public static void TryDeleteManagedFile(string? photoPath)
    {
        if (string.IsNullOrWhiteSpace(photoPath))
            return;

        try
        {
            var full = Path.GetFullPath(photoPath);
            var root = Path.GetFullPath(PhotosDirectory);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return;
            if (File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
