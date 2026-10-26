using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Printing;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Production;

public sealed class ProductionPrintService
{
    public void PrintMaterialIssue(ProductionMaterialIssuePrintDto dto)
        => ShowPrint(CreateMaterialIssueDocument(dto));

    internal static FlowDocument CreateMaterialIssueDocument(ProductionMaterialIssuePrintDto dto)
    {
        var doc = CreateBase($"Phiếu giao nguyên liệu — {dto.OrderCode}");
        AddMeta(doc, dto.DocumentCode, dto.DocumentDate, dto.BranchName, dto.DeliveredByName, dto.ReceivedByName);
        var table = CreateTable("STT", "Mã NVL", "Tên", "SL", "ĐVT", "Đơn giá", "Ghi chú");
        foreach (var line in dto.Lines)
            AddRow(table, line.LineNumber.ToString(), line.MaterialCode, line.MaterialName,
                FormatQty(line.Quantity), line.UnitCode, FormatMoney(line.UnitCost), line.Notes ?? string.Empty);
        doc.Blocks.Add(table);
        AddSignatures(doc);
        return doc;
    }

    public void PrintReturn(ProductionReturnPrintDto dto)
    {
        var doc = CreateBase($"Phiếu thợ giao trả — {dto.OrderCode}");
        AddMeta(doc, dto.DocumentCode, dto.DocumentDate, dto.BranchName, dto.DeliveredByName, dto.ReceivedByName);
        var table = CreateTable("Mã", "Tên", "Đã giao", "Vào TP", "Trả lại", "Phế liệu", "Hao hụt", "ĐVT");
        foreach (var line in dto.Lines)
            AddRow(table, line.MaterialCode, line.MaterialName,
                FormatQty(line.QuantityIssued), FormatQty(line.QuantityInProduct),
                FormatQty(line.QuantityReturned), FormatQty(line.QuantityScrap),
                FormatQty(line.QuantityLoss), line.UnitCode);
        doc.Blocks.Add(table);
        AddSignatures(doc);
        ShowPrint(doc);
    }

    public void PrintQc(ProductionQcPrintDto dto)
    {
        var doc = CreateBase($"Phiếu QC — {dto.OrderCode}");
        doc.Blocks.Add(new Paragraph(new Run($"Chi nhánh: {dto.BranchName}  |  In lúc: {dto.PrintedAt:dd/MM/yyyy HH:mm}")));
        var table = CreateTable("Mẫu", "Món", "Trạng thái", "TL vàng", "ĐVT", "Tuổi", "Tiền công", "Lỗi");
        foreach (var unit in dto.Units)
            AddRow(table, unit.ModelName, unit.UnitNumber.ToString(), unit.StatusDisplay,
                unit.ActualGoldWeight?.ToString("0.####") ?? string.Empty,
                unit.WeightUnit ?? string.Empty, unit.GoldPurityCode ?? string.Empty,
                FormatMoney(unit.LaborCost), unit.DefectNotes ?? string.Empty);
        doc.Blocks.Add(table);
        AddSignatures(doc);
        ShowPrint(doc);
    }

    public void PrintFinishedReceipt(ProductionFinishedReceiptPrintDto dto)
    {
        var doc = CreateBase($"Phiếu nhập kho thành phẩm — {dto.OrderCode}");
        AddMeta(doc, dto.DocumentCode, dto.DocumentDate, dto.BranchName, null, null);
        doc.Blocks.Add(new Paragraph(new Run($"Kho nhận: {dto.FinishedLocationName}")));
        var table = CreateTable("Serial", "Mẫu", "TL vàng", "Giá vàng", "Tiền công", "Đá/PK", "Tổng");
        foreach (var line in dto.Lines)
            AddRow(table, line.SerialCode, line.ModelName, FormatQty(line.GoldWeight),
                FormatMoney(line.MaterialCost), FormatMoney(line.LaborCost),
                FormatMoney(line.NonGoldCost), FormatMoney(line.TotalCost));
        doc.Blocks.Add(table);
        AddSignatures(doc);
        ShowPrint(doc);
    }

    public void PrintOrder(ProductionOrderPrintDto dto)
        => ShowPrint(CreateOrderDocument(dto));

