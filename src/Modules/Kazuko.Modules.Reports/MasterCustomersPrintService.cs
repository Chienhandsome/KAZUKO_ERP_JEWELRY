using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class MasterCustomersReportColumns
{
    public const string SettingsKey = "reports.master.customers.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("Code", "Mã KH", 0.10f),
        new("Kind", "Loại khách", 0.13f),
        new("Name", "Tên khách hàng", 0.22f),
        new("Phone", "Điện thoại", 0.11f),
        new("Email", "Email", 0.15f),
        new("Address", "Địa chỉ", 0.19f),
        new("GroupName", "Nhóm", 0.10f),
        new("IdentityNumber", "CMND/CCCD", 0.11f),
        new("CreditLimit", "Hạn mức công nợ", 0.12f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[CustomerRows.Index]", HorzAlign.Center),
        "Kind" => ("[CustomerRows.Kind]", HorzAlign.Left),
        "Code" => ("[CustomerRows.Code]", HorzAlign.Left),
        "Name" => ("[CustomerRows.Name]", HorzAlign.Left),
        "Phone" => ("[CustomerRows.Phone]", HorzAlign.Left),
        "Email" => ("[CustomerRows.Email]", HorzAlign.Left),
        "Address" => ("[CustomerRows.Address]", HorzAlign.Left),
        "GroupName" => ("[CustomerRows.GroupName]", HorzAlign.Left),
        "IdentityNumber" => ("[CustomerRows.IdentityNumber]", HorzAlign.Left),
        "CreditLimit" => ("[CustomerRows.CreditLimitText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class MasterCustomersPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<MasterCustomersRowViewModel> rows,
        AuthenticatedSession session,
        string searchText,
        IReadOnlyList<ReportColumnDefinition> activeColumns,
        string customerKind = "Có hồ sơ")
    {
        var printRows = rows.Select(r => new MasterCustomersPrintRow
        {
            Index = r.Index,
            Code = r.Code,
            Kind = r.Item.IsWalkIn ? "Vãng lai · Hệ thống" : "Có hồ sơ",
            Name = r.Name,
            Phone = r.Phone,
            Email = r.Email,
            Address = r.Address,
            GroupName = r.GroupName,
            IdentityNumber = r.IdentityNumber,
            CreditLimitText = r.CreditLimitText,
            CreditLimit = r.Item.CreditLimit
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : MasterCustomersReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "CustomerRows");
        FastReportTemplateHelper.BindDataSource(report, "CustomerRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                $"Loại khách: {customerKind}",
                string.IsNullOrWhiteSpace(searchText) ? null : $"Từ khóa: {searchText.Trim()}"
            ]));
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalCreditLimit", printRows.Sum(x => x.CreditLimit).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "ItemCount", "Số khách hàng");
        FastReportTemplateHelper.AddStringParameter(report, "TotalCreditLimit", "Tổng hạn mức công nợ");

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
            "DANH MỤC KHÁCH HÀNG", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = MasterCustomersReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng số khách hàng: [ItemCount]  ·  Tổng hạn mức công nợ: [TotalCreditLimit] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Kế toán: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
