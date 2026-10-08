using System.IO;

namespace Kazuko.Modules.Dashboard;

/// <summary>
/// Copies company logos into LocalAppData so display survives deletion of the original file.
/// </summary>
internal static class CompanyLogoStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    public static string LogosDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KazukoERP",
        "company-logos");

    public static string Import(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Không tìm thấy file ảnh đã chọn.", sourceFilePath);

        var extension = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, PNG, BMP, GIF hoặc WEBP.");

        Directory.CreateDirectory(LogosDirectory);

        var fileName = $"logo_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(LogosDirectory, fileName);
        File.Copy(sourceFilePath, destination, overwrite: false);
        return destination;
    }

    public static void TryDeleteManagedFile(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
            return;

        try
        {
            var full = Path.GetFullPath(logoPath);
            var root = Path.GetFullPath(LogosDirectory);
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