    internal static FlowDocument CreateOrderDocument(ProductionOrderPrintDto dto)
    {
        var doc = CreateBase($"Phiếu sản xuất — {dto.OrderCode}");
        doc.Blocks.Add(new Paragraph(new Run(
            $"Ngày lập: {dto.OrderDate:dd/MM/yyyy}  |  Ngày giao NVL: {dto.MaterialIssueDate:dd/MM/yyyy}  |  Hạn hoàn thành: {dto.DueDate:dd/MM/yyyy}")));
        doc.Blocks.Add(new Paragraph(new Run(
            $"Trạng thái: {dto.StatusDisplay}  |  Chi nhánh: {dto.BranchName}")));
        if (!string.IsNullOrWhiteSpace(dto.CustomerName))
            doc.Blocks.Add(new Paragraph(new Run($"Khách hàng: {dto.CustomerName}")));
        if (!string.IsNullOrWhiteSpace(dto.SalesOrderCode))
            doc.Blocks.Add(new Paragraph(new Run($"Đơn đặt hàng: {dto.SalesOrderCode}")));
        if (!string.IsNullOrWhiteSpace(dto.WorkerDisplayName))
            doc.Blocks.Add(new Paragraph(new Run($"Thợ sản xuất: {dto.WorkerDisplayName}")));
        if (!string.IsNullOrWhiteSpace(dto.Notes))
            doc.Blocks.Add(new Paragraph(new Run($"Ghi chú: {dto.Notes}")));

        var table = CreateTable("STT", "Sản phẩm", "SL", "TL vàng", "Tuổi vàng", "Tiền công");
        foreach (var line in dto.Lines)
            AddRow(table, line.LineNumber.ToString(), line.ModelName,
                line.PlannedQuantity.ToString(),
                line.GoldWeight?.ToString("0.####") ?? string.Empty,
                line.GoldPurityCode ?? string.Empty,
                FormatMoney(line.LaborCost));
        doc.Blocks.Add(table);
        AddSignatures(doc);
        return doc;
    }

