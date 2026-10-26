using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class WorkShiftCatalogTests
{
    private static (IHumanResourcesRepository Repo, ShiftRepositoryFake Fake, CurrentSessionAccessor Session) Setup()
    {
        var repo = DispatchProxy.Create<IHumanResourcesRepository, ShiftRepositoryFake>();
        var session = new CurrentSessionAccessor(); session.Set(new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"));
        return (repo,(ShiftRepositoryFake)repo,session);
    }

    [Fact]
    public async Task Catalog_loads_inactive_shifts_and_saves_selected_id_times_and_active_state()
    {
        var (repo,fake,session) = Setup();
        var vm = new WorkShiftCatalogViewModel(repo,session);
        await vm.LoadAsync();
        Assert.False(fake.ActiveOnly);
        Assert.Equal(2,vm.Items.Count);
        vm.SelectedItem = vm.Items[1];
        Assert.False(vm.Editor!.IsActive);
        vm.Editor.Name = "Ca tối mới"; vm.Editor.StartTimeText = "22:00"; vm.Editor.EndTimeText = "06:00"; vm.Editor.IsActive = true;
        vm.SaveCommand.Execute(null);
        Assert.Equal(2,fake.LastSave!.Id);
        Assert.True(fake.LastSave.IsActive);
        Assert.Equal(TimeSpan.FromHours(22),fake.LastSave.StartTime);
        Assert.Equal("Ca tối mới",vm.SelectedItem!.Name);
        Assert.True(vm.SelectedItem.IsActive);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task New_opens_separate_editor_and_preserves_existing_draft_when_cancelled()
    {
        var (repo,fake,session) = Setup();
        var vm = new WorkShiftCatalogViewModel(repo,session); await vm.LoadAsync();
        vm.Editor!.Name = "Chưa lưu";
        int opens = 0;
        vm.RequestCreate = () => { opens++; return Task.FromResult<WorkShiftItem?>(null); };
        vm.NewCommand.Execute(null);
        Assert.Equal(1,opens); Assert.Equal("Chưa lưu",vm.Editor.Name); Assert.Null(fake.LastSave);
        var created = new WorkShiftItem(3,"Ca mới",null,null,3,true);
        vm.RequestCreate = () => { fake.Rows.Add(created); return Task.FromResult<WorkShiftItem?>(created); };
        vm.NewCommand.Execute(null);
        Assert.Equal(3,vm.SelectedItem!.Id); Assert.Equal("Ca mới",vm.Editor!.Name);
    }

    [Fact]
    public async Task Validation_and_failed_save_keep_editor_open_without_losing_input()
    {
        var (repo,fake,session) = Setup();
        var editor = new ShiftEditDialogViewModel(repo,session);
        editor.Name = "Ca thử"; editor.StartTimeText = "25:00";
        await editor.SaveAsync(); Assert.Null(fake.LastSave); Assert.Contains("HH:mm",editor.ErrorMessage);
        editor.StartTimeText = "08:00"; editor.EndTimeText = "08:00";
        await editor.SaveAsync(); Assert.Null(fake.LastSave); Assert.Contains("trùng",editor.ErrorMessage);
        editor.EndTimeText = "17:00"; fake.FailSave = true;
        await editor.SaveAsync(); Assert.Contains("Lỗi thử",editor.ErrorMessage); Assert.Equal("Ca thử",editor.Name); Assert.False(editor.IsSaving);
        fake.FailSave = false;
        WorkShiftItem? saved = null; editor.RequestClose += item => saved = item;
        await editor.SaveAsync(); Assert.NotNull(saved); Assert.Null(fake.LastSave!.Id);
    }

    [Fact]
    public async Task Read_only_user_can_browse_but_cannot_create_or_edit()
    {
        var (repo,fake,session) = Setup();
        var auth = new ReadOnlyAuth();
        var vm = new WorkShiftCatalogViewModel(repo,session,auth); await vm.LoadAsync();
        Assert.Equal(2,vm.Items.Count); Assert.False(vm.NewCommand.CanExecute(null)); Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.CanEditSelected);
        await vm.Editor!.SaveAsync(); Assert.Null(fake.LastSave);
    }

    [Fact]
    public async Task Delete_requires_confirmation_and_keeps_selection_when_backend_rejects()
    {
        var (repo,fake,session) = Setup();
        var vm = new WorkShiftCatalogViewModel(repo,session); await vm.LoadAsync();
        var selected = vm.SelectedItem;
        vm.ConfirmDelete = _ => false; vm.DeleteCommand.Execute(null);
        Assert.Equal(2,fake.Rows.Count);
        vm.ConfirmDelete = _ => true; fake.FailDelete = true; vm.DeleteCommand.Execute(null);
        Assert.Equal(selected,vm.SelectedItem); Assert.Contains("chấm công",vm.Error); Assert.False(vm.IsBusy);
        fake.FailDelete = false; vm.DeleteCommand.Execute(null);
        Assert.Single(fake.Rows); Assert.DoesNotContain(vm.Items,x=>x.Id==selected!.Id);
        var readOnly = new WorkShiftCatalogViewModel(repo,session,new ReadOnlyAuth()); await readOnly.LoadAsync();
        Assert.False(readOnly.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Catalog_and_create_dialog_render_in_theme_font_and_dpi_matrix()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var (repo,_,session) = Setup();
                foreach (var theme in new[]{"Light","Dark"})
                foreach (var scale in new[]{.9,1,1.25,1.5})
                foreach (var dpi in new[]{96,144,192})
                {
                    var vm = new WorkShiftCatalogViewModel(repo,session); vm.LoadAsync().GetAwaiter().GetResult();
                    Render(new WorkShiftCatalogDialog(vm),theme,scale,dpi,740,500,"catalog");
                    var editor = new ShiftEditDialogViewModel(repo,session) { Name="Ca làm việc buổi sáng",StartTimeText="08:00",EndTimeText="17:00" };
                    Render(new ShiftEditDialog { DataContext=editor },theme,scale,dpi,400,340,"create");
                }
            }
            catch(Exception ex) { failure=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Render(Window window,string theme,double scale,int dpi,int width,int height,string name)
    {
        foreach(var resource in new[]{theme,"Typography","Controls"})window.Resources.MergedDictionaries.Insert(0,new ResourceDictionary{Source=new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml",UriKind.Relative)});
        foreach(var key in new[]{"FontSize.Caption","FontSize.Body","FontSize.Subtitle","FontSize.Heading"})window.Resources[key]=(double)window.FindResource(key)*scale;
        var content=(FrameworkElement)window.Content;
        content.Measure(new Size(width,height)); content.Arrange(new Rect(0,0,width,height));
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind); content.UpdateLayout();
        if(window is WorkShiftCatalogDialog && window.FindName("ShiftGrid") is System.Windows.Controls.DataGrid grid)
        {
            Assert.Equal(2,grid.Items.Count);
            Assert.Contains(Descendants<System.Windows.Controls.TextBlock>(grid),x=>x.Text=="08:00");
        }
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()) { dc.DrawRectangle((Brush)window.FindResource("AppBackgroundBrush"),null,new Rect(0,0,width,height)); dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,width,height)); }
        var bitmap=new RenderTargetBitmap(width*dpi/96,height*dpi/96,dpi,dpi,PixelFormats.Pbgra32); bitmap.Render(visual);
        if(Environment.GetEnvironmentVariable("KAZUKO_SHIFT_SCREENSHOTS") is {Length:>0} folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=System.IO.File.Create(System.IO.Path.Combine(folder,$"{name}-{theme}-{scale}-{dpi}.png")); encoder.Save(stream);
        }
        window.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T value)yield return value;
            foreach(var valueChild in Descendants<T>(child))yield return valueChild;
        }
    }

    private sealed class ReadOnlyAuth : IAuthorizationService
    {
        public PermissionSnapshot Current {get;}=new(false,[new(1,"CAT_EMPLOYEES",true,false,false,false,false)]);
        public event EventHandler? Changed {add{} remove{}}
        public Task RefreshAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public void Clear(){}
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements,CancellationToken cancellationToken=default)=>throw new UnauthorizedAccessException();
    }
}

