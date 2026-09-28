using System.IO;

namespace Kazuko.Modules.HumanResources;

/// <summary>
/// Copies employee photos into LocalAppData so display survives deletion of the original file.
/// </summary>
internal static class EmployeePhotoStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    public static string PhotosDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERP",
        "employee-photos");

    public static string Import(string sourceFilePath, string? employeeCodeHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Không tìm thấy file ảnh đã chọn.", sourceFilePath);

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, PNG, BMP, GIF hoặc WEBP.");

        Directory.CreateDirectory(PhotosDirectory);

        var safeHint = string.IsNullOrWhiteSpace(employeeCodeHint)
            ? "nv"
            : string.Concat(employeeCodeHint.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeHint))
            safeHint = "nv";

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
