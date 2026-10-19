using System.IO;
using System.Data;
using System.Globalization;
using FastReport;
using FastReport.Barcode;
using Kazuko.Core.Authentication;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// In tem nữ trang 28x12 cho sản phẩm nhập kho thành phẩm.
/// Template lấy từ PRINT_TEMPLATES (mã TEM_NU_TRANG_28x12), dữ liệu tự động từ thông tin SP.
/// </summary>
public sealed class FinishedGoodsLabelPrintService
{
    public const float PaperWidthMm = 86f;
    public const float PaperHeightMm = 12f;
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IPrintTemplateStore _templates;

    public FinishedGoodsLabelPrintService(IPrintTemplateStore templates)
    {
        _templates = templates;
    }

    public async Task<Report> CreatePreparedReportAsync(
        IReadOnlyList<ManualFinishedProductItem> items,
        DateTime receiptDate,
        AuthenticatedSession session,
        string shopName,
        string shopAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(session);

        var template = await _templates.GetByCodeAsync(
            PrintTemplateCodes.JewelryLabel28x12, session, cancellationToken);

        var report = new Report();
        using (var stream = new MemoryStream(template.Content))
            report.Load(stream);

        foreach (var page in report.Pages.OfType<ReportPage>())
        {
            page.Landscape = false;
            page.PaperWidth = PaperWidthMm;
            page.PaperHeight = PaperHeightMm;
            page.LeftMargin = page.TopMargin = page.RightMargin = page.BottomMargin = 0;
        }

        var table = BuildLabelTable(items, receiptDate);
        RegisterTable(report, table, "DataSet");

        SetParameter(report, "Tên tiệm", shopName);
        SetParameter(report, "DiaChiDoanhNghiep", shopAddress);

        report.Prepare();
        return report;
    }

