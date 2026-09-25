using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Kazuko.Infrastructure.Updates;

internal static class ModulePackagePathRules
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly Regex SafeRelativeSegment = new(
        @"^[A-Za-z0-9][A-Za-z0-9._\- ]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ModulePackageValidationException("Đường dẫn payload không được để trống.");

        var normalized = path.Replace('\\', '/').Trim();
        ValidateRelativePath(normalized, allowEmpty: false);
        return normalized;
    }

    public static void ValidateRelativePath(string path, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (allowEmpty)
                return;
            throw new ModulePackageValidationException("Đường dẫn trong gói không hợp lệ.");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains(':', StringComparison.Ordinal))
            throw new ModulePackageValidationException("Đường dẫn chứa ký tự không được phép.");

        if (normalized.StartsWith('/') || normalized.StartsWith('\\'))
            throw new ModulePackageValidationException("Không chấp nhận đường dẫn tuyệt đối trong gói.");

        if (normalized.Contains("://", StringComparison.OrdinalIgnoreCase))
            throw new ModulePackageValidationException("Không chấp nhận URI trong đường dẫn gói.");

        // Reject empty segments / repeated separators explicitly (do not collapse silently).
        if (normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.StartsWith('/') ||
            normalized.EndsWith('/'))
        {
            throw new ModulePackageValidationException("Đường dẫn chứa phân đoạn rỗng hoặc dấu phân cách lặp.");
        }

        var segments = normalized.Split('/');
        if (segments.Length == 0 || segments.Any(string.IsNullOrEmpty))
            throw new ModulePackageValidationException("Đường dẫn chứa phân đoạn rỗng hoặc dấu phân cách lặp.");

        foreach (var segment in segments)
        {
            // Do not silently drop "." — two different spellings must not map to one destination.
            if (segment is "." or "..")
                throw new ModulePackageValidationException("Đường dẫn có thành phần không an toàn.");

            if (segment.EndsWith('.') || segment.EndsWith(' '))
                throw new ModulePackageValidationException("Đường dẫn có thành phần không an toàn.");

            var withoutTrailingDots = segment.TrimEnd('.', ' ');
            var device = withoutTrailingDots.Split('.')[0];
            if (ReservedDeviceNames.Contains(device))
                throw new ModulePackageValidationException("Đường dẫn dùng tên thiết bị Windows bị cấm.");

            if (!SafeRelativeSegment.IsMatch(segment))
                throw new ModulePackageValidationException("Đường dẫn chứa ký tự không được phép.");
        }
    }

    public static string ResolveUnderRoot(string stagingRoot, string relativePath)
    {
        // Re-validate the exact relative form; callers must not pass pre-collapsed aliases.
        ValidateRelativePath(relativePath, allowEmpty: false);

        var rootFull = Path.GetFullPath(stagingRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var combined = Path.GetFullPath(
            Path.Combine(stagingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ModulePackageValidationException("Đường dẫn thoát khỏi thư mục staging.");

        // Ensure GetFullPath did not collapse a different spelling into another destination.
        var expectedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var actualRelative = combined[rootFull.Length..];
        if (!string.Equals(actualRelative, expectedRelative, StringComparison.OrdinalIgnoreCase))
            throw new ModulePackageValidationException("Đường dẫn bị chuẩn hóa khác với khai báo gốc.");

        return combined;
    }

    public static bool LooksLikeSymlink(ZipArchiveEntryAttributes attributes) =>
        attributes.IsSymlink;
}

internal readonly struct ZipArchiveEntryAttributes(int externalAttributes)
{
    // ZIP "Unix" mode sits in the high 16 bits of ExternalAttributes (S_IFLNK = 0xA000).
    private const int UnixSymlinkMask = 0xA000;
    // Windows file attribute for reparse point (symlink/junction) sits in bit 10 (0x0400).
    private const int WindowsReparsePointMask = 0x0400;

    public bool IsSymlink =>
        ((externalAttributes >> 16) & 0xF000) == UnixSymlinkMask ||
        (externalAttributes & WindowsReparsePointMask) != 0;
}

internal sealed class ModulePackageValidationException(string message) : Exception(message);

internal static class CryptographicEquals
{
    public static bool HexSha256Equals(string expectedHex, byte[] actualHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || actualHash.Length != 32)
            return false;

        try
        {
            var expected = Convert.FromHexString(expectedHex.Trim());
            if (expected.Length != 32)
                return false;
            return CryptographicOperations.FixedTimeEquals(expected, actualHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
