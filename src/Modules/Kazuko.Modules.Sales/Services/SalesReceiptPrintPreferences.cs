using System.IO;
using System.Text.Json;
using Kazuko.Core.Printing;

namespace Kazuko.Modules.Sales.Services;

/// <summary>Persists preferred bill orientation under %LocalAppData%\KazukoERP.</summary>
public static class SalesReceiptPrintPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static SalesReceiptOrientation Load()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return SalesReceiptOrientation.Landscape;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<PrefsDto>(json);
            return dto?.Orientation == "portrait"
                ? SalesReceiptOrientation.Portrait
                : SalesReceiptOrientation.Landscape;
        }
        catch
        {
            return SalesReceiptOrientation.Landscape;
        }
    }

    public static void Save(SalesReceiptOrientation orientation)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(GetPath())!;
                Directory.CreateDirectory(dir);
                var dto = new PrefsDto
                {
                    Orientation = orientation == SalesReceiptOrientation.Portrait ? "portrait" : "landscape"
                };
                File.WriteAllText(GetPath(), JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch
            {
                // preference is best-effort
            }
        }
    }

    private static string GetPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KazukoERP",
            "sales.receipt.orientation.json");

    private sealed class PrefsDto
    {
        public string Orientation { get; set; } = "landscape";
    }
}