public class ShiftRepositoryFake : DispatchProxy
{
    public List<WorkShiftItem> Rows {get;}=[new(1,"Ca sáng",TimeSpan.FromHours(8),TimeSpan.FromHours(12),1,true),new(2,"Ca chiều đã ngừng sử dụng",TimeSpan.FromHours(13),TimeSpan.FromHours(17),2,false)];
    public WorkShiftSaveRequest? LastSave; public bool ActiveOnly; public bool FailSave; public bool FailDelete;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method!.Name=="GetWorkShiftsAsync") { ActiveOnly=(bool)args![0]!;return Task.FromResult<IReadOnlyList<WorkShiftItem>>(Rows.ToArray()); }
        if(method.Name=="DeleteWorkShiftAsync")
        {
            if(FailDelete)return Task.FromException(new InvalidOperationException("Ca đã có dữ liệu chấm công."));
            Rows.RemoveAll(x=>x.Id==(long)args![0]!); return Task.CompletedTask;
        }
        if(method.Name=="SaveWorkShiftAsync")
        {
            if(FailSave)return Task.FromException<WorkShiftItem>(new InvalidOperationException("Lỗi thử"));
            LastSave=(WorkShiftSaveRequest)args![0]!;
            var row=new WorkShiftItem(LastSave.Id??3,LastSave.Name,LastSave.StartTime,LastSave.EndTime,1,LastSave.IsActive);
            Rows.RemoveAll(x=>x.Id==row.Id); Rows.Add(row); return Task.FromResult(row);
        }
        throw new NotSupportedException(method.Name);
    }
}
