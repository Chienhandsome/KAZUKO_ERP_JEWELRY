using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using FastReport;
using FastReport.Barcode;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;
using Kazuko.Modules.Warehouse.LabelDesigner;
using Kazuko.Modules.Warehouse.Printing;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class LabelTemplateLTests
{
    [Fact]
    public async Task Settings_preview_renders_saved_label_as_png()
    {
        var service = new LabelDesignerService(new TemplateStore(), new CompanyService());
        var bytes = await service.CreatePreviewAsync(Session);
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        Assert.Equal(ImageFormat.Png.Guid, image.RawFormat.Guid);
        Assert.True(image.Width > image.Height);
        Assert.True(image.Height > 20);
    }

    [Theory]
    [InlineData("0", false, "0 chỉ")]
    [InlineData("0", true, "0 chỉ")]
    [InlineData("0.01", false, "0,01 chỉ")]
    [InlineData("0.01", true, "1 ly")]
    [InlineData("0.1", true, "1 phân")]
    [InlineData("0.125", true, "1,25 phân")]
    [InlineData("0.0999", true, "9,99 ly")]
    [InlineData("1.25", true, "1,25 chỉ")]
    public void Weight_is_stored_in_chi_and_only_display_unit_changes(string value, bool compact, string expected)
    {
        Assert.Equal(expected, JewelryLabelData.FormatWeight(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), compact));
    }

    [Fact]
    public void Grams_use_exact_chi_conversion()
    {
        Assert.Equal("0,0375 g", JewelryLabelData.FormatGrams(.01m));
        Assert.Equal("0,375 g", JewelryLabelData.FormatGrams(.1m));
        Assert.Equal("3,75 g", JewelryLabelData.FormatGrams(1m));
        Assert.Equal("0,000375 g", JewelryLabelData.FormatGrams(.0001m));
    }

    [Fact]
    public void Sample_uses_separate_parties_from_general_settings()
    {
        using var table = LabelDesignerSampleData.BuildSampleTable(Profile);
        var row = table.Rows[0];
        Assert.Equal(Profile.DistributorName, row["TEN_NPP"]);
        Assert.Equal(Profile.DistributorAddress, row["DIACHI_NPP"]);
        Assert.Equal(Profile.ManufacturerName, row["TEN_NSX"]);
        Assert.Equal(Profile.ManufacturerAddress, row["DIACHI_NSX"]);
        Assert.Equal(Profile.CountryOfOrigin, row["XUATXU"]);
        Assert.Equal("2,5 chỉ", row["TONGTRONGLUONG_CHUOI"]);
        Assert.Equal("9,375 g", row["TONGTRONGLUONG_GRAM"]);
    }

    [Theory]
    [InlineData(false, "0,01 chỉ")]
    [InlineData(true, "1 ly")]
    public async Task Production_label_resolves_qr_weights_parties_and_keeps_86x12(bool compact, string expectedWeight)
    {
        var store = new TemplateStore();
        var service = new FinishedGoodsLabelPrintService(store);
        var item = new ManualFinishedProductItem("N600050", "OTHER-CODE", "NHẪN TRƠN 9999", null, "9999", null,
            "Chỉ", .01m, 0, .01m, null, null, 0, 0, 0, "KHÔNG LẤY TÊN NCC HÀNG", null, "KZ", null);
        using var report = await service.CreatePreparedReportAsync([item], DateTime.Today, Session, Profile, compact,
            PrintTemplateCodes.JewelryLabelL28x12);
        Assert.Equal(PrintTemplateCodes.JewelryLabelL28x12, store.LastRequestedCode);
        Assert.Equal(1, report.PreparedPages.Count);
        using var page = report.PreparedPages.GetPage(0);
        Assert.Equal(86, page.PaperWidth);
        Assert.Equal(12, page.PaperHeight);
        var texts = page.AllObjects.OfType<TextObject>().ToDictionary(t => t.Name, t => t.Text);
        Assert.Equal("TTL: " + expectedWeight, texts["TotalWeight"]);
        Assert.Equal("0,0375 g", texts["TotalGrams"]);
        Assert.Equal("NPP: " + Profile.DistributorName, texts["Distributor"]);
        Assert.Equal("NSX: " + Profile.ManufacturerName, texts["Manufacturer"]);
        Assert.Equal("Xuất xứ: Vietnam", texts["Origin"]);
        Assert.Equal("Ký hiệu: KZ", texts["Symbol"]);
        var qr = Assert.Single(page.AllObjects.OfType<BarcodeObject>());
        Assert.IsType<BarcodeQR>(qr.Barcode);
        Assert.Equal("N600050", qr.Text);
        var output = Environment.GetEnvironmentVariable("KAZUKO_LABEL_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            using var bitmap = new Bitmap(688, 96);
            bitmap.SetResolution(203, 203);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.PageUnit = GraphicsUnit.Millimeter;
                VectorLabelRenderer.DrawPreparedLabel(graphics, report);
            }
            bitmap.Save(Path.Combine(output, compact ? "label-compact.png" : "label-chi.png"));
            using var zoom = new Bitmap(bitmap.Width * 4, bitmap.Height * 4);
            using (var graphics = Graphics.FromImage(zoom))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, zoom.Width, zoom.Height));
            }
            zoom.Save(Path.Combine(output, compact ? "label-compact-4x.png" : "label-chi-4x.png"), ImageFormat.Png);
        }
    }

    [Fact]
    public async Task Loading_reference_layout_does_not_save_over_existing_template()
    {
        var store = new TemplateStore();
        var vm = new LabelDesignerViewModel(store, new CompanyService(), Session);
        await vm.InitializeAsync();
        vm.SelectedTemplate = JewelryLabelTemplates.Resolve(PrintTemplateCodes.JewelryLabelL28x12);
        vm.LoadReferenceTemplateCommand.Execute(null);
        Assert.Contains(vm.Objects, obj => obj.Source.Name == "Distributor");
        Assert.Equal(0, store.Saves);
        Assert.Null(vm.DialogResult);
        Assert.NotEmpty(vm.GetFrxBytes());
        vm.SaveCommand.Execute(null);
        Assert.Equal(PrintTemplateCodes.JewelryLabelL28x12, store.LastSavedCode);
    }

    [Fact]
    public async Task Changing_selector_without_loading_cannot_save_over_the_other_template()
    {
        var store = new TemplateStore();
        var vm = new LabelDesignerViewModel(store, new CompanyService(), Session);
        vm.SelectedTemplate = JewelryLabelTemplates.Resolve(PrintTemplateCodes.JewelryLabel28x12);
        await vm.InitializeAsync();
        vm.SelectedTemplate = JewelryLabelTemplates.Resolve(PrintTemplateCodes.JewelryLabelL28x12);
        vm.SaveCommand.Execute(null);
        Assert.Equal(PrintTemplateCodes.JewelryLabel28x12, store.LastSavedCode);
    }

    [Theory]
    [InlineData(PrintTemplateCodes.JewelryLabel28x12)]
    [InlineData(PrintTemplateCodes.JewelryLabelL28x12)]
    public void Print_dialog_offers_two_templates_and_honors_explicit_selection(string code)
    {
        var vm = new LabelPrintDialogViewModel(1, templateCode: code);
        Assert.Equal(2, vm.Templates.Count);
        Assert.Equal(code, vm.SelectedTemplate.Code);
        vm.SelectedTemplate = vm.Templates.Single(option => option.Code != code);
        Assert.NotEqual(code, vm.SelectedTemplate.Code);
    }

    [Fact]
    public async Task Print_test_uses_draft_for_loaded_template_and_saved_content_for_other_template()
    {
        var store = new TemplateStore();
        var vm = new LabelDesignerViewModel(store, new CompanyService(), Session);
        vm.SelectedTemplate = JewelryLabelTemplates.All[0];
        await vm.InitializeAsync();
        var edited = vm.Report!.AllObjects.OfType<TextObject>().First();
        edited.Text = "DRAFT";
        using var draft = await vm.CreatePrintTestReportAsync(Profile, PrintTemplateCodes.JewelryLabel28x12, false);
        using var draftPage = draft.PreparedPages.GetPage(0);
        Assert.Contains(draftPage.AllObjects.OfType<TextObject>(), text => text.Text == "DRAFT");
        using var other = await vm.CreatePrintTestReportAsync(Profile, PrintTemplateCodes.JewelryLabelL28x12, true);
        using var otherPage = other.PreparedPages.GetPage(0);
        Assert.Contains(otherPage.AllObjects.OfType<TextObject>(), text => text.Name == "Distributor");
        Assert.DoesNotContain(otherPage.AllObjects.OfType<TextObject>(), text => text.Text == "DRAFT");
        Assert.Equal(PrintTemplateCodes.JewelryLabelL28x12, store.LastRequestedCode);
        Assert.Equal(0, store.Saves);
    }

    [Theory]
    [InlineData(PrintTemplateCodes.JewelryLabel28x12, false)]
    [InlineData(PrintTemplateCodes.JewelryLabelL28x12, true)]
    public async Task Production_print_loads_the_selected_record(string code, bool qrExpected)
    {
        var store = new TemplateStore();
        var item = new ManualFinishedProductItem("B600022", "OTHER", "NHẪN", null, "9999", null,
            "Chỉ", 1m, 0m, 1m, null, null, 0m, 0m, 0m, null, null, null, null);
        using var report = await new FinishedGoodsLabelPrintService(store)
            .CreatePreparedReportAsync([item], DateTime.Today, Session, Profile, templateCode: code);
        using var page = report.PreparedPages.GetPage(0);
        Assert.Equal(code, store.LastRequestedCode);
        var barcode = Assert.Single(page.AllObjects.OfType<BarcodeObject>());
        Assert.Equal(qrExpected, barcode.Barcode is BarcodeQR);
        Assert.Equal("B600022", barcode.Text);
    }

    private static readonly AuthenticatedSession Session = new(1, "admin", "Admin", "ADMIN", 1, "CN", "Chi nhánh");
    private static readonly CompanyProfileDto Profile = new(1, "KAZUKO", null, "Địa chỉ công ty", null, "01:2026/KZJ",
        "CTY TNHH VBĐ THÀNH ĐẠT", "SN 778 Tổ DP Chiến Thắng - 28P - T. Điện Biên",
        "CTY KAZUKO GOLD", "925/15 Âu Cơ - Tân Sơn Nhì - TPHCM", "Vietnam");

    private sealed class CompanyService : ICompanyProfileService
    {
        public Task<CompanyProfileDto> GetAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) => Task.FromResult(Profile);
        public Task SaveAsync(CompanyProfileUpdateRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class TemplateStore : IPrintTemplateStore
    {
        public int Saves { get; private set; }
        public string? LastRequestedCode { get; private set; }
        public string? LastSavedCode { get; private set; }
        public Task<PrintTemplateDto> GetByCodeAsync(string code, AuthenticatedSession session, CancellationToken cancellationToken = default)
        {
            LastRequestedCode = code;
            string resource = string.Equals(code, PrintTemplateCodes.JewelryLabelL28x12, StringComparison.OrdinalIgnoreCase)
                ? "Kazuko.Warehouse.LabelL.frx" : "Kazuko.Warehouse.LabelLegacy.frx";
            using var source = typeof(FinishedGoodsLabelPrintService).Assembly.GetManifestResourceStream(resource)!;
            using var bytes = new MemoryStream(); source.CopyTo(bytes);
            return Task.FromResult(new PrintTemplateDto(1, code, "Mẫu L", bytes.ToArray(), "hash", DateTime.Today, null));
        }
        public Task<PrintTemplateDto> SaveAsync(string code, string name, byte[] content, string? notes, AuthenticatedSession session, CancellationToken cancellationToken = default)
        {
            Saves++;
            LastSavedCode = code;
            return Task.FromResult(new PrintTemplateDto(1, code, name, content, "hash", DateTime.Today, notes));
        }
    }
}
