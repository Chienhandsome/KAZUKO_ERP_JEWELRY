using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.PriceBoard;

public partial class PriceBoardTvSettingsWindow : Window
{
    private readonly PriceBoardTvSettingsViewModel _vm;
    public PriceBoardTvSettingsWindow(PriceBoardTvSettingsViewModel vm)
    {
        InitializeComponent();_vm=vm;DataContext=vm;vm.PropertyChanged+=Changed;
        Loaded+=async(_,_)=>
        {
            try{await TvBrowser.InitializeAsync(PreviewBrowser);}
            catch(Exception ex){MessageBox.Show("Không mở được bản xem thử. Kiểm tra WebView2 Runtime: "+ex.Message,Title);}
            // LAN remains available when the embedded browser runtime is unavailable.
            if(IsLoaded)await vm.InitializeAsync();
        };
        Closing+=(_,e)=>{if(vm.IsBusy)e.Cancel=true;};
        Closed+=(_,_)=>{vm.PropertyChanged-=Changed;vm.Dispose();PreviewBrowser.Dispose();};
        BusyText.Visibility=Visibility.Collapsed;
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(_vm.PreviewUrl)&&PreviewBrowser.CoreWebView2 is not null&&!string.IsNullOrEmpty(_vm.PreviewUrl))
            PreviewBrowser.Source=new Uri(_vm.PreviewUrl);
        if(e.PropertyName==nameof(_vm.IsBusy)){Editors.IsEnabled=!_vm.IsBusy;BusyText.Visibility=_vm.IsBusy?Visibility.Visible:Visibility.Collapsed;}
    }
}
