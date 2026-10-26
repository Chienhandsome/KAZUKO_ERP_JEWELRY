using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.UserManagement;
using Kazuko.Modules.Finance;

if (args.Length != 1 || !Path.GetFileName(args[0]).StartsWith("test88_", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Supply only an explicitly restored test88_*.fdb database, never DATA.FDB.");
var path = Path.GetFullPath(args[0]);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with { DatabasePath = path, UseEmbedded = false });
await using var db = FirebirdDatabase.CreateConnection(); await db.OpenAsync();
var userId = await db.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var branchId = await db.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B WHERE EXISTS(SELECT 1 FROM CASH_FUNDS F WHERE F.BRANCH_ID=B.ID AND F.IS_ACTIVE=TRUE) ORDER BY B.ID");
var session = new AuthenticatedSession(userId, "test", "Test", "ADMIN", branchId, "TEST", "Chi nhánh thử");
var service = new FirebirdVoucherCategoryService();
var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
var code = "TEST_" + suffix;
var category = await service.SaveAsync(new(null, "OUT", code, "Chi thử " + suffix, true), session);
Check((await service.ListAsync("OUT", false, session)).Any(x => x.Id == category.Id), "reload saved category");
Check(!(await service.ListAsync("IN", true, session)).Any(x => x.Id == category.Id), "separate receipt/payment lists");
await Reject(() => service.SaveAsync(new(null, "OUT", code.ToLowerInvariant(), "Trùng mã", true), session), "duplicate normalized code");
var income = await service.SaveAsync(new(null, "IN", code, "Thu thử", true), session);
var vouchers = new FirebirdFinanceVoucherService();
var request = new ManualVoucherRequest("OUT", DateTime.Today, "CASH", 12345, "Nhân viên thử", "Kiểm tra loại thu/chi", null, category.Id);
var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS");
await Reject(() => vouchers.CreateManualVoucherAsync(request with { CategoryId = income.Id }, session), "wrong direction");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS") == before, "failed validation writes no voucher");
var voucher = await vouchers.CreateManualVoucherAsync(request, session);
Check(voucher.CategoryId == category.Id && voucher.CategoryName == category.Name && voucher.AmountVnd == 12345, "persist category with original amount");
var renamed = await service.SaveAsync(new(category.Id, "OUT", code, "Đổi tên " + suffix, true), session);
var detail = await new FirebirdFinanceDashboardRepository().GetVoucherDetailAsync(voucher.Id, session);
Check(detail?.CategoryName == category.Name, "historical category name snapshot");
Check((await new FirebirdFinanceDashboardRepository().SearchVouchersAsync("OUT", null, null, category.Name, session)).Any(x => x.Id == voucher.Id), "search snapshot category name");
await service.SaveAsync(new(category.Id, "OUT", code, renamed.Name, false), session);
Check(!(await service.ListAsync("OUT", false, session)).Any(x => x.Id == category.Id), "inactive removed from picker");
Check((await service.ListAsync("OUT", true, session)).Any(x => x.Id == category.Id), "inactive remains manageable");
await Reject(() => vouchers.CreateManualVoucherAsync(request, session), "inactive category cannot be posted");
var company2 = await db.QuerySingleAsync<long>("INSERT INTO COMPANIES(CODE,NAME) VALUES(@Code,'Test company') RETURNING ID", new { Code = "T_" + suffix });
var branch2 = await db.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME) VALUES(@CompanyId,@Code,'Test branch') RETURNING ID", new { CompanyId = company2, Code = "T_" + suffix });
var otherSession = session with { BranchId = branch2 };
Check(!(await service.ListAsync("OUT", true, otherSession)).Any(x => x.Id == category.Id), "catalog company isolation");
await Reject(() => service.SaveAsync(new(category.Id, "OUT", code, "Không được sửa", true), otherSession), "cross-company update");
await Reject(() => vouchers.CreateManualVoucherAsync(request with { CategoryId = income.Id, VoucherType = "IN" }, otherSession), "cross-company voucher");
var defaultVoucher = await vouchers.CreateManualVoucherAsync(request with { CategoryId = null }, session);
Check(defaultVoucher.CategoryId is > 0 && defaultVoucher.CategoryName == "Chi khác", "legacy callers use Other default");
await vouchers.CancelVoucherAsync(voucher.Id, "Test cancellation", session);
Check((await new FirebirdFinanceDashboardRepository().GetVoucherDetailAsync(voucher.Id, session))?.CategoryName == category.Name, "cancellation preserves category");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AUDIT_LOG WHERE EVENT_TYPE='VOUCHER_CATEGORY_SAVED' AND ENTITY_ID=@Id", new { Id = category.Id.ToString() }) >= 3, "category audit");
await new FirebirdFinanceDashboardRepository().GetExpenseBreakdownAsync(session);
Console.WriteLine("PASS database: CRUD/deactivate, direction/company guards, historical name, search, default, cancel, audit and chart query");
Check(!BackendAccess.Allows("IVoucherCategoryService", "SaveAsync", [new VoucherCategorySaveRequest(null,"OUT","A","A",true)], PermissionSnapshot.Denied), "deny ungranted catalog writes");
Check(BackendAccess.Allows("IVoucherCategoryService", "SaveAsync", [new VoucherCategorySaveRequest(null,"OUT","A","A",true)], new PermissionSnapshot(true, [])), "admin catalog access");
var addOnly = new PermissionSnapshot(false, [new(1,"FIN_CASH_OUT",true,true,false,false,false)]);
Check(BackendAccess.Allows("IVoucherCategoryService","SaveAsync",[new VoucherCategorySaveRequest(null,"OUT","A","A",true)],addOnly), "add permission permits new category");
Check(!BackendAccess.Allows("IVoucherCategoryService","SaveAsync",[new VoucherCategorySaveRequest(1,"OUT","A","A",true)],addOnly), "add permission cannot edit category");
Check(!BackendAccess.Allows("IVoucherCategoryService","ListAsync",["IN",false,session],addOnly), "out permission does not expose income catalog");
try { VoucherCategoryRules.Normalize(new(null, "OUT", "Mã lỗi", "Tên", true)); throw new Exception("Expected bad code rejection"); } catch (ArgumentException) { }
Console.WriteLine("PASS validation and backend permission policy");