    public void PrintGoldSettlement(ProductionGoldSettlementPrintDto dto)
    {
        var doc = CreateBase($"Biên bản quyết toán vàng — {dto.OrderCode}");
        doc.Blocks.Add(new Paragraph(new Run($"Chi nhánh: {dto.BranchName}  |  In lúc: {dto.PrintedAt:dd/MM/yyyy HH:mm}")));
        var table = CreateTable("Mã", "Tên", "Đã giao", "Vào TP", "Trả lại", "Phế liệu", "Hao hụt", "ĐVT");
        foreach (var line in dto.BalanceLines)
            AddRow(table, line.MaterialCode, line.MaterialName,
                FormatQty(line.QuantityIssued), FormatQty(line.QuantityInProduct),
                FormatQty(line.QuantityReturned), FormatQty(line.QuantityScrap),
                FormatQty(line.QuantityLoss), line.UnitCode);
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph(new Run(
            $"Tổng tiền công: {FormatMoney(dto.TotalLaborCostVnd)}  |  Sinh công nợ: {(dto.CreatesPayableDebt ? "Có" : "Không")}")));
        AddSignatures(doc);
        ShowPrint(doc);
    }

    private static FlowDocument CreateBase(string title)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(40),
            PageWidth = A4Width,
            PageHeight = A4Height,
            ColumnWidth = A4Width - 80,
            ColumnGap = 0,
            // Paper documents use ink colours, independent of app/Windows dark themes.
            Foreground = Brushes.Black,
            Background = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12
        };
        doc.Blocks.Add(new Paragraph(new Bold(new Run(title))) { FontSize = 16, Margin = new Thickness(0, 0, 0, 12) });
        return doc;
    }

    private static void AddMeta(
        FlowDocument doc,
        string documentCode,
        DateTime date,
        string branchName,
        string? deliveredBy,
        string? receivedBy)
    {
        doc.Blocks.Add(new Paragraph(new Run(
            $"Chứng từ: {documentCode}  |  Ngày: {date:dd/MM/yyyy}  |  Chi nhánh: {branchName}")));
        if (!string.IsNullOrWhiteSpace(deliveredBy) || !string.IsNullOrWhiteSpace(receivedBy))
        {
            doc.Blocks.Add(new Paragraph(new Run(
                $"Người giao: {deliveredBy ?? "-"}  |  Người nhận: {receivedBy ?? "-"}")));
        }
    }

    private static Table CreateTable(params string[] headers)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
        for (var i = 0; i < headers.Length; i++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var headerRow = new TableRow();
        foreach (var header in headers)
            headerRow.Cells.Add(HeaderCell(header));
        group.Rows.Add(headerRow);
        return table;
    }

    private static void AddRow(Table table, params string[] cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
            row.Cells.Add(BodyCell(cell));
        table.RowGroups[0].Rows.Add(row);
    }

    private static TableCell HeaderCell(string text) =>
        new(new Paragraph(new Bold(new Run(text))))
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4)
        };

    private static TableCell BodyCell(string text) =>
        new(new Paragraph(new Run(text)))
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4)
        };

    private static void AddSignatures(FlowDocument doc)
    {
        doc.Blocks.Add(new Paragraph(new Run(" ")) { Margin = new Thickness(0, 24, 0, 0) });
        doc.Blocks.Add(new Paragraph(new Run(
            "Người lập: .....................     Người giao: .....................     Người nhận: .....................")));
    }

    private static void ShowPrint(FlowDocument document)
    {
        var dialog = new PrintDialog();
        var ticket = dialog.PrintTicket?.Clone() ?? new PrintTicket();
        // Do not pass unsupported A4 settings to a label driver's native dialog.
        // The user can choose their A4 printer in the dialog; validate before submitting.
        if (dialog.PrintQueue?.GetPrintCapabilities().PageMediaSizeCapability.Any(IsA4) == true)
        {
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;
        }
        if (dialog.ShowDialog() != true)
            return;
        var queue = dialog.PrintQueue ?? throw new InvalidOperationException("Chưa chọn máy in phiếu sản xuất.");
        var capabilities = queue.GetPrintCapabilities();
        if (!capabilities.PageMediaSizeCapability.Any(IsA4))
            throw new InvalidOperationException($"Máy in '{queue.Name}' không hỗ trợ giấy A4. Hãy chọn máy in A4 hoặc Microsoft Print to PDF, không chọn máy in tem.");
        ticket = (dialog.PrintTicket ?? queue.DefaultPrintTicket ?? new PrintTicket()).Clone();
        ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        ticket.PageOrientation = PageOrientation.Portrait;
        dialog.PrintTicket = queue.MergeAndValidatePrintTicket(dialog.PrintTicket, ticket).ValidatedPrintTicket;
        if (!IsA4(dialog.PrintTicket.PageMediaSize) || dialog.PrintTicket.PageOrientation == PageOrientation.Landscape)
            throw new InvalidOperationException("Driver không chấp nhận khổ A4 dọc. Kiểm tra khổ giấy của máy in rồi thử lại.");
        var area = queue.GetPrintCapabilities(dialog.PrintTicket).PageImageableArea;
        var printable = area is null ? new Rect(40,40,A4Width-80,A4Height-80)
            : new Rect(area.OriginWidth,area.OriginHeight,area.ExtentWidth,area.ExtentHeight);
        var paginator = PreparePaginator(document, new Size(A4Width,A4Height), printable);
        dialog.PrintDocument(paginator, "In sản xuất");
    }

    internal const double A4Width = 210d / 25.4 * 96;
    internal const double A4Height = 297d / 25.4 * 96;

    internal static bool IsA4(PageMediaSize? media) => media is not null &&
        (media.PageMediaSizeName == PageMediaSizeName.ISOA4 ||
         (media.Width is { } w && media.Height is { } h &&
          ((Math.Abs(w-A4Width)<3 && Math.Abs(h-A4Height)<3) || (Math.Abs(h-A4Width)<3 && Math.Abs(w-A4Height)<3))));

    internal static DocumentPaginator PreparePaginator(FlowDocument document, Size paper, Rect printable)
    {
        if (Math.Abs(paper.Width-A4Width)>3 || Math.Abs(paper.Height-A4Height)>3)
            throw new InvalidOperationException("Phiếu sản xuất yêu cầu khổ A4 dọc, không dùng khổ tem.");
        if (printable.IsEmpty || !double.IsFinite(printable.Width) || !double.IsFinite(printable.Height)
            || printable.Width<300 || printable.Height<400)
            throw new InvalidOperationException("Vùng in quá nhỏ cho phiếu A4. Kiểm tra máy in và khổ giấy.");
        document.PageWidth = paper.Width;
        document.PageHeight = paper.Height;
        document.PagePadding = new Thickness(Math.Max(40,printable.Left),Math.Max(40,printable.Top),
            Math.Max(40,paper.Width-printable.Right),Math.Max(40,paper.Height-printable.Bottom));
        if (paper.Width-document.PagePadding.Left-document.PagePadding.Right<200
            || paper.Height-document.PagePadding.Top-document.PagePadding.Bottom<300)
            throw new InvalidOperationException("Lề/vùng in không phù hợp với A4. Kiểm tra thiết lập máy in.");
        document.ColumnWidth = paper.Width-document.PagePadding.Left-document.PagePadding.Right;
        document.ColumnGap = 0;
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = paper;
        if (paginator is DynamicDocumentPaginator dynamicPaginator) dynamicPaginator.IsBackgroundPaginationEnabled = false;
        paginator.ComputePageCount();
        if (paginator.PageCount<1) throw new InvalidOperationException("Không tạo được trang in sản xuất.");
        return paginator;
    }

    private static string FormatMoney(decimal value) => value.ToString("N0");
    private static string FormatQty(decimal value) => value.ToString("0.####");
}
