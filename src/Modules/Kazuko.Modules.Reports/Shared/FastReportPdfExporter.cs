using System.Diagnostics;
using System.IO;
using FastReport;
using FastReport.Export.PdfSimple;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Reports.Shared;

/// <summary>Xuất FastReport ra PDF tạm, mở ngoài app hoặc gửi in qua shell.</summary>
public sealed class FastReportPdfExporter
{
    private static IAuthorizationService? _authorization;
    public static void ConfigureAuthorization(IAuthorizationService authorization) => _authorization = authorization;
    public string ExportToTempPdf(Report report, string filePrefix, string purpose = "view")
    {
        ArgumentNullException.ThrowIfNull(report);
        var safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "report" : filePrefix.Trim();
        var path = Path.Combine(
            Path.GetTempPath(),
            $"kazuko-{safePrefix}-{purpose}-{Guid.NewGuid():N}.pdf");
        using var export = new PDFSimpleExport();
        report.Export(export, path);
        return path;
    }

    public void OpenExternally(Report report, string filePrefix)
    {
        _authorization?.DemandAsync([new("REP_VIEW"), new("REP_EXPORT")]).GetAwaiter().GetResult();
        var path = ExportToTempPdf(report, filePrefix, "preview");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Print(Report report, string filePrefix)
    {
        _authorization?.DemandAsync([new("REP_VIEW"), new("REP_EXPORT")]).GetAwaiter().GetResult();
        var path = ExportToTempPdf(report, filePrefix, "print");
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "print"
        });
    }

    public static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }
}
