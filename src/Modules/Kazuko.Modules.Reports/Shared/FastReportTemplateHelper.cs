using System.Drawing;
using System.IO;
using FastReport;
using FastReport.Utils;

namespace Kazuko.Modules.Reports.Shared;

/// <summary>Helper dựng / nạp mẫu .frx (Bounds dùng Units.Millimeters, không phải mm giấy).</summary>
public static class FastReportTemplateHelper
{
    public static string GetTemplatePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Reports", "Templates", fileName);

    public static float Mm(float millimeters) => millimeters * Units.Millimeters;

    public static void EnsureTemplate(string templatePath, Action<Report> buildDefault)
    {
        if (File.Exists(templatePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
        using var report = new Report();
        buildDefault(report);
        report.Save(templatePath);
    }

    public static void BindDataSource(Report report, string dataSourceName)
    {
        var dataSource = report.GetDataSource(dataSourceName);
        if (dataSource is null)
            return;

        dataSource.Enabled = true;
        foreach (Base obj in report.AllObjects)
        {
            if (obj is DataBand dataBand)
                dataBand.DataSource = dataSource;
        }
    }

    public static void AddStringParameter(Report report, string name, string description)
    {
        var parameter = new FastReport.Data.Parameter(name)
        {
            DataType = typeof(string),
            Description = description,
            Value = string.Empty
        };
        report.Parameters.Add(parameter);
    }

    public static TextObject CreateText(
        float left,
        float top,
        float width,
        float height,
        string text,
        float fontSize,
        FontStyle style,
        HorzAlign align)
    {
        return new TextObject
        {
            Bounds = new RectangleF(left, top, width, height),
            Text = text,
            Font = new Font("Arial", fontSize, style),
            HorzAlign = align,
            VertAlign = VertAlign.Center,
            CanGrow = false,
            WordWrap = true,
            Padding = new System.Windows.Forms.Padding(2, 1, 2, 1)
        };
    }

    public static string JoinFilterParts(IEnumerable<string?> parts)
    {
        var list = parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();
        return list.Count == 0 ? "Tất cả" : string.Join(" · ", list);
    }

    public static string? FilterPart(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("Tất cả", StringComparison.Ordinal))
            return null;
        return $"{label}: {value.Trim()}";
    }
}
