using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Kazuko.Modules.PriceBoard;

public sealed record TvMonitor(string Device,string Name,int Left,int Top,int Width,int Height,bool Primary)
{
    public override string ToString()=>Name;
    public static IReadOnlyList<TvMonitor> GetAll()
    {
        var result=new List<TvMonitor>();
        MonitorCallback callback=(IntPtr monitor,IntPtr dc,ref NativeRect bounds,IntPtr data)=>
        {
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(GetMonitorInfo(monitor,ref info))result.Add(new(info.Device,$"Màn hình {result.Count+1} · {bounds.Right-bounds.Left} × {bounds.Bottom-bounds.Top}{(info.Flags==1?" (chính)":"")}",bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,info.Flags==1));return true;
        };
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero);return result;
    }
    [StructLayout(LayoutKind.Sequential)]internal struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref NativeRect bounds,IntPtr data);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}

public sealed class PriceBoardTvWindow : Window
{
    private readonly WebView2 _browser=new();
    private string _url;
    public PriceBoardTvWindow(string url,TvMonitor monitor)
    {
        _url=url;
        Title="Bảng giá vàng TV — Esc để đóng";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        Content=_browser;Width=960;Height=540;WindowStartupLocation=WindowStartupLocation.Manual;
        Loaded+=async(_,_)=>
        {
            SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,monitor.Left,monitor.Top,monitor.Width,monitor.Height,0x0040);
            try
            {
                await TvBrowser.InitializeAsync(_browser);if(!IsLoaded)return;
                _browser.CoreWebView2.WebMessageReceived+=(_,e)=>{if(e.TryGetWebMessageAsString()=="close-tv")Close();};
                _browser.Source=new Uri(_url);
            }
            catch(Exception ex){MessageBox.Show("Không mở được bảng giá TV: "+ex.Message,Title);Close();}
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};Closed+=(_,_)=>_browser.Dispose();
    }
    public void Navigate(string url){_url=url;if(_browser.CoreWebView2 is not null)_browser.Source=new Uri(url);}
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
internal static class TvBrowser
{
    public static async Task InitializeAsync(WebView2 browser)
    {
        var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KazukoERP","WebView2-TV");
        var environment=await CoreWebView2Environment.CreateAsync(null,folder);
        await browser.EnsureCoreWebView2Async(environment);
        browser.CoreWebView2.Settings.AreDevToolsEnabled=false;browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
        browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
    }
}
