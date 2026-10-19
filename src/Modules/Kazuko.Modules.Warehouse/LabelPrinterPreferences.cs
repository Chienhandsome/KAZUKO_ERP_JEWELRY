using System.IO;
using System.Text.Json;

namespace Kazuko.Modules.Warehouse;

/// <summary>Nhớ máy in tem đã chọn lần trước, lưu dưới %LocalAppData%\KazukoERP.</summary>
public static class LabelPrinterPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string? LoadPrinterName()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<PrefsDto>(json);
            return string.IsNullOrWhiteSpace(dto?.PrinterName) ? null : dto.PrinterName;
        }
        catch
        {
            return null;
        }
    }

    public static void SavePrinterName(string? printerName)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(GetPath())!;
                Directory.CreateDirectory(dir);
                var dto = new PrefsDto { PrinterName = printerName ?? string.Empty };
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
            "jewelry.label.printer.json");

    private sealed class PrefsDto
    {
        public string PrinterName { get; set; } = string.Empty;
    }
}
