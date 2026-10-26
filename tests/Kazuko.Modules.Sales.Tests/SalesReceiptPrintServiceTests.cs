using System.IO;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Finance;
using Kazuko.Core.Printing;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class SalesReceiptPrintServiceTests
{
    [Fact]
    public void BuildLineTable_maps_sale_and_old_gold_with_lade_filter()
    {
        var receipt = BuildReceipt(saleLines: 2, oldGoldLines: 1);
        var table = SalesReceiptPrintService.BuildLineTable(receipt);
        var sales = SalesReceiptPrintService.BuildSaleLineTable(receipt);
        var components = SalesReceiptPrintService.BuildComponentTable(receipt);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(2, sales.Rows.Count);
        Assert.Equal(1, components.Rows.Count);
        Assert.Equal(0, table.Rows[0]["LADE"]);
        Assert.Equal(0, table.Rows[1]["LADE"]);
        Assert.Equal(30, table.Rows[2]["LADE"]);
        Assert.Equal("SP000", table.Rows[0]["MAVACH"]);
        Assert.Equal("Sản phẩm demo 0", table.Rows[0]["MATHANG"]);
        Assert.Equal("9999", table.Rows[0]["LOAIVANG"]);
        Assert.Equal(310.5m, table.Rows[0]["THANHTIEN"]);
        Assert.Equal("Vàng cũ 0", table.Rows[2]["MATHANG"]);
        Assert.Equal("9999", table.Rows[2]["LOAIVANG"]);
        Assert.Equal(84.7m, table.Rows[2]["THANHTIEN"]);
        Assert.True(table.Columns.Contains("VANGTHUCTE_CHUOI"));
        Assert.True(table.Columns.Contains("LADE"));
    }

    [Fact]
    public void BuildComponentTable_uses_sale_lines_when_no_old_gold()
    {
        var receipt = BuildReceipt(saleLines: 1, oldGoldLines: 0);
        var components = SalesReceiptPrintService.BuildComponentTable(receipt);

        Assert.Single(components.Rows);
        Assert.Equal("9999", components.Rows[0]["LOAIVANG"]);
        Assert.Equal(277.5m, components.Rows[0]["THANHTIEN"]);
    }

    [Theory]
    [InlineData("phoi_a5_ngang.frx", SalesReceiptOrientation.Landscape)]
    [InlineData("phoi_a5_doc.frx", SalesReceiptOrientation.Portrait)]
    public async Task CreatePreparedReportAsync_prepares_a5_templates_without_compiler_errors(
        string fileName,
        SalesReceiptOrientation orientation)
    {
        var frxPath = FindExampleFrx(fileName);
        Assert.True(File.Exists(frxPath), $"Missing template: {frxPath}");

        var store = new StubPrintTemplateStore(await File.ReadAllBytesAsync(frxPath));
        var service = new SalesReceiptPrintService(store);
        var session = new AuthenticatedSession(1, "admin", "Admin", "ADMIN", 1, "BR", "Branch");

        using var report = await service.CreatePreparedReportAsync(
            BuildReceipt(1, 1), session, orientation);
        Assert.NotNull(report);
        Assert.True(report.PreparedPages.Count > 0);
    }

    private static string FindExampleFrx(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Assets", "examples", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Assets", "examples", fileName));
    }

    [Theory]
    [InlineData("phoi_a5_ngang.frx", PrintTemplateCodes.SalesReceiptA5)]
    [InlineData("phoi_a5_doc.frx", PrintTemplateCodes.SalesReceiptA5Portrait)]
    public async Task Settings_preview_renders_saved_bill_as_png(string fileName, string code)
    {
        var store = new StubPrintTemplateStore(await File.ReadAllBytesAsync(FindExampleFrx(fileName)));
        var service = new ReceiptTemplatePreviewService(new SalesReceiptPrintService(store), new PreviewCompany());
        var bytes = await service.CreatePreviewAsync(code,
            new AuthenticatedSession(1, "admin", "Admin", "ADMIN", 1, "BR", "Branch"));
        using var stream = new MemoryStream(bytes);
        using var image = System.Drawing.Image.FromStream(stream);
        Assert.Equal(System.Drawing.Imaging.ImageFormat.Png.Guid, image.RawFormat.Guid);
        Assert.True(image.Width > 100);
        Assert.True(image.Height > 100);
    }

    private sealed class PreviewCompany : ICompanyProfileService
    {
        public Task<CompanyProfileDto> GetAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompanyProfileDto(1, "KAZUKO", "0901234567", "Địa chỉ mẫu", null, null));
        public Task SaveAsync(CompanyProfileUpdateRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPrintTemplateStore : IPrintTemplateStore
    {
        private readonly byte[] _content;

        public StubPrintTemplateStore(byte[] content) => _content = content;

        public Task<PrintTemplateDto> GetByCodeAsync(
            string code,
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintTemplateDto(
                1, code, "Bill A5", _content, "test", DateTime.UtcNow, null));

        public Task<PrintTemplateDto> SaveAsync(
            string code,
            string name,
            byte[] content,
            string? notes,
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void VietnameseMoneyText_reads_common_amounts()
    {
        Assert.Contains("đồng", VietnameseMoneyText.ToDong(0), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Một", VietnameseMoneyText.ToDong(1));
        var million = VietnameseMoneyText.ToDong(1_000_000m);
        Assert.Contains("triệu", million, StringComparison.OrdinalIgnoreCase);
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
                111m, 25m, 8m, draft, amounts,
                GoldPurity: "9999",
                StoneWeightChi: 0.1m,
                TotalWeightChi: 2.6m));
        }

        var oldGold = new List<OldGoldLineSnapshot>();
        for (var i = 0; i < oldGoldLines; i++)
        {
            var draft = new OldGoldLineDraft(10 + i, 1, 1.2m, 0.1m, 0, 100, 77m, null, null);
            oldGold.Add(new OldGoldLineSnapshot(
                10 + i, $"Vàng cũ {i}", 77m, draft, new OldGoldLineAmounts(1.1m, 1.1m, 84.7m),
                MaterialCode: "9999"));
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
