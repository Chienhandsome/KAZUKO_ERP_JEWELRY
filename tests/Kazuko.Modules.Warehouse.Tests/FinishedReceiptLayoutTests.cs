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
                    var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(),Stub<IProductionApplicationService>(),new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>());
                    var window=new ProductionFinishedReceiptDialog(vm){Width=1366,Height=820,WindowState=WindowState.Normal};
                    var root=(FrameworkElement)window.Content;((Panel)root).Background=(Brush)app.Resources["AppBackgroundBrush"];
                    root.Measure(new Size(1334,788));root.Arrange(new Rect(0,0,1334,788));root.UpdateLayout();
                    var buttons=Descendants(root).OfType<Button>().Where(b=>b.Content as string=="Ghi sổ nhập kho").ToArray();Assert.Equal(2,buttons.Length);
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
    public class NoDatabase:DispatchProxy {protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException();}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
