using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Sales;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Sales.ReceiptDesigner;
using Kazuko.Modules.Sales.Services;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class ReceiptDesignerTests
{
    private static readonly AuthenticatedSession Session = new(1, "admin", "Admin", "ADMIN", 1, "BR", "Branch");
    [Theory]
    [InlineData(PrintTemplateCodes.SalesReceiptA5, "phoi_a5_ngang.frx")]
    [InlineData(PrintTemplateCodes.SalesReceiptA5Portrait, "phoi_a5_doc.frx")]
    public async Task Edit_save_reload_preserves_tables_bindings_and_user_positions(string code, string file)
    {
        var store = new Store(file);
        var auth = new Authorization();
        var vm = Create(code, store, auth);
        await vm.InitializeAsync();
        Assert.NotEmpty(vm.Sections);
        Assert.False(vm.IsDirty);
        vm.SelectedSection = vm.Sections.First(x => x.Element.Descendants("TableCell").Any());
        var table = vm.Objects.First(x => x.IsTable);
        var widths = table.Element.Elements("TableColumn").Select(x => (double?)x.Attribute("Width") ?? 75.6).ToArray();
        table.Left = 15; table.Width *= .9;
        Assert.Equal(widths[0] * .9, (double)table.Element.Elements("TableColumn").First().Attribute("Width")!, 2);
        var cell = vm.Objects.First(x => x.IsCell);
        cell.Text = "Tiêu đề đã sửa"; cell.Bold = true; cell.FontSize = 10;
        var cellName = cell.Name;
        vm.SelectedSection = vm.Sections.First(x => x.Element.Name.LocalName == "ReportSummaryBand");
        var summary = vm.Objects.FirstOrDefault(x => x.Name == "Table1") ?? vm.Objects.First(x => x.CanMove);
        summary.Left = 13; summary.Top = 12;
        var summaryName = summary.Name;
        vm.SaveCommand.Execute(null);
        Assert.Equal(true, vm.DialogResult);
        Assert.Equal(code, store.SavedCode);
        Assert.NotNull(store.Saved);
        Assert.True(auth.Demands >= 2);
        var saved = XDocument.Load(new MemoryStream(store.Saved!));
        Assert.Equal("Tiêu đề đã sửa", (string?)saved.Descendants().Single(x => (string?)x.Attribute("Name") == cellName).Attribute("Text"));
        Assert.NotEmpty(saved.Descendants("TableDataSource"));
        Assert.NotEmpty(saved.Descendants("SubreportObject"));
        // Running the real print pipeline must keep the edited position, including summary alignment.
        using var prepared = await new SalesReceiptPrintService(store).CreatePreparedReportAsync(Receipt(), Session,
            code == PrintTemplateCodes.SalesReceiptA5 ? SalesReceiptOrientation.Landscape : SalesReceiptOrientation.Portrait);
        var component = Assert.IsAssignableFrom<FastReport.ReportComponentBase>(prepared.FindObject(summaryName));
        Assert.Equal(13, component.Left, 2); Assert.Equal(12, component.Top, 2);
        Assert.True(prepared.PreparedPages.Count > 0);
        var reopened = Create(code, store, auth); await reopened.InitializeAsync();
        Assert.Contains((byte)'<', reopened.GetFrxBytes());
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public async Task Invalid_expression_and_revoked_edit_permission_cannot_replace_saved_template()
    {
        var store = new Store("phoi_a5_ngang.frx"); var auth = new Authorization();
        var vm = Create(PrintTemplateCodes.SalesReceiptA5, store, auth); await vm.InitializeAsync();
        vm.AddTextCommand.Execute(null); vm.SelectedObject!.Text = "[MissingFieldDoesNotExist + ]";
        vm.SaveCommand.Execute(null);
        Assert.Null(store.Saved); Assert.Null(vm.DialogResult); Assert.True(vm.IsDirty);
        Assert.Contains("Lưu mẫu thất bại", vm.StatusMessage);
        vm.SelectedObject.Text = "Nội dung hợp lệ"; auth.Deny = true;
        vm.SaveCommand.Execute(null);
        Assert.Null(store.Saved); Assert.Contains("Không có quyền", vm.StatusMessage);
    }

    [Fact]
    public async Task Moving_between_sections_and_cancel_do_not_write_draft()
    {
        var store = new Store("phoi_a5_ngang.frx"); var vm = Create(PrintTemplateCodes.SalesReceiptA5, store, new Authorization());
        await vm.InitializeAsync();
        var first = vm.SelectedSection; vm.AddTextCommand.Execute(null); var name = vm.SelectedObject!.Name;
        vm.SelectedObject.LeftMm = 12.5; vm.SelectedObject.Text = "Ghi chú mẫu";
        vm.SelectedSection = vm.Sections.Last(); vm.SelectedSection = first;
        Assert.Equal(12.5, vm.Objects.Single(x => x.Name == name).LeftMm, 3);
        vm.CancelCommand.Execute(null); Assert.False(vm.DialogResult); Assert.Null(store.Saved);
    }

    [Theory]
    [InlineData("Light", .9, 900, 550)]
    [InlineData("Light", 1, 1200, 760)]
    [InlineData("Dark", 1.25, 1200, 760)]
    [InlineData("Dark", 1.5, 900, 550)]
    public void Designer_renders_shared_styles_at_minimum_and_normal_sizes(string theme, double scale, int width, int height) => Sta(() =>
    {
        var vm = Create(PrintTemplateCodes.SalesReceiptA5, new Store("phoi_a5_ngang.frx"), new Authorization());
        var window = new ReceiptDesignerDialog(vm);
        foreach (var resource in new[] { theme, "Typography", "Controls" })
            window.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
        foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading" })
            window.Resources[key] = (double)window.FindResource(key) * scale;
        vm.InitializeAsync().GetAwaiter().GetResult();
        vm.SelectedObject = vm.Objects.First(x => x.IsText && !x.IsCell);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width - 24, height - 48)); content.Arrange(new Rect(0, 0, width - 24, height - 48)); content.UpdateLayout();
        Assert.True(content.IsMeasureValid); Assert.NotEmpty(vm.Objects);
        var target = new RenderTargetBitmap(width - 24, height - 48, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle((Brush)window.FindResource("AppBackgroundBrush"), null, new Rect(0, 0, width - 24, height - 48));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width - 24, height - 48));
        }
        target.Render(visual);
        if (Environment.GetEnvironmentVariable("KAZUKO_DESIGNER_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = File.Create(Path.Combine(folder, $"bill-{theme}-{scale}-{width}.png")); encoder.Save(output);
        }
        window.Close();
    });
    private static ReceiptDesignerViewModel Create(string code, Store store, Authorization auth) =>
        new(code, Session, store, new ReceiptTemplatePreviewService(new SalesReceiptPrintService(store), new Company()), auth);
    private static SalesReceiptDto Receipt() => new("KAZUKO", "Branch", "Địa chỉ", "0901234567",
        new SalesCheckoutDetail(0, "BILL-MAU", "SALE", new DateTime(2026, 1, 1), 0, "Khách mẫu", null, 0, "Nhân viên", 0, "Nhân viên",
            null, null, [], [], [], new(0, 0, 0, 0, 0, 0, 0, 0, 0), null));
    private sealed class Store : IPrintTemplateStore
    {
        private byte[] _bytes;
        public byte[]? Saved { get; private set; }
        public string? SavedCode { get; private set; }
        public Store(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Assets", "examples", file))) dir = dir.Parent;
            _bytes = File.ReadAllBytes(Path.Combine(dir!.FullName, "src", "Assets", "examples", file));
        }
        public Task<PrintTemplateDto> GetByCodeAsync(string code, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintTemplateDto(1, code, "Bill A5", _bytes, "hash", DateTime.Today, null));
        public Task<PrintTemplateDto> SaveAsync(string code, string name, byte[] content, string? notes, AuthenticatedSession session, CancellationToken cancellationToken = default)
        {
            Saved = content; SavedCode = code; _bytes = content; return GetByCodeAsync(code, session, cancellationToken);
        }
    }
    private sealed class Company : ICompanyProfileService
    {
        public Task<CompanyProfileDto> GetAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) => Task.FromResult(new CompanyProfileDto(1, "KAZUKO", null, "Địa chỉ mẫu", null, null));
        public Task SaveAsync(CompanyProfileUpdateRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Authorization : IAuthorizationService
    {
        public bool Deny { get; set; }
        public int Demands { get; private set; }
        public PermissionSnapshot Current => new(!Deny, []);
        public event EventHandler? Changed { add { } remove { } }
        public void Clear() { }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            Demands++; Assert.Contains(requirements, x => x.Function == "SYS_SETTINGS" && x.Action == PermissionAction.Edit);
            return Deny ? Task.FromException(new UnauthorizedAccessException("Không có quyền sửa mẫu bill.")) : Task.CompletedTask;
        }
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
