using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Printing;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Sales.ReceiptDesigner;

namespace Kazuko.Modules.Sales.Services;

public sealed class ReceiptDesignerService(IPrintTemplateStore templates, ReceiptTemplatePreviewService preview,
    IAuthorizationService authorization) : IReceiptDesignerService
{
    public async Task<bool> OpenDesignerAsync(string templateCode, AuthenticatedSession session)
    {
        await authorization.DemandAsync([new("SYS_SETTINGS", PermissionAction.Edit)]);
        var vm = new ReceiptDesignerViewModel(templateCode, session, templates, preview, authorization);
        var dialog = new ReceiptDesignerDialog(vm) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true;
    }
}
