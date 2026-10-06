using System.Windows;
using System.Windows.Documents;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class SalesReceiptPrintServiceTests
{
    [Fact]
    public void Short_receipt_fits_one_a4_page() =>
        RunSta(() =>
        {
            var service = new SalesReceiptPrintService();
            var receipt = BuildReceipt(saleLines: 2, oldGoldLines: 1);
            var document = service.CreateDocument(receipt);
            var paginator = service.CreatePaginator(receipt);

            Assert.Equal(SalesReceiptPrintService.A4WidthDip, document.PageWidth);
            Assert.Equal(SalesReceiptPrintService.A4HeightDip, document.PageHeight);
            Assert.Equal(new Size(SalesReceiptPrintService.A4WidthDip, SalesReceiptPrintService.A4HeightDip), paginator.PageSize);
            Assert.Equal(1, paginator.PageCount);
            Assert.Contains(document.Blocks.OfType<Paragraph>(), p => p.KeepTogether);
            Assert.True(HasTableHeaderGroup(document));
        });

    [Fact]
    public void Long_receipt_paginates_with_a4_page_size_and_table_headers() =>
        RunSta(() =>
        {
            var service = new SalesReceiptPrintService();
            var receipt = BuildReceipt(saleLines: 80, oldGoldLines: 0);
            var document = service.CreateDocument(receipt);
            var paginator = service.CreatePaginator(receipt);

            Assert.Equal(SalesReceiptPrintService.A4WidthDip, document.PageWidth);
            Assert.Equal(SalesReceiptPrintService.A4HeightDip, document.PageHeight);
            Assert.True(paginator.PageCount > 1, $"Expected multi-page receipt, got {paginator.PageCount}.");
            Assert.True(HasTableHeaderGroup(document));

            for (var page = 0; page < paginator.PageCount; page++)
            {
                var visual = paginator.GetPage(page);
                Assert.NotNull(visual);
                Assert.Equal(SalesReceiptPrintService.A4WidthDip, visual.Size.Width, 1);
                Assert.Equal(SalesReceiptPrintService.A4HeightDip, visual.Size.Height, 1);
            }

            Assert.Contains(document.Blocks.OfType<Paragraph>(), p => p.KeepTogether);
        });

    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
            throw captured;
    }

    private static bool HasTableHeaderGroup(FlowDocument document)
    {
        foreach (var table in document.Blocks.OfType<Table>())
        {
            if (table.RowGroups.Count >= 2 && table.RowGroups[0].Rows.Count == 1)
                return true;
        }

        return false;
    }

    private static SalesReceiptDto BuildReceipt(int saleLines, int oldGoldLines)
    {
        var sales = new List<SalesLineSnapshot>();
        for (var i = 0; i < saleLines; i++)
        {
            var id = i + 1;
            var draft = new SalesLineDraft(id, 111m, 25m, 8m, null);
            var amounts = new SalesLineAmounts(id, 277.5m, 25m, 8m, 310.5m, false);
            sales.Add(new SalesLineSnapshot(
                id, $"SP{i:000}", $"Sản phẩm demo {i}", 2.5m,
                111m, 25m, 8m, draft, amounts));
        }

        var oldGold = new List<OldGoldLineSnapshot>();
        for (var i = 0; i < oldGoldLines; i++)
        {
            var draft = new OldGoldLineDraft(10 + i, 1, 1.2m, 0.1m, 0, 100, 77m, null, null);
            oldGold.Add(new OldGoldLineSnapshot(
                10 + i, $"Vàng cũ {i}", 77m, draft, new OldGoldLineAmounts(1.1m, 1.1m, 84.7m)));
        }

        var allocations = new List<PaymentAllocationDraft>
        {
            new(PaymentMethods.Cash, 100m, 1, null, null),
            new(PaymentMethods.BankTransfer, 211m, null, 2, "CK-1")
        };

        var saleGross = sales.Sum(x => x.Amounts.TotalAmountVnd);
        var oldTotal = oldGold.Sum(x => x.Amounts.AmountVnd);
        var offset = Math.Min(saleGross, oldTotal);
        var net = saleGross - oldTotal;
        var totals = new SalesCheckoutTotals(
            saleGross, 0, saleGross, oldTotal, offset, net,
            net > 0 ? net : 0, net < 0 ? -net : 0, 0);

        var detail = new SalesCheckoutDetail(
            99,
            "MB26/00099",
            oldGoldLines > 0 && saleLines > 0 ? SalesCheckoutTypes.TradeIn : SalesCheckoutTypes.Sale,
            new DateTime(2026, 10, 6),
            1,
            "Khách demo",
            "0901000000",
            2,
            "NV Demo",
            2,
            "Thu ngân Demo",
            5,
            6,
            sales,
            oldGold,
            allocations,
            totals,
            "Ghi chú demo");

        return new SalesReceiptDto("Công ty KAZUKO", "Chi nhánh trung tâm", "1 Đường A", "0281234567", detail);
    }
}