    private static DataTable BuildLabelTable(
        IReadOnlyList<ManualFinishedProductItem> items, DateTime receiptDate)
    {
        var table = new DataTable();
        table.Columns.Add("NGAYNHAP", typeof(DateTime));
        table.Columns.Add("MAVACH", typeof(string));
        table.Columns.Add("THANHTIEN", typeof(decimal));
        table.Columns.Add("TUOI", typeof(decimal));
        table.Columns.Add("DONGIA", typeof(decimal));
        table.Columns.Add("LOAIVANG", typeof(string));
        table.Columns.Add("QUAY", typeof(string));
        table.Columns.Add("CONGNHAP", typeof(decimal));
        table.Columns.Add("CONGBAN", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT_LI", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_CHUOI", typeof(string));
        table.Columns.Add("DMATHANGID", typeof(string));
        table.Columns.Add("TENHANG", typeof(string));
        table.Columns.Add("NOTE", typeof(string));
        table.Columns.Add("CONGNHAP_NGHIN", typeof(int));
        table.Columns.Add("CONGBAN_NGHIN", typeof(int));
        table.Columns.Add("NHACUNGCAP", typeof(string));
        table.Columns.Add("TRONGLUONGVANG_CHUOI2", typeof(string));
        table.Columns.Add("THUONGHIEU", typeof(string));
        table.Columns.Add("MANHACUNGCAP", typeof(string));
        table.Columns.Add("KYHIEU", typeof(string));
        table.Columns.Add("CONGSI", typeof(decimal));
        table.Columns.Add("CONGSI_NGHIN", typeof(int));
        table.Columns.Add("TONGTRONGLUONG_DEM", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG_DEM", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT_DEM", typeof(decimal));
        table.Columns.Add("TONGTRONGLUONG_CHUOI", typeof(string));
        table.Columns.Add("TRONGLUONGHOT_CHUOI", typeof(string));
        table.Columns.Add("DIACHINCC", typeof(string));
        table.Columns.Add("TIENDABAN_NGHIN", typeof(int));
        table.Columns.Add("TIENDANHAP_NGHIN", typeof(int));
        table.Columns.Add("GIABAN_NGHIN", typeof(int));
        table.Columns.Add("GIABAN", typeof(decimal));
        table.Columns.Add("TIENDABAN", typeof(decimal));
        table.Columns.Add("TIENDANHAP", typeof(decimal));
        table.Columns.Add("THANHTIENBAN", typeof(decimal));
        table.Columns.Add("MAHIEU", typeof(string));
        table.Columns.Add("TCCS", typeof(string));

        foreach (var item in items)
        {
            var laborSale = item.SaleLaborCost ?? item.CraftLaborCost ?? 0m;
            var laborPurchase = item.PurchaseLaborCost ?? 0m;
            var row = table.NewRow();
            row["NGAYNHAP"] = receiptDate;
            row["MAVACH"] = item.ProductCode;
            row["THANHTIEN"] = item.SalePrice ?? 0m;
            row["TUOI"] = 0m;
            row["DONGIA"] = 0m;
            row["LOAIVANG"] = item.GoldPurityCode ?? string.Empty;
            row["QUAY"] = string.Empty;
            row["CONGNHAP"] = laborPurchase;
            row["CONGBAN"] = laborSale;
            row["TONGTRONGLUONG"] = item.TotalWeight;
            row["TRONGLUONGVANG"] = item.GoldWeight;
            row["TRONGLUONGHOT"] = item.StoneWeight;
            row["TONGTRONGLUONG_LI"] = 0m;
            row["TRONGLUONGVANG_LI"] = 0m;
            row["TRONGLUONGHOT_LI"] = 0m;
            row["TRONGLUONGVANG_CHUOI"] = FormatChi(item.GoldWeight);
            row["DMATHANGID"] = item.ProductGroup ?? string.Empty;
            row["TENHANG"] = item.ProductName;
            row["NOTE"] = item.Notes ?? string.Empty;
            row["CONGNHAP_NGHIN"] = (int)(laborPurchase / 1000m);
            row["CONGBAN_NGHIN"] = (int)(laborSale / 1000m);
            row["NHACUNGCAP"] = item.SupplierName ?? string.Empty;
            row["TRONGLUONGVANG_CHUOI2"] = FormatChi(item.GoldWeight);
            row["THUONGHIEU"] = string.Empty;
            row["MANHACUNGCAP"] = string.Empty;
            row["KYHIEU"] = item.SymbolCode ?? string.Empty;
            row["CONGSI"] = 0m;
            row["CONGSI_NGHIN"] = 0;
            row["TONGTRONGLUONG_DEM"] = 0m;
            row["TRONGLUONGVANG_DEM"] = 0m;
            row["TRONGLUONGHOT_DEM"] = 0m;
            row["TONGTRONGLUONG_CHUOI"] = FormatChi(item.TotalWeight);
            row["TRONGLUONGHOT_CHUOI"] = FormatChi(item.StoneWeight);
            row["DIACHINCC"] = string.Empty;
            row["TIENDABAN_NGHIN"] = 0;
            row["TIENDANHAP_NGHIN"] = 0;
            row["GIABAN_NGHIN"] = (int)((item.SalePrice ?? 0m) / 1000m);
            row["GIABAN"] = item.SalePrice ?? 0m;
            row["TIENDABAN"] = 0m;
            row["TIENDANHAP"] = 0m;
            row["THANHTIENBAN"] = item.SalePrice ?? 0m;
            row["MAHIEU"] = item.ModelCode ?? string.Empty;
            row["TCCS"] = string.Empty;
            table.Rows.Add(row);
        }

        return table;
    }

    private static string FormatChi(decimal weight)
        => weight.ToString("0.####", Vi) + " chỉ";

    internal static void RegisterTable(Report report, DataTable table, string name)
    {
        foreach (var barcode in report.AllObjects.OfType<BarcodeObject>())
        {
            barcode.DataColumn = $"{name}.MAVACH";
            barcode.Expression = string.Empty;
        }
        // Keep band bindings when replacing dictionary stubs from a saved template.
        var bands = report.AllObjects.OfType<DataBand>()
            .Where(band => band.DataSource?.Name == name).ToArray();
        var existing = report.Dictionary.FindByName(name);
        if (existing is not null)
            existing.Dispose();

        report.RegisterData(table, name);
        var dataSource = report.GetDataSource(name);
        if (dataSource is not null)
        {
            dataSource.Enabled = true;
            foreach (var band in bands)
                band.DataSource = dataSource;
        }
    }

    private static void SetParameter(Report report, string name, object value)
    {
        var param = report.Parameters.FindByName(name);
        if (param is not null)
            param.DataType = value.GetType();
        report.SetParameterValue(name, value);
    }
}
