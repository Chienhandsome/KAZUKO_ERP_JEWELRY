using System.Windows;
using Kazuko.Core.Sales;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Sales;

public sealed class CraftOrderDeliveryPresenter(Func<SalesViewModel> create,IAuthorizationService authorization):ICraftOrderDeliveryPresenter
{
    public async Task ShowAsync(long salesOrderId)
    {
        if(!authorization.Current.Allows("CRAFT_ORDER",PermissionAction.View))throw new InvalidOperationException("Bạn chưa có quyền xem đơn đặt.");
        var sales=create();
        await sales.InitializeAsync();
        await sales.CraftOrders.OpenOrderAsync(salesOrderId);
        var window=new Window
        {
            Title="Đơn đặt — giao hàng / tất toán",Width=1250,Height=800,MinWidth=1000,MinHeight=650,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive),
            Content=new CraftOrderTabView{DataContext=sales.CraftOrders}
        };
        window.SetResourceReference(Window.BackgroundProperty,"AppBackgroundBrush");
        window.Closing+=(_,e)=>{if(sales.CraftOrders.IsLoading)e.Cancel=true;};
        window.ShowDialog();
    }
}
