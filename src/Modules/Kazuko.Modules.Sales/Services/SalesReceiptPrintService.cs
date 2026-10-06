using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Services;

/// <summary>
/// Builds an A4 FlowDocument from an immutable sales receipt snapshot.
/// Page size is fixed at 96 DPI A4; does not read live price tables.
/// </summary>
public sealed class SalesReceiptPrintService
{
    public const double A4WidthDip = 793.7;
    public const double A4HeightDip = 1122.5;

    public FlowDocument CreateDocument(SalesReceiptDto receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Checkout);

        var checkout = receipt.Checkout;
        var doc = new FlowDocument
        {
            PageWidth = A4WidthDip,
            PageHeight = A4HeightDip,
            PagePadding = new Thickness(48, 40, 48, 40),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            ColumnWidth = A4WidthDip
        };

        doc.Blocks.Add(new Paragraph(new Bold(new Run(receipt.CompanyName)))
        {
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 2),
            TextAlignment = TextAlignment.Center
        });
        doc.Blocks.Add(new Paragraph(new Run(receipt.BranchName))
        {
            Margin = new Thickness(0, 0, 0, 2),
            TextAlignment = TextAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(receipt.BranchAddress) || !string.IsNullOrWhiteSpace(receipt.BranchPhone))
        {
            doc.Blocks.Add(new Paragraph(new Run(
                $"{receipt.BranchAddress ?? string.Empty}" +
                (string.IsNullOrWhiteSpace(receipt.BranchAddress) || string.IsNullOrWhiteSpace(receipt.BranchPhone)
                    ? string.Empty
                    : "  |  ") +
                (string.IsNullOrWhiteSpace(receipt.BranchPhone) ? string.Empty : $"ĐT: {receipt.BranchPhone}")))
            {
                Margin = new Thickness(0, 0, 0, 8),
                TextAlignment = TextAlignment.Center,
                FontSize = 11
            });
        }

        doc.Blocks.Add(new Paragraph(new Bold(new Run(CheckoutTitle(checkout.CheckoutType))))
        {
            FontSize = 15,
            Margin = new Thickness(0, 4, 0, 8),
            TextAlignment = TextAlignment.Center
        });

        doc.Blocks.Add(MetaParagraph(
            $"Số: {checkout.DocumentCode}  |  Ngày: {checkout.DocumentDate:dd/MM/yyyy}  |  Loại: {checkout.CheckoutType}"));
        doc.Blocks.Add(MetaParagraph(
            $"Khách: {checkout.CustomerName}" +
            (string.IsNullOrWhiteSpace(checkout.CustomerPhone) ? string.Empty : $" ({checkout.CustomerPhone})")));
        doc.Blocks.Add(MetaParagraph(
            $"NV bán: {checkout.SalespersonName}  |  Thu ngân: {checkout.CreatedByDisplayName}"));

        if (checkout.SaleLines.Count > 0)
        {
            doc.Blocks.Add(SectionTitle("Hàng bán"));
            var saleTable = CreateTable(
                "STT", "Mã", "Tên", "TL vàng", "Giá vàng", "Công", "Đá", "Thành tiền");
            var index = 1;
            foreach (var line in checkout.SaleLines)
            {
                AddRow(saleTable,
                    index.ToString(CultureInfo.InvariantCulture),
                    line.ProductCode,
                    line.ProductName,
                    FormatQty(line.GoldWeightChi),
                    FormatMoney(line.AppliedPrices.AppliedGoldPricePerChi),
                    FormatMoney(line.Amounts.LaborAmountVnd),
                    FormatMoney(line.Amounts.StoneAmountVnd),
                    FormatMoney(line.Amounts.TotalAmountVnd));
                index++;
            }

            doc.Blocks.Add(saleTable);
        }

        if (checkout.OldGoldLines.Count > 0)
        {
            doc.Blocks.Add(SectionTitle("Vàng cũ"));
            var oldTable = CreateTable(
                "STT", "Nguyên liệu", "TL", "TL quy", "%", "Giá mua", "Thành tiền");
            var index = 1;
            foreach (var line in checkout.OldGoldLines)
            {
                AddRow(oldTable,
                    index.ToString(CultureInfo.InvariantCulture),
                    line.MaterialName,
                    FormatQty(line.Draft.TotalWeightChi),
                    FormatQty(line.Amounts.ConvertedWeightChi),
                    FormatQty(line.Draft.ValuePercent),
                    FormatMoney(line.Draft.AppliedBuyPricePerChi),
                    FormatMoney(line.Amounts.AmountVnd));
                index++;
            }

            doc.Blocks.Add(oldTable);
        }

        if (checkout.Allocations.Count > 0)
        {
            doc.Blocks.Add(SectionTitle("Thanh toán"));
            var payTable = CreateTable("STT", "Phương thức", "Số tiền", "Tham chiếu");
            var index = 1;
            foreach (var allocation in checkout.Allocations)
            {
                AddRow(payTable,
                    index.ToString(CultureInfo.InvariantCulture),
                    MethodLabel(allocation.PaymentMethod),
                    FormatMoney(allocation.AmountVnd),
                    allocation.ReferenceNumber ?? string.Empty);
                index++;
            }

            doc.Blocks.Add(payTable);
        }

        var totalsIntro = SectionTitle("Tổng kết");
        totalsIntro.KeepWithNext = true;
        doc.Blocks.Add(totalsIntro);
        AddKeptParagraph(doc, $"Doanh thu hàng: {FormatMoney(checkout.Totals.SaleGrossVnd)}");
        AddKeptParagraph(doc, $"Chiết khấu: {FormatMoney(checkout.Totals.DiscountAmountVnd)}");
        AddKeptParagraph(doc, $"Vàng cũ: {FormatMoney(checkout.Totals.OldGoldTotalVnd)}");
        AddKeptParagraph(doc, $"Bù trừ: {FormatMoney(checkout.Totals.OffsetAmountVnd)}");
        doc.Blocks.Add(new Paragraph(new Bold(new Run($"Thanh toán net: {FormatMoney(checkout.Totals.NetAmountVnd)}")))
        {
            Margin = new Thickness(0, 4, 0, 2),
            KeepTogether = true,
            KeepWithNext = true
        });
        if (checkout.Totals.ReceivableAmountVnd > 0)
            AddKeptParagraph(doc, $"Công nợ phải thu: {FormatMoney(checkout.Totals.ReceivableAmountVnd)}");
        if (checkout.Totals.PayoutAmountVnd > 0)
            AddKeptParagraph(doc, $"Chi khách: {FormatMoney(checkout.Totals.PayoutAmountVnd)}");
        if (!string.IsNullOrWhiteSpace(checkout.Notes))
            AddKeptParagraph(doc, $"Ghi chú: {checkout.Notes}");

        doc.Blocks.Add(new Paragraph(new Run(" "))
        {
            Margin = new Thickness(0, 20, 0, 0),
            KeepTogether = true,
            KeepWithNext = true
        });
        doc.Blocks.Add(new Paragraph(new Run(
            "Khách hàng                    Thu ngân                    Quản lý"))
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
            KeepTogether = true,
            KeepWithNext = true
        });
        doc.Blocks.Add(new Paragraph(new Run(
            "..................             ..................             .................."))
        {
            TextAlignment = TextAlignment.Center,
            KeepTogether = true
        });

        return doc;
    }

    public DocumentPaginator CreatePaginator(SalesReceiptDto receipt)
    {
        var document = CreateDocument(receipt);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(A4WidthDip, A4HeightDip);
        if (paginator is DynamicDocumentPaginator dynamicPaginator)
            dynamicPaginator.ComputePageCount();
        return paginator;
    }

    private static string CheckoutTitle(string checkoutType) => checkoutType switch
    {
        SalesCheckoutTypes.TradeIn => "PHIẾU BÁN KÈM THU ĐỔI VÀNG CŨ",
        SalesCheckoutTypes.OldGoldPurchase => "PHIẾU MUA VÀNG CŨ",
        _ => "PHIẾU BÁN HÀNG"
    };

    private static Paragraph SectionTitle(string text) =>
        new(new Bold(new Run(text))) { Margin = new Thickness(0, 12, 0, 4) };

    private static Paragraph MetaParagraph(string text) =>
        new(new Run(text)) { Margin = new Thickness(0, 0, 0, 2) };

    private static void AddKeptParagraph(FlowDocument doc, string text)
    {
        doc.Blocks.Add(new Paragraph(new Run(text))
        {
            Margin = new Thickness(0, 0, 0, 2),
            KeepTogether = true,
            KeepWithNext = true
        });
    }

    private static Table CreateTable(params string[] headers)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 8)
        };
        foreach (var _ in headers)
            table.Columns.Add(new TableColumn());

        // First row group acts as the repeating table header for paginator consumers.
        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow();
        foreach (var header in headers)
            headerRow.Cells.Add(HeaderCell(header));
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static void AddRow(Table table, params string[] cells)
    {
        var body = table.RowGroups[^1];
        var row = new TableRow();
        foreach (var cell in cells)
            row.Cells.Add(BodyCell(cell));
        body.Rows.Add(row);
    }

    private static TableCell HeaderCell(string text) =>
        new(new Paragraph(new Bold(new Run(text))))
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(3),
            Background = Brushes.WhiteSmoke
        };

    private static TableCell BodyCell(string text) =>
        new(new Paragraph(new Run(text)))
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(3)
        };

    private static string MethodLabel(string method) => method switch
    {
        PaymentMethods.Cash => "Tiền mặt",
        PaymentMethods.BankTransfer => "Chuyển khoản",
        PaymentMethods.Card => "Thẻ",
        _ => method
    };

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));

    private static string FormatQty(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
