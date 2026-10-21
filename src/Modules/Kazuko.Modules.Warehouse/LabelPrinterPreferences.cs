using System.IO;
using System.Text.Json;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Nhớ máy in tem và lệch dọc (mm) theo từng tên máy in,
/// lưu dưới %LocalAppData%\KazukoERP.
/// </summary>
public static class LabelPrinterPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string? LoadPrinterName()
    {
        var dto = Read();
        return string.IsNullOrWhiteSpace(dto?.PrinterName) ? null : dto.PrinterName;
    }

    public static double LoadVerticalOffsetMm(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return 0;
        var dto = Read();
        if (dto?.VerticalOffsetsMm is null)
            return 0;
        return dto.VerticalOffsetsMm.TryGetValue(printerName, out var offset) ? offset : 0;
    }

    public static void Save(string printerName, double verticalOffsetMm)
    {
        lock (Gate)
        {
            try
            {
                var dto = Read() ?? new PrefsDto();
                dto.PrinterName = printerName;
                dto.VerticalOffsetsMm ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                dto.VerticalOffsetsMm[printerName] = verticalOffsetMm;
                var dir = Path.GetDirectoryName(GetPath())!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(GetPath(), JsonSerializer.Serialize(dto, JsonOptions));
            }
            catch
            {
                // preference is best-effort
            }
        }
    }

    private static PrefsDto? Read()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<PrefsDto>(File.ReadAllText(path));
        }
        catch
        {
            return null;
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
        public Dictionary<string, double>? VerticalOffsetsMm { get; set; }
    }
}
