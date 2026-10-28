using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Warehouse;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Warehouse;

public sealed class ProductionFinishedReceiptPresenter(IWarehouseRepository warehouse,IProductionQueryRepository queries,
    IProductionApplicationService application,ICurrentSessionAccessor sessions,IPrintTemplateStore templates,ICompanyProfileService company,
    IGoldTypeCatalogService goldTypes,IFinishedReceiptDraftStore drafts,IFinishedReceiptDocuments documents,IAuthorizationService authorization,
    ICraftOrderApplicationService? craftOrders=null,Func<long,Task>? openDelivery=null) : IProductionFinishedReceiptPresenter
{
    public bool Show(long? orderId=null)
    {
        if(!authorization.Current.Allows("INV_FINISHED_RECEIPT",PermissionAction.Add))throw new InvalidOperationException("Bạn chưa có quyền nhập thành phẩm.");
        var session=sessions.Current??throw new InvalidOperationException("Chưa đăng nhập.");
        var vm=new ProductionFinishedReceiptDialogViewModel(warehouse,queries,application,session,templates,company,goldTypes,drafts,documents,orderId,craftOrders,openDelivery);
        var window=new ProductionFinishedReceiptDialog(vm){Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive)};
        window.ShowDialog();return vm.IsPosted;
    }
}