var unused = await service.SaveAsync(new(null,"OUT","DEL_"+suffix,"Loại chưa dùng",true),session);
await Reject(()=>service.DeleteAsync(unused.Id,"IN",session),"cannot delete opposite direction");
await Reject(()=>service.DeleteAsync(unused.Id,"OUT",otherSession),"cannot delete another company's category");
await service.DeleteAsync(unused.Id,"OUT",session);
Check(!(await service.ListAsync("OUT",true,session)).Any(x=>x.Id==unused.Id),"unused category deleted");
await Reject(()=>service.DeleteAsync(category.Id,"OUT",session),"used category protected even after voucher cancellation");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AUDIT_LOG WHERE EVENT_TYPE='VOUCHER_CATEGORY_DELETED' AND ENTITY_ID=@id",new{id=unused.Id.ToString()})>0,"category deletion audit");
var deleteOut = new PermissionSnapshot(false,[new(1,"FIN_CASH_OUT",true,false,false,true,false)]);
Check(BackendAccess.Allows("IVoucherCategoryService","DeleteAsync",[1L,"OUT",session],deleteOut),"out delete grant");
Check(!BackendAccess.Allows("IVoucherCategoryService","DeleteAsync",[1L,"IN",session],deleteOut),"out grant cannot delete income types");
Check(!BackendAccess.Allows("IVoucherCategoryService","DeleteAsync",[1L,"OUT",session],addOnly),"add permission cannot delete categories");
Sta(() =>
{
    var fake = new FakeCategories(); var fakeVoucher = new FakeVouchers(); var accessor = new CurrentSessionAccessor(); accessor.Set(session);
    var vm = new CashVoucherDialogViewModel(fakeVoucher, accessor, [new(1, "Q", "Quỹ thử", true)], [], "OUT", fake);
    vm.LoadCategoriesAsync().GetAwaiter().GetResult();
    Check(vm.SelectedCategory?.Id == 10, "category picker default"); vm.CashAmountText = "100.000";
    vm.SaveCommand.Execute(null); Check(fakeVoucher.Request?.CategoryId == 10, "UI sends selected category");
    fakeVoucher.Request = null; vm.SelectedCategory = new(11,"IN","OTHER","Thu khác",true); vm.SaveCommand.Execute(null);
    Check(fakeVoucher.Request is null && vm.ErrorMessage.Length > 0, "UI rejects opposite category");
    var manager = new VoucherCategoryViewModel(fake, session, "OUT", null);
    manager.Code = "NEW"; manager.Name = "Chi mẫu"; manager.SaveCommand.Execute(null);
    Check(fake.LastSave?.Name == "Chi mẫu", "manager saves edits");
    // Exercise the actual NewCommand -> modal editor -> Save -> list selection path.
    var managerWindow = new VoucherCategoryDialog(manager) { Opacity = 0, ShowActivated = false };
    foreach (var resource in new[] { "Light", "Typography", "Controls" })
        managerWindow.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
    managerWindow.Show();
    var opened = 0;
    EventManager.RegisterClassHandler(typeof(VoucherCategoryEditorDialog), FrameworkElement.LoadedEvent,
        new RoutedEventHandler((sender, _) =>
        {
            var editor = (VoucherCategoryEditorDialog)sender;
            editor.Opacity = 0;
            var editorVm = (VoucherCategoryViewModel)editor.DataContext;
            opened++;
            editorVm.Code = "NEW_UI"; editorVm.Name = "Loại tạo từ nút thêm";
            editor.Dispatcher.BeginInvoke(new Action(() => editorVm.SaveCommand.Execute(null)));
        }));
    manager.NewCommand.Execute(null);
    Check(opened == 1, "new button opens a separate editor");
    Check(fake.LastSave is { Id: null, Code: "NEW_UI" }, "new dialog creates, not updates existing category");
    Check(manager.SelectedItem?.Code == "NEW_UI", "new category is selected after save");
    managerWindow.Close();
    var deleteId = manager.SelectedItem!.Id;
    manager.ConfirmDelete = _ => false; manager.DeleteCommand.Execute(null);
    Check(manager.Items.Any(x=>x.Id==deleteId),"cancel confirmation preserves category");
    manager.ConfirmDelete = _ => true; fake.FailDelete = true; manager.DeleteCommand.Execute(null);
    Check(manager.SelectedItem?.Id==deleteId && manager.Error.Length>0 && !manager.IsBusy,"failed delete retains selection");
    fake.FailDelete = false; manager.DeleteCommand.Execute(null);
    Check(manager.Items.All(x=>x.Id!=deleteId) && !manager.DeleteCommand.CanExecute(null),"successful delete reloads and clears editor selection");
    foreach (var theme in new[] { "Light", "Dark" })
    foreach (var scale in new[] { 1d, 1.5d })
    {
        var screenVm = new CashVoucherDialogViewModel(fakeVoucher, accessor, [new(1,"Q","Quỹ tiền mặt",true)], [], "OUT", fake);
        screenVm.LoadCategoriesAsync().GetAwaiter().GetResult();
        Render(new CashVoucherDialog { DataContext = screenVm }, theme, scale, 620, 700, "voucher");
        manager.LoadAsync().GetAwaiter().GetResult(); manager.SelectedItem = manager.Items.FirstOrDefault();
        Render(new VoucherCategoryDialog(manager), theme, scale, 820, 600, "categories");
        Render(new VoucherCategoryEditorDialog(manager.CreateNewEditor()), theme, scale, 500, 430, "category-new");
    }
    Console.WriteLine("PASS UI: selected category, validation, manager save, Light/Dark at 100%/150%");
});
Console.WriteLine("ALL FINANCE CATEGORY CHECKS PASSED; live database was not used.");
static void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); }
static async Task Reject(Func<Task> action, string label)
{
    try { await action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; }
    throw new Exception("Expected rejection: " + label);
}
static void Sta(Action action)
{
    Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
}
static void Render(Window window, string theme, double scale, int width, int height, string name)
{
    foreach (var resource in new[] { theme, "Typography", "Controls" })
        window.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
    foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading" }) window.Resources[key] = (double)window.FindResource(key) * scale;
    var content = (FrameworkElement)window.Content; content.Measure(new Size(width-24,height-48)); content.Arrange(new Rect(0,0,width-24,height-48)); content.UpdateLayout();
    if (window.FindName("CategoryGrid") is System.Windows.Controls.DataGrid grid)
        Check(grid.Columns[1].ActualWidth >= 150, "category name column remains visible");
    var drawing = new DrawingVisual(); using(var dc = drawing.RenderOpen())
    { dc.DrawRectangle((Brush)window.FindResource("AppBackgroundBrush"),null,new Rect(0,0,width-24,height-48)); dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,width-24,height-48)); }
    var bitmap = new RenderTargetBitmap(width-24,height-48,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing);
    if(Environment.GetEnvironmentVariable("KAZUKO_FINANCE_SCREENSHOTS") is { Length:>0 } folder)
    {
        Directory.CreateDirectory(folder); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(folder,$"{name}-{theme}-{scale}.png")); encoder.Save(output);
    }
    window.Close();
}
sealed class FakeCategories : IVoucherCategoryService
{
    public bool FailDelete;
    public Task DeleteAsync(long id,string voucherType,AuthenticatedSession session,CancellationToken ct=default)
    { if(FailDelete)return Task.FromException(new InvalidOperationException("Loại đã sử dụng")); _items.RemoveAll(x=>x.Id==id && x.VoucherType==voucherType); return Task.CompletedTask; }
    public VoucherCategorySaveRequest? LastSave;
    private readonly List<VoucherCategory> _items = [new(10,"OUT","OTHER","Chi khác",true), new(20,"IN","OTHER","Thu khác",true)];
    public Task<IReadOnlyList<VoucherCategory>> ListAsync(string voucherType,bool includeInactive,AuthenticatedSession session,CancellationToken ct=default) =>
        Task.FromResult<IReadOnlyList<VoucherCategory>>(_items.Where(x => x.VoucherType==voucherType && (includeInactive || x.IsActive)).ToArray());
    public Task<VoucherCategory> SaveAsync(VoucherCategorySaveRequest request,AuthenticatedSession session,CancellationToken ct=default)
    {
        LastSave=request;
        var saved = new VoucherCategory(request.Id ?? _items.Max(x => x.Id)+1,request.VoucherType,request.Code,request.Name,request.IsActive);
        _items.RemoveAll(x => x.Id==saved.Id); _items.Add(saved);
        return Task.FromResult(saved);
    }
}
sealed class FakeVouchers : IFinanceVoucherService
{
    public ManualVoucherRequest? Request;
    public Task<CashVoucherDetail> CreateManualVoucherAsync(ManualVoucherRequest request,AuthenticatedSession session,CancellationToken ct=default)
    { Request=request; return Task.FromResult(new CashVoucherDetail(1,"PC",DateTime.Today,"OUT","CASH",request.AmountVnd,null,null,null,null,null,"Test","POSTED",request.CategoryId,"Chi khác")); }
    public Task CancelVoucherAsync(long id,string reason,AuthenticatedSession session,CancellationToken ct=default) => Task.CompletedTask;
}
