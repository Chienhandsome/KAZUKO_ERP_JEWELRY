using System.Reflection;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedReceiptLayoutTests
{
    [Fact]
    public void Form_keeps_posting_buttons_inside_viewport_in_both_themes_and_large_fonts()
    {
        Exception? error=null;var thread=new Thread(()=>
        {
            try
            {
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                foreach(var theme in new[]{"Light","Dark"})foreach(var scale in new[]{.9,1.5})
                {
                    app.Resources.MergedDictionaries.Clear();
                    foreach(var resource in new[]{theme,"Typography","Controls"})app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Kazuko.Shared.UI;component/Themes/"+resource+".xaml")});
                    foreach(var font in new[]{"Caption","Body","Subtitle","Heading","Display"})app.Resources["FontSize."+font]=(double)app.Resources["FontSize."+font]*scale;
                    var catalog=new Kazuko.Shared.UI.Controls.CatalogDialog{ShowContactDetails=false,DataContext=new CatalogPreview()};
                    var catalogRoot=(FrameworkElement)catalog.Content;
                    catalogRoot.Measure(new Size(628,380));catalogRoot.Arrange(new Rect(0,0,628,380));catalogRoot.UpdateLayout();
                    foreach(var caption in new[]{"Thêm mới","Sửa","Xóa","Chọn","Đóng"})
                        Assert.Single(Descendants(catalogRoot).OfType<Button>(),b=>b.Content as string==caption);
                    Assert.DoesNotContain(Descendants(catalogRoot).OfType<TextBlock>(),t=>t.IsVisible&&t.Text=="Điện thoại");
                    var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(),Stub<IProductionApplicationService>(),new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>());
                    vm.LineProgress.Add(new("Nhẫn mẫu dài để kiểm tra bố cục","750",2,2,1));
                    vm.LineProgress.Add(new("Dây chuyền","9999",1,1,0));
                    var window=new ProductionFinishedReceiptDialog(vm){Width=1366,Height=820,WindowState=WindowState.Normal};
                    var root=(FrameworkElement)window.Content;((Panel)root).Background=(Brush)app.Resources["AppBackgroundBrush"];
                    root.Measure(new Size(1334,788));root.Arrange(new Rect(0,0,1334,788));root.UpdateLayout();
                    var buttons=Descendants(root).OfType<Button>().Where(b=>b.Content as string=="Ghi sổ nhập kho").ToArray();Assert.Single(buttons);
                    var catalogButtons=Descendants(root).OfType<Button>().Where(b=>b.Content as string=="Danh mục").ToArray();
                    Assert.Equal(2,catalogButtons.Length);
                    Assert.DoesNotContain(Descendants(root).OfType<Button>(),b=>b.Content as string=="Thêm KH");
                    Assert.False(Descendants(root).OfType<Expander>().Single(x=>x.Header as string=="Tiến độ từng mẫu: đơn đặt / SX / nhập").IsExpanded);
                    foreach(var caption in new[]{"Bỏ món đang nhập","Kiểm tra kết quả","Mở đơn / Giao hàng"})
                        Assert.Equal(Visibility.Collapsed,Descendants(root).OfType<Button>().Single(x=>x.Content as string==caption).Visibility);
                    foreach(var button in buttons){var point=button.TransformToAncestor(root).Transform(new Point());Assert.True(point.Y+button.ActualHeight<=789,"Posting action is cut off vertically.");}
                    if(theme=="Light"&&scale==1.5)
                    {
                        var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../artifacts/production-receipt/layout-light-150.png"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        var bitmap=new RenderTargetBitmap(1334,788,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(output);encoder.Save(file);
                    }
                }
            }
            catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static T Stub<T>() where T:class=>DispatchProxy.Create<T,NoDatabase>();
    public sealed class CatalogPreview
    {
        public string SearchHint => "Tìm nhóm hàng hoặc ký hiệu";
        public string Query { get; set; } = "";
        public string Error => "";
        public bool IsBusy => false;
        public bool CanChoose => true;
        public string SelectedContext => "Nhóm hàng thành phẩm";
        public string SelectedGroupName => "";
        public FinishedProductCatalogItem[] Rows { get; } = [new(1,"NHAN","Nhẫn thành phẩm tên dài",true)];
        public FinishedProductCatalogItem? Selected { get; set; } = new(1,"NHAN","Nhẫn thành phẩm tên dài",true);
    }
    public class NoDatabase:DispatchProxy {protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException();}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
