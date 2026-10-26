using System.IO;
using FastReport.Export.Image;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Services;

public sealed class ReceiptTemplatePreviewService(
    SalesReceiptPrintService printer, ICompanyProfileService companyProfile) : IReceiptTemplatePreviewService
{
    public async Task<byte[]> CreatePreviewAsync(string templateCode, AuthenticatedSession session)
        => await CreateDraftPreviewAsync(templateCode, session, null);

    public async Task<byte[]> CreateDraftPreviewAsync(string templateCode, AuthenticatedSession session, byte[]? content)
    {
        var orientation = templateCode switch
        {
            PrintTemplateCodes.SalesReceiptA5 => SalesReceiptOrientation.Landscape,
            PrintTemplateCodes.SalesReceiptA5Portrait => SalesReceiptOrientation.Portrait,
            _ => throw new ArgumentException("Mẫu bill không hợp lệ.", nameof(templateCode))
        };
        var profile = await companyProfile.GetAsync(session);
        var line = new SalesLineSnapshot(0, "B600022", "Nhẫn nữ", 1.2m, 8000000m, 300000m, 0m,
            new SalesLineDraft(0, 8000000m, 300000m, 0m, null),
            new SalesLineAmounts(0, 9600000m, 300000m, 0m, 9900000m, false), "610", 0m, 1.2m);
        var totals = new SalesCheckoutTotals(9900000m, 0m, 9900000m, 0m, 0m, 9900000m, 9900000m, 0m, 0m);
        var checkout = new SalesCheckoutDetail(0, "BILL-MAU", "SALE", new DateTime(2026, 1, 1),
            0, "Khách hàng mẫu", "0901234567", 0, "Nhân viên mẫu", 0, "Nhân viên mẫu",
            null, null, [line], [], [], totals, "Dữ liệu minh họa");
        var receipt = new SalesReceiptDto(profile.Name, session.BranchName ?? string.Empty,
            profile.Address, profile.Phone, checkout);
        using var report = await printer.CreatePreparedReportAsync(receipt, session, orientation, templateContent: content);
        using var stream = new MemoryStream();
        using var export = new ImageExport
        {
            ImageFormat = ImageExportFormat.Png, Resolution = 120, PageRange = FastReport.PageRange.PageNumbers,
            PageNumbers = "1"
        };
        export.Export(report, stream);
        return stream.ToArray();
    }
}
