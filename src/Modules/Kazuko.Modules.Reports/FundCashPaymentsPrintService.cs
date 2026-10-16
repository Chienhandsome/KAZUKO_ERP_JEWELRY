using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class FundCashPaymentsReportColumns
{
    public const string SettingsKey = "reports.fund.cash_payments.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("Code", "Số phiếu", 0.12f),
        new("VoucherDateText", "Ngày", 0.10f),
        new("PartyName", "Đối tượng", 0.20f),
        new("AmountVndText", "Số tiền", 0.14f),
        new("Notes", "Diễn giải", 0.24f),
        new("CreatedByName", "Người lập", 0.15f),
        new("FundName", "Quỹ tiền mặt", 0.14f, DefaultVisible: false)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[PaymentRows.Index]", HorzAlign.Center),
        "Code" => ("[PaymentRows.Code]", HorzAlign.Left),
        "VoucherDateText" => ("[PaymentRows.VoucherDateText]", HorzAlign.Center),
        "PartyName" => ("[PaymentRows.PartyName]", HorzAlign.Left),
        "AmountVndText" => ("[PaymentRows.AmountVndText]", HorzAlign.Right),
        "Notes" => ("[PaymentRows.Notes]", HorzAlign.Left),
        "CreatedByName" => ("[PaymentRows.CreatedByName]", HorzAlign.Left),
        "FundName" => ("[PaymentRows.FundName]", HorzAlign.Left),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class FundCashPaymentsPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<FundCashPaymentsRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new FundCashPaymentsPrintRow
        {
            Index = r.Index,
            Code = r.Code,
            VoucherDateText = r.VoucherDateText,
            PartyName = r.PartyName,
            AmountVndText = r.AmountVndText,
            Notes = r.Notes,
            CreatedByName = r.CreatedByName,
            FundName = r.FundName,
            AmountVnd = r.Item.AmountVnd
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : FundCashPaymentsReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "PaymentRows");
        FastReportTemplateHelper.BindDataSource(report, "PaymentRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue("FilterText", $"Từ ngày {fromDateText} đến {toDateText}");
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalAmount", printRows.Sum(x => x.AmountVnd).ToString("N0", Vi));

        report.Prepare();
        return report;
    }

    private static Report BuildReport(IReadOnlyList<ReportColumnDefinition> columns)
    {
        var report = new Report();
        FastReportTemplateHelper.AddStringParameter(report, "BranchName", "Chi nhánh");
        FastReportTemplateHelper.AddStringParameter(report, "PrintedAt", "Ngày in");
        FastReportTemplateHelper.AddStringParameter(report, "PrintedBy", "Người in");
        FastReportTemplateHelper.AddStringParameter(report, "FilterText", "Bộ lọc");
        FastReportTemplateHelper.AddStringParameter(report, "ItemCount", "Số phiếu");
        FastReportTemplateHelper.AddStringParameter(report, "TotalAmount", "Tổng tiền");

        var page = new ReportPage
        {
            Name = "Page1",
            PaperWidth = 210,
            PaperHeight = 297,
            LeftMargin = 10,
            TopMargin = 10,
            RightMargin = 10,
            BottomMargin = 10
        };
        report.Pages.Add(page);

        var contentWidth = FastReportTemplateHelper.Mm(190);
        var rowHeight = FastReportTemplateHelper.Mm(7);

        var titleBand = new ReportTitleBand { Name = "ReportTitle1", Height = FastReportTemplateHelper.Mm(28) };
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(0), contentWidth, FastReportTemplateHelper.Mm(8),
            "DANH SÁCH PHIẾU CHI TIỀN MẶT", 14, FontStyle.Bold, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(9), contentWidth, FastReportTemplateHelper.Mm(5),
            "[BranchName]", 10, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(14), contentWidth, FastReportTemplateHelper.Mm(5),
            "Ngày in: [PrintedAt]  ·  Người in: [PrintedBy]", 9, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(20), contentWidth, FastReportTemplateHelper.Mm(5),
            "Bộ lọc: [FilterText]", 9, FontStyle.Regular, HorzAlign.Left));

        var pageHeader = new PageHeaderBand { Name = "PageHeader1", Height = rowHeight };
        var dataBand = new DataBand { Name = "Data1", Height = rowHeight };

        var tableColumns = columns.Select(c =>
        {
            var (expr, align) = FundCashPaymentsReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số phiếu: [ItemCount]  ·  Tổng tiền chi: [TotalAmount] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ quỹ: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
